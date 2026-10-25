// Project-paper flood solver: conservative shallow-water fluxes on a GPU grid,
// adaptive CFL substeps, dry-cell handling, and a SWE pressure velocity step.
// The original pipe-flux reference is credited in the repository documentation.
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

// Example variant: terrain-aware flood + 3 source modes.
// See TerrainFloodSimulation_Szenarien.compute for the matching compute shader.
public sealed class TerrainFloodSimulation : MonoBehaviour
{
    public ComputeShader computeShader;
    public Shader terrainShader;
    public Shader waterShader;
    [Range(32, 256)] public int resolution = 128;
    [Range(16, 128)] public int meshResolution = 64;
    public Vector2 worldSize = new Vector2(1.65f, 1.35f);
    public float heightScale = 0.12f;
    // Four-times faster scenario playback than the original 0.12 setting.
    public float timeScale = 0.48f;
    [Min(0.1f)] public float gravity = 9.81f;
    [Range(0.05f, 0.9f)] public float courantNumber = 0.45f;
    [Tooltip("Pipe cross-section A in the paper's conservative flux equation.")]
    public float pipeArea = 0.0007f;
    [Min(0f)] public float dryThreshold = 0.00001f;
    [Tooltip("Keep scenario flow directed from the left. Disable for the full four-way equation.")]
    public bool oneWayFlow = true;
    [Tooltip("Let water leave through the right edge. Disable to make the river end act as a closed basin where the level rises.")]
    public bool openDownstreamBoundary = false;
    public float rainRate = 0f;
    public float evaporation = 0.004f;
    // Maximum water depth in metres. This is intentionally high enough for overtopping.
    public float maximumWater = 15f;
    public bool renderSimulationTerrain = false;
    [Header("Terrain fitting")]
    public bool fitToReferenceTerrain = true;
    public string referenceTerrainName = "SM_Tutorial_Terrain_01";
    [Range(0.8f, 1.2f)] public float terrainCoverage = 1f;
    [Header("Collider obstacles")]
    public LayerMask obstacleLayers = ~0;
    public float obstacleScanHeight = 2f;
    public float minimumObstacleHeight = 0.04f;
    public float obstacleRefreshInterval = 0.75f;
    [Header("Terrain collision")]
    [Tooltip("Adds non-trigger MeshColliders to reference-terrain meshes that do not have one.")]
    public bool addMissingTerrainColliders = true;
    [Header("River flooding")]
    public string referenceRiverName = "SM_Tutorial_River_01";
    [Range(0.02f, 0.3f)] public float riverHalfWidth = 0.09f;
    // Keep the channel shallow enough that the increased level can spill onto terrain.
    [Range(0.02f, 0.25f)] public float riverDepth = 0.025f;
    public float riverInflow = 0.10f;
    [Header("Flood scenarios")]
    public FloodSourceMode floodSourceMode = FloodSourceMode.RiverInlet;
    [Tooltip("UV position on the simulation grid: (0,0) = bottom-left, (1,1) = top-right.")]
    public Vector2 pointSourceUV = new Vector2(0.25f, 0.45f);
    [Range(0.005f, 0.5f)] public float pointSourceRadius = 0.045f;
    [Tooltip("Multiplier for riverInflow, evaluated in simulation seconds.")]
    public AnimationCurve inflowMultiplierOverTime = AnimationCurve.Linear(0, 1, 120, 1);
    [Tooltip("Multiplier for rainRate, evaluated in simulation seconds.")]
    public AnimationCurve rainMultiplierOverTime = AnimationCurve.Linear(0, 1, 120, 1);
    [Tooltip("Carve an additional channel from the river mesh into the sampled terrain.")]
    public bool carveRiverIntoTerrain = false;

    public enum FloodSourceMode { RiverInlet = 0, Point = 1, RainOnly = 2 }
    public bool visibleLeftStartReservoir;
    [Tooltip("Rotation of the flood grid on the map. 90 degrees moves the former top entrance to the left.")]
    public float simulationYaw = 0f;
    public Color waterColor = new Color(0.015f, 0.25f, 0.72f, 0.78f);

    RenderTexture state;
    RenderTexture flux;
    RenderTexture velocity;
    Texture2D obstacleMask;
    Texture2D riverMask;
    Texture2D terrainHeightMap;
    Material terrainMaterial;
    Material waterMaterial;
    Mesh mesh;
    int initializeKernel, waterKernel, fluxKernel, applyKernel, velocityKernel;
    bool running;
    float nextObstacleRefresh;
    float scenarioTime;
    int lastObstacleCount = -1;

    public void Begin()
    {
        if (!SystemInfo.supportsComputeShaders || computeShader == null || terrainShader == null || waterShader == null)
        {
            Debug.LogError("Scenario 03 requires compute shader support and its three assigned shaders.", this);
            return;
        }
        if (fitToReferenceTerrain) FitToReferenceTerrain();
        if (addMissingTerrainColliders) EnsureReferenceTerrainColliders();
        CreateResources();
        scenarioTime = 0f;
        Dispatch(initializeKernel);
        running = true;
    }

    void FixedUpdate()
    {
        if (!running) return;
        Vector2 cellSize = worldSize / Mathf.Max(1, resolution);
        float waveSpeed = Mathf.Sqrt(Mathf.Max(0.001f, gravity * Mathf.Max(maximumWater, riverDepth)));
        float stableDt = courantNumber * Mathf.Min(cellSize.x, cellSize.y) / Mathf.Max(0.1f, waveSpeed);
        float frameDt = Mathf.Max(0f, Time.fixedDeltaTime * timeScale);
        // Keep enough CFL substeps when the accelerated playback clock is used.
        int substeps = Mathf.Clamp(Mathf.CeilToInt(frameDt / Mathf.Max(stableDt, 0.00001f)), 1, 32);
        float dt = frameDt / substeps;
        scenarioTime += frameDt;
        computeShader.SetFloat("_DeltaTime", dt);
        computeShader.SetInt("_SourceMode", (int)floodSourceMode);
        computeShader.SetVector("_PointSourceUV", new Vector4(pointSourceUV.x, pointSourceUV.y, 0, 0));
        computeShader.SetFloat("_PointSourceRadius", pointSourceRadius);
        computeShader.SetFloat("_RiverInflow", riverInflow * Mathf.Max(0f,
            inflowMultiplierOverTime != null ? inflowMultiplierOverTime.Evaluate(scenarioTime) : 1f));
        computeShader.SetInt("_OneWayFlow", oneWayFlow ? 1 : 0);
        computeShader.SetInt("_OpenDownstreamBoundary", openDownstreamBoundary ? 1 : 0);
        computeShader.SetFloat("_RainRate", rainRate * Mathf.Max(0f,
            rainMultiplierOverTime != null ? rainMultiplierOverTime.Evaluate(scenarioTime) : 1f));
        computeShader.SetFloat("_Evaporation", evaporation);
        if (Time.time >= nextObstacleRefresh) RefreshColliderObstacles();
        computeShader.SetFloat("_MaximumVelocity", Mathf.Min(cellSize.x, cellSize.y) / Mathf.Max(dt, 0.00001f) * courantNumber);
        for (int i = 0; i < substeps; i++)
        {
            Dispatch(waterKernel);
            Dispatch(fluxKernel);
            Dispatch(applyKernel);
            Dispatch(velocityKernel);
        }
    }

    /// <summary>Move the point-source inlet to a location on the terrain.</summary>
    public void SetPointSourceWorldPosition(Vector3 worldPosition)
    {
        Vector3 local = transform.InverseTransformPoint(worldPosition);
        pointSourceUV = new Vector2(
            Mathf.Clamp01(local.x / Mathf.Max(worldSize.x, 1e-6f) + .5f),
            Mathf.Clamp01(local.z / Mathf.Max(worldSize.y, 1e-6f) + .5f));
        floodSourceMode = FloodSourceMode.Point;
    }

    public void StopSimulation() => running = false;

    /// <summary>
    /// Reads the current GPU water state once for study logging. The returned
    /// values are world-space water depths in metres, indexed row-major by the
    /// simulation grid. This is intended for end-of-simulation sampling only;
    /// it is not used by the real-time solver.
    /// </summary>
    public bool TryCaptureWaterDepthMap(out float[] depths)
    {
        depths = null;
        if (state == null || !state.IsCreated()) return false;

        RenderTexture previous = RenderTexture.active;
        Texture2D snapshot = null;
        try
        {
            RenderTexture.active = state;
            snapshot = new Texture2D(resolution, resolution, TextureFormat.RGBAFloat, false, true);
            snapshot.ReadPixels(new Rect(0, 0, resolution, resolution), 0, 0, false);
            snapshot.Apply(false, false);
            Color[] pixels = snapshot.GetPixels();
            depths = new float[pixels.Length];
            float worldHeightScale = Mathf.Abs(heightScale * transform.lossyScale.y);
            for (int i = 0; i < pixels.Length; i++) depths[i] = Mathf.Max(0f, pixels[i].g * worldHeightScale);
            return true;
        }
        catch (System.Exception exception)
        {
            Debug.LogWarning("Flood water depth logging was unavailable: " + exception.Message, this);
            depths = null;
            return false;
        }
        finally
        {
            RenderTexture.active = previous;
            if (snapshot != null) Destroy(snapshot);
        }
    }

    /// <summary>Samples a previously captured depth map at a world position.</summary>
    public bool TryGetWaterDepthAtWorldPosition(float[] depths, Vector3 worldPosition, out float depth)
    {
        depth = 0f;
        if (depths == null || depths.Length != resolution * resolution) return false;
        Vector3 local = transform.InverseTransformPoint(worldPosition);
        float u = local.x / Mathf.Max(worldSize.x, 0.0001f) + .5f;
        float v = local.z / Mathf.Max(worldSize.y, 0.0001f) + .5f;
        if (u < 0f || u > 1f || v < 0f || v > 1f) return false;
        int x = Mathf.Clamp(Mathf.FloorToInt(u * resolution), 0, resolution - 1);
        int y = Mathf.Clamp(Mathf.FloorToInt(v * resolution), 0, resolution - 1);
        depth = depths[y * resolution + x];
        return true;
    }

    void CreateResources()
    {
        ReleaseResources();
        resolution = Mathf.CeilToInt(resolution / 8f) * 8;
        state = CreateTexture(RenderTextureFormat.ARGBFloat, FilterMode.Bilinear, "Flood State");
        flux = CreateTexture(RenderTextureFormat.ARGBFloat, FilterMode.Point, "Flood Flux");
        velocity = CreateTexture(RenderTextureFormat.RGFloat, FilterMode.Point, "Flood Velocity");
        obstacleMask = new Texture2D(resolution, resolution, TextureFormat.R8, false, true)
        { name = "Flood Collider Obstacles", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        RefreshColliderObstacles();
        CreateRiverMask();
        CreateTerrainHeightMap();
        initializeKernel = computeShader.FindKernel("Initialize");
        waterKernel = computeShader.FindKernel("AddWater");
        fluxKernel = computeShader.FindKernel("ComputeFlux");
        applyKernel = computeShader.FindKernel("ApplyFlux");
        velocityKernel = computeShader.FindKernel("UpdateVelocity");
        foreach (int kernel in new[] { initializeKernel, waterKernel, fluxKernel, applyKernel, velocityKernel })
        {
            computeShader.SetTexture(kernel, "StateMap", state);
            computeShader.SetTexture(kernel, "FluxMap", flux);
            computeShader.SetTexture(kernel, "VelocityMap", velocity);
            computeShader.SetTexture(kernel, "ObstacleMap", obstacleMask);
            computeShader.SetTexture(kernel, "RiverMask", riverMask);
            computeShader.SetTexture(kernel, "TerrainHeightMap", terrainHeightMap);
        }
        computeShader.SetInt("_Width", resolution);
        computeShader.SetInt("_Height", resolution);
        Vector2 cellSize = worldSize / Mathf.Max(1, resolution);
        computeShader.SetFloat("_Gravity", gravity);
        computeShader.SetFloat("_PipeArea", pipeArea);
        computeShader.SetFloat("_PipeLength", Mathf.Min(cellSize.x, cellSize.y));
        computeShader.SetFloat("_CellArea", cellSize.x * cellSize.y);
        computeShader.SetVector("_CellSize", cellSize);
        computeShader.SetFloat("_DryThreshold", dryThreshold);
        computeShader.SetInt("_OneWayFlow", oneWayFlow ? 1 : 0);
        computeShader.SetInt("_OpenDownstreamBoundary", openDownstreamBoundary ? 1 : 0);
        computeShader.SetFloat("_MaximumWater", maximumWater);
        computeShader.SetInt("_CarveRiver", carveRiverIntoTerrain ? 1 : 0);
        // Initialize() also needs this value before the first FixedUpdate().
        computeShader.SetInt("_SourceMode", (int)floodSourceMode);
        computeShader.SetVector("_PointSourceUV", new Vector4(pointSourceUV.x, pointSourceUV.y, 0, 0));
        computeShader.SetFloat("_PointSourceRadius", pointSourceRadius);
        ConfigureRiverSource();

        mesh = CreateGrid(meshResolution);
        terrainMaterial = new Material(terrainShader);
        waterMaterial = new Material(waterShader);
        SetupMaterial(terrainMaterial);
        SetupMaterial(waterMaterial);
        waterMaterial.SetColor("_Color", waterColor);
        if (renderSimulationTerrain) CreateSurface("Simulated Terrain", terrainMaterial, 0f);
        CreateSurface("Simulated Water", waterMaterial, 0.002f);
    }

    void CreateTerrainHeightMap()
    {
        // Sample ONLY the reference terrain; houses and dam colliders are excluded.
        // The existing mesh colliders are added by EnsureReferenceTerrainColliders().
        terrainHeightMap = new Texture2D(resolution, resolution, TextureFormat.RFloat, false, true)
        { name = "Flood Terrain Heights", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        float[] heights = new float[resolution * resolution];
        bool[] sampled = new bool[heights.Length];
        Transform reference = FindSceneTransform(referenceTerrainName);
        Collider[] terrainColliders = reference != null
            ? reference.GetComponentsInChildren<Collider>(true) : new Collider[0];
        if (terrainColliders.Length > 0)
        {
            Bounds bounds = terrainColliders[0].bounds;
            for (int i = 1; i < terrainColliders.Length; i++)
                bounds.Encapsulate(terrainColliders[i].bounds);

            float originY = bounds.max.y + 1f;
            float rayLength = bounds.size.y + 2f;
            float verticalScale = Mathf.Max(Mathf.Abs(heightScale * transform.lossyScale.y), 0.00001f);
            float minSample = float.PositiveInfinity;
            int missed = 0;
            for (int z = 0; z < resolution; z++)
            for (int x = 0; x < resolution; x++)
            {
                int index = z * resolution + x;
                Vector3 local = new Vector3(
                    ((x + 0.5f) / resolution - 0.5f) * worldSize.x, 0f,
                    ((z + 0.5f) / resolution - 0.5f) * worldSize.y);
                Vector3 world = transform.TransformPoint(local);
                Ray ray = new Ray(new Vector3(world.x, originY, world.z), Vector3.down);
                float bestY = float.NegativeInfinity;
                foreach (Collider collider in terrainColliders)
                {
                    if (collider == null || !collider.enabled || collider.isTrigger) continue;
                    if (collider.Raycast(ray, out RaycastHit hit, rayLength))
                        bestY = Mathf.Max(bestY, hit.point.y);
                }
                if (float.IsNegativeInfinity(bestY)) { missed++; continue; }
                float solverHeight = (bestY - transform.position.y) / verticalScale;
                heights[index] = solverHeight;
                sampled[index] = true;
                minSample = Mathf.Min(minSample, solverHeight);
            }
            if (missed > 0 && !float.IsPositiveInfinity(minSample))
            {
                for (int i = 0; i < heights.Length; i++)
                    if (!sampled[i]) heights[i] = minSample;
                Debug.LogWarning($"Flood heightmap: {missed} cells missed the terrain and use its lowest sampled height.", this);
            }
        }
        if (terrainColliders.Length == 0 || !System.Array.Exists(sampled, s => s))
        {
            for (int z = 0; z < resolution; z++)
            for (int x = 0; x < resolution; x++)
                heights[z * resolution + x] = 0.025f * (1f - (x + 0.5f) / resolution);
            Debug.LogWarning("Flood heightmap: no usable reference terrain colliders; using the old synthetic slope.", this);
        }
        terrainHeightMap.SetPixelData(heights, 0);
        terrainHeightMap.Apply(false, false);
    }

    void CreateRiverMask()
    {
        riverMask = new Texture2D(resolution, resolution, TextureFormat.R8, false, true)
        { name = "Flood River Mask", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        byte[] pixels = new byte[resolution * resolution];
        Transform river = FindSceneTransform(referenceRiverName);
        bool rasterized = false;
        if (river != null)
        {
            foreach (MeshFilter filter in river.GetComponentsInChildren<MeshFilter>(true))
            {
                Mesh sourceMesh = filter.sharedMesh;
                if (sourceMesh == null || !sourceMesh.isReadable) continue;
                Vector3[] vertices = sourceMesh.vertices;
                int[] triangles = sourceMesh.triangles;
                for (int t = 0; t + 2 < triangles.Length; t += 3)
                {
                    Vector2 a = RiverUV(filter.transform.TransformPoint(vertices[triangles[t]]));
                    Vector2 b = RiverUV(filter.transform.TransformPoint(vertices[triangles[t + 1]]));
                    Vector2 c = RiverUV(filter.transform.TransformPoint(vertices[triangles[t + 2]]));
                    RasterizeTriangle(pixels, a, b, c);
                    rasterized = true;
                }
            }
        }
        if (!rasterized)
        {
            int center = Mathf.RoundToInt(.5f * (resolution - 1));
            int halfWidth = Mathf.Max(1, Mathf.RoundToInt(riverHalfWidth * resolution));
            for (int z = Mathf.Max(0, center - halfWidth); z <= Mathf.Min(resolution - 1, center + halfWidth); z++)
            for (int x = 0; x < resolution; x++) pixels[z * resolution + x] = 255;
            Debug.LogWarning("River mesh is not readable; using the centered fallback river mask.", this);
        }
        riverMask.SetPixelData(pixels, 0);
        riverMask.Apply(false, false);
    }

    Vector2 RiverUV(Vector3 worldPoint)
    {
        Vector3 local = transform.InverseTransformPoint(worldPoint);
        return new Vector2(local.x / worldSize.x + .5f, local.z / worldSize.y + .5f);
    }

    void RasterizeTriangle(byte[] pixels, Vector2 a, Vector2 b, Vector2 c)
    {
        int x0 = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(a.x, Mathf.Min(b.x, c.x)) * resolution) - 1, 0, resolution - 1);
        int x1 = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(a.x, Mathf.Max(b.x, c.x)) * resolution) + 1, 0, resolution - 1);
        int y0 = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(a.y, Mathf.Min(b.y, c.y)) * resolution) - 1, 0, resolution - 1);
        int y1 = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(a.y, Mathf.Max(b.y, c.y)) * resolution) + 1, 0, resolution - 1);
        float denominator = (b.y - c.y) * (a.x - c.x) + (c.x - b.x) * (a.y - c.y);
        if (Mathf.Abs(denominator) < .0000001f) return;
        for (int y = y0; y <= y1; y++)
        for (int x = x0; x <= x1; x++)
        {
            Vector2 p = new Vector2((x + .5f) / resolution, (y + .5f) / resolution);
            float u = ((b.y - c.y) * (p.x - c.x) + (c.x - b.x) * (p.y - c.y)) / denominator;
            float v = ((c.y - a.y) * (p.x - c.x) + (a.x - c.x) * (p.y - c.y)) / denominator;
            if (u >= -.01f && v >= -.01f && u + v <= 1.01f) pixels[y * resolution + x] = 255;
        }
    }

    void ConfigureRiverSource()
    {
        float center = .5f;
        Transform river = FindSceneTransform(referenceRiverName);
        Renderer riverRenderer = river != null ? river.GetComponentInChildren<Renderer>(true) : null;
        if (riverRenderer != null && worldSize.y > .001f)
        {
            float localZ = transform.InverseTransformPoint(riverRenderer.bounds.center).z;
            center = Mathf.Clamp01(localZ / worldSize.y + .5f);
        }
        computeShader.SetFloat("_RiverCenter", center);
        computeShader.SetFloat("_RiverHalfWidth", riverHalfWidth);
        computeShader.SetFloat("_RiverDepth", riverDepth);
        computeShader.SetFloat("_RiverInflow", riverInflow);
        computeShader.SetInt("_VisibleLeftStartReservoir", visibleLeftStartReservoir ? 1 : 0);
    }

    void FitToReferenceTerrain()
    {
        Transform reference = FindSceneTransform(referenceTerrainName);
        if (reference == null)
        {
            Debug.LogWarning($"Flood terrain reference '{referenceTerrainName}' was not found; prefab dimensions are used.", this);
            return;
        }

        Renderer[] renderers = reference.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
        {
            Debug.LogWarning($"Flood terrain reference '{referenceTerrainName}' has no Renderer.", this);
            return;
        }

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
        bool quarterTurn = Mathf.Abs(Mathf.Sin(simulationYaw * Mathf.Deg2Rad)) > .5f;
        worldSize = quarterTurn
            ? new Vector2(Mathf.Max(.1f, bounds.size.z * terrainCoverage), Mathf.Max(.1f, bounds.size.x * terrainCoverage))
            : new Vector2(Mathf.Max(.1f, bounds.size.x * terrainCoverage), Mathf.Max(.1f, bounds.size.z * terrainCoverage));
        Quaternion gridRotation = Quaternion.Euler(0f, simulationYaw, 0f);
        transform.SetPositionAndRotation(new Vector3(bounds.center.x, transform.position.y, bounds.center.z), gridRotation);
        Debug.Log($"Flood fitted to {referenceTerrainName}: center=({bounds.center.x:F3}, {bounds.center.z:F3}), size=({worldSize.x:F3}, {worldSize.y:F3}), yaw={simulationYaw:F0} degrees. Flow: local left to right (+X).", this);
    }

    static Transform FindSceneTransform(string objectName)
    {
        Scene scene = SceneManager.GetActiveScene();
        foreach (GameObject root in scene.GetRootGameObjects())
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            if (child.name == objectName) return child;
        return null;
    }

    RenderTexture CreateTexture(RenderTextureFormat format, FilterMode filtering, string textureName)
    {
        RenderTexture texture = new RenderTexture(resolution, resolution, 0, format)
        { enableRandomWrite = true, filterMode = filtering, wrapMode = TextureWrapMode.Clamp, name = textureName };
        texture.Create();
        return texture;
    }

    void SetupMaterial(Material material)
    {
        material.SetTexture("_StateTex", state);
        material.SetFloat("_HeightScale", heightScale);
    }

    void EnsureReferenceTerrainColliders()
    {
        Transform reference = FindSceneTransform(referenceTerrainName);
        if (reference == null) return;

        foreach (MeshFilter filter in reference.GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter.sharedMesh == null) continue;
            MeshCollider collider = filter.GetComponent<MeshCollider>();
            if (collider == null) collider = filter.gameObject.AddComponent<MeshCollider>();
            collider.sharedMesh = filter.sharedMesh;
            collider.convex = false;
            collider.isTrigger = false;
            collider.enabled = true;
        }
    }

    void CreateSurface(string objectName, Material material, float yOffset)
    {
        GameObject surface = new GameObject(objectName, typeof(MeshFilter), typeof(MeshRenderer));
        surface.transform.SetParent(transform, false);
        surface.transform.localPosition = new Vector3(-worldSize.x * .5f, yOffset, -worldSize.y * .5f);
        surface.GetComponent<MeshFilter>().sharedMesh = mesh;
        surface.GetComponent<MeshRenderer>().sharedMaterial = material;
    }

    Mesh CreateGrid(int cells)
    {
        cells = Mathf.Clamp(cells, 16, 128);
        int row = cells + 1;
        Vector3[] vertices = new Vector3[row * row];
        Vector2[] uv = new Vector2[vertices.Length];
        int[] triangles = new int[cells * cells * 6];
        for (int z = 0; z <= cells; z++)
        for (int x = 0; x <= cells; x++)
        {
            int index = z * row + x;
            uv[index] = new Vector2(x / (float)cells, z / (float)cells);
            vertices[index] = new Vector3(uv[index].x * worldSize.x, 0, uv[index].y * worldSize.y);
        }
        int ti = 0;
        for (int z = 0; z < cells; z++)
        for (int x = 0; x < cells; x++)
        {
            int i = z * row + x;
            triangles[ti++] = i; triangles[ti++] = i + row; triangles[ti++] = i + 1;
            triangles[ti++] = i + 1; triangles[ti++] = i + row; triangles[ti++] = i + row + 1;
        }
        Mesh result = new Mesh { name = "Flood Simulation Grid", indexFormat = IndexFormat.UInt32 };
        result.vertices = vertices; result.uv = uv; result.triangles = triangles;
        result.bounds = new Bounds(new Vector3(worldSize.x*.5f, 0, worldSize.y*.5f), new Vector3(worldSize.x, 100f, worldSize.y));
        return result;
    }

    void Dispatch(int kernel) => computeShader.Dispatch(kernel, resolution / 8, resolution / 8, 1);

    public void RefreshColliderObstacles()
    {
        if (obstacleMask == null) return;
        byte[] pixels = new byte[resolution * resolution];
        Physics.SyncTransforms();
        Vector3 scaledSize = new Vector3(worldSize.x * Mathf.Abs(transform.lossyScale.x),
            obstacleScanHeight, worldSize.y * Mathf.Abs(transform.lossyScale.z));
        Vector3 scanCenter = transform.position + Vector3.up * (obstacleScanHeight * .5f);
        Bounds scanBounds = new Bounds(scanCenter, scaledSize);
        Collider[] colliders = FindObjectsByType<Collider>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        Transform terrainReference = FindSceneTransform(referenceTerrainName);
        Transform riverReference = FindSceneTransform(referenceRiverName);
        int detectedObstacles = 0;

        foreach (Collider obstacle in colliders)
        {
            if (obstacle == null || !obstacle.enabled || obstacle.transform.IsChildOf(transform)) continue;
            // A multi-part reference terrain must never become a wall in ObstacleMap.
            if (terrainReference != null && obstacle.transform.IsChildOf(terrainReference)) continue;
            if (riverReference != null && obstacle.transform.IsChildOf(riverReference)) continue;
            if ((obstacleLayers.value & (1 << obstacle.gameObject.layer)) == 0) continue;
            Bounds bounds = obstacle.bounds;
            if (!scanBounds.Intersects(bounds)) continue;
            if (bounds.max.y < transform.position.y + minimumObstacleHeight) continue;

            Vector3 a = transform.InverseTransformPoint(new Vector3(bounds.min.x, transform.position.y, bounds.min.z));
            Vector3 b = transform.InverseTransformPoint(new Vector3(bounds.max.x, transform.position.y, bounds.max.z));
            float minX = Mathf.Min(a.x, b.x), maxX = Mathf.Max(a.x, b.x);
            float minZ = Mathf.Min(a.z, b.z), maxZ = Mathf.Max(a.z, b.z);
            float coveredX = Mathf.Clamp01((maxX - minX) / worldSize.x);
            float coveredZ = Mathf.Clamp01((maxZ - minZ) / worldSize.y);
            if (coveredX > .8f && coveredZ > .8f) continue; // Ignore the ground/terrain collider.
            detectedObstacles++;

            int x0 = Mathf.Clamp(Mathf.FloorToInt((minX / worldSize.x + .5f) * resolution) - 1, 0, resolution - 1);
            int x1 = Mathf.Clamp(Mathf.CeilToInt((maxX / worldSize.x + .5f) * resolution) + 1, 0, resolution - 1);
            int z0 = Mathf.Clamp(Mathf.FloorToInt((minZ / worldSize.y + .5f) * resolution) - 1, 0, resolution - 1);
            int z1 = Mathf.Clamp(Mathf.CeilToInt((maxZ / worldSize.y + .5f) * resolution) + 1, 0, resolution - 1);
            float cellX = worldSize.x * Mathf.Abs(transform.lossyScale.x) / resolution;
            float cellZ = worldSize.y * Mathf.Abs(transform.lossyScale.z) / resolution;
            float horizontalTolerance = Mathf.Sqrt(cellX * cellX + cellZ * cellZ) * .65f;
            for (int z = z0; z <= z1; z++)
            for (int x = x0; x <= x1; x++)
            {
                Vector3 localSample = new Vector3(((x + .5f) / resolution - .5f) * worldSize.x,
                    transform.InverseTransformPoint(bounds.center).y,
                    ((z + .5f) / resolution - .5f) * worldSize.y);
                Vector3 sample = transform.TransformPoint(localSample);
                Vector3 closest = obstacle.ClosestPoint(sample);
                Vector2 horizontalDelta = new Vector2(sample.x - closest.x, sample.z - closest.z);
                if (horizontalDelta.magnitude <= horizontalTolerance) pixels[z * resolution + x] = 255;
            }
        }

        obstacleMask.SetPixelData(pixels, 0);
        obstacleMask.Apply(false, false);
        if (detectedObstacles != lastObstacleCount)
        {
            Debug.Log($"Flood collider mask: {detectedObstacles} obstacle collider(s) detected.", this);
            lastObstacleCount = detectedObstacles;
        }
        nextObstacleRefresh = Time.time + Mathf.Max(.1f, obstacleRefreshInterval);
    }

    void OnDestroy() => ReleaseResources();
    void ReleaseResources()
    {
        running = false;
        if (state != null) { state.Release(); Destroy(state); }
        if (flux != null) { flux.Release(); Destroy(flux); }
        if (velocity != null) { velocity.Release(); Destroy(velocity); }
        if (obstacleMask != null) Destroy(obstacleMask);
        if (riverMask != null) Destroy(riverMask);
        if (terrainHeightMap != null) Destroy(terrainHeightMap);
        if (terrainMaterial != null) Destroy(terrainMaterial);
        if (waterMaterial != null) Destroy(waterMaterial);
        if (mesh != null) Destroy(mesh);
        state = null; flux = null; velocity = null; obstacleMask = null; terrainHeightMap = null; riverMask = null;
    }
}
