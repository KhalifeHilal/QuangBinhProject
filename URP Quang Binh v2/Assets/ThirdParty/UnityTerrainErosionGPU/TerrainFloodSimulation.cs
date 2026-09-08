// Adapted from bshishov/UnityTerrainErosionGPU (MIT): shallow-water pipe model.
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

public sealed class TerrainFloodSimulation : MonoBehaviour
{
    public ComputeShader computeShader;
    public Shader terrainShader;
    public Shader waterShader;
    [Range(32, 256)] public int resolution = 128;
    [Range(16, 128)] public int meshResolution = 64;
    public Vector2 worldSize = new Vector2(1.65f, 1.35f);
    public float heightScale = 0.12f;
    public float timeScale = 0.12f;
    public float rainRate = 0f;
    public float evaporation = 0.004f;
    public float maximumWater = 0.42f;
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
    [Header("River flooding")]
    public string referenceRiverName = "SM_Tutorial_River_01";
    [Range(0.02f, 0.3f)] public float riverHalfWidth = 0.09f;
    [Range(0.02f, 0.25f)] public float riverDepth = 0.10f;
    public float riverInflow = 0.9f;
    public bool visibleLeftStartReservoir;
    [Tooltip("Rotation of the flood grid on the map. 90 degrees moves the former top entrance to the left.")]
    public float simulationYaw = 0f;
    public Color waterColor = new Color(0.015f, 0.25f, 0.72f, 0.78f);

    RenderTexture state;
    RenderTexture flux;
    Texture2D obstacleMask;
    Texture2D riverMask;
    Material terrainMaterial;
    Material waterMaterial;
    Mesh mesh;
    int initializeKernel, waterKernel, fluxKernel, applyKernel;
    bool running;
    float nextObstacleRefresh;
    int lastObstacleCount = -1;

    public void Begin()
    {
        if (!SystemInfo.supportsComputeShaders || computeShader == null || terrainShader == null || waterShader == null)
        {
            Debug.LogError("Scenario 03 requires compute shader support and its three assigned shaders.", this);
            return;
        }
        if (fitToReferenceTerrain) FitToReferenceTerrain();
        CreateResources();
        Dispatch(initializeKernel);
        running = true;
    }

    void FixedUpdate()
    {
        if (!running) return;
        float dt = Mathf.Min(Time.fixedDeltaTime * timeScale, 0.012f);
        computeShader.SetFloat("_DeltaTime", dt);
        computeShader.SetFloat("_RainRate", rainRate);
        computeShader.SetFloat("_Evaporation", evaporation);
        if (Time.time >= nextObstacleRefresh) RefreshColliderObstacles();
        Dispatch(waterKernel);
        Dispatch(fluxKernel);
        Dispatch(applyKernel);
    }

    public void StopSimulation() => running = false;

    void CreateResources()
    {
        ReleaseResources();
        resolution = Mathf.CeilToInt(resolution / 8f) * 8;
        state = CreateTexture(RenderTextureFormat.ARGBFloat, FilterMode.Bilinear, "Flood State");
        flux = CreateTexture(RenderTextureFormat.ARGBFloat, FilterMode.Point, "Flood Flux");
        obstacleMask = new Texture2D(resolution, resolution, TextureFormat.R8, false, true)
        { name = "Flood Collider Obstacles", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        RefreshColliderObstacles();
        CreateRiverMask();
        initializeKernel = computeShader.FindKernel("Initialize");
        waterKernel = computeShader.FindKernel("AddWater");
        fluxKernel = computeShader.FindKernel("ComputeFlux");
        applyKernel = computeShader.FindKernel("ApplyFlux");
        foreach (int kernel in new[] { initializeKernel, waterKernel, fluxKernel, applyKernel })
        {
            computeShader.SetTexture(kernel, "StateMap", state);
            computeShader.SetTexture(kernel, "FluxMap", flux);
            computeShader.SetTexture(kernel, "ObstacleMap", obstacleMask);
            computeShader.SetTexture(kernel, "RiverMask", riverMask);
        }
        computeShader.SetInt("_Width", resolution);
        computeShader.SetInt("_Height", resolution);
        computeShader.SetFloat("_Gravity", 9.81f);
        computeShader.SetFloat("_PipeArea", 0.0007f);
        computeShader.SetFloat("_PipeLength", 1f / resolution);
        computeShader.SetFloat("_CellArea", 1f / (resolution * resolution));
        computeShader.SetFloat("_MaximumWater", maximumWater);
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
        result.bounds = new Bounds(new Vector3(worldSize.x*.5f, heightScale*.5f, worldSize.y*.5f), new Vector3(worldSize.x, heightScale*3, worldSize.y));
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
        int detectedObstacles = 0;

        foreach (Collider obstacle in colliders)
        {
            if (obstacle == null || !obstacle.enabled || obstacle.transform.IsChildOf(transform)) continue;
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
        if (obstacleMask != null) Destroy(obstacleMask);
        if (riverMask != null) Destroy(riverMask);
        if (terrainMaterial != null) Destroy(terrainMaterial);
        if (waterMaterial != null) Destroy(waterMaterial);
        if (mesh != null) Destroy(mesh);
        state = null; flux = null; obstacleMask = null;
    }
}
