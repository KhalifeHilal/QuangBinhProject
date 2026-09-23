using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Adds a gentle north/south elevation gradient to the tutorial terrain while
/// keeping the river centre at its original height. The mesh and its collider
/// are changed together so the visual terrain and flood obstacle scan agree.
/// </summary>
[DefaultExecutionOrder(-50)]
public sealed class TutorialTerrainTopography : MonoBehaviour
{
    [Min(0f)] public float northRise = 0.30f;
    [Min(0f)] public float southDrop = 0.10f;
    [Range(0.1f, 1f)] public float transition = 0.85f;

    bool applied;

    void OnEnable()
    {
        ApplyTopography();
    }

    /// <summary>Enables the terrain profile once the tutorial has finished.</summary>
    public static void ActivateAfterTutorial()
    {
        Scene scene = SceneManager.GetActiveScene();
        foreach (GameObject root in scene.GetRootGameObjects())
        foreach (TutorialTerrainTopography profile in root.GetComponentsInChildren<TutorialTerrainTopography>(true))
        {
            profile.enabled = true;
            profile.ApplyTopography();
        }
    }

    [ContextMenu("Apply Tutorial Topography")]
    public void ApplyTopography()
    {
        if (applied) return;
        MeshFilter terrainFilter = FindTerrainMeshFilter();
        if (terrainFilter == null)
        {
            Debug.LogWarning("Tutorial terrain mesh SM_Tutorial_Terrain_01 was not found.", this);
            return;
        }

        Mesh sourceMesh = terrainFilter.sharedMesh;
        if (sourceMesh == null)
        {
            Debug.LogWarning("Tutorial terrain mesh SM_Tutorial_Terrain_01 has no mesh assigned.", this);
            return;
        }

        if (!sourceMesh.isReadable)
        {
            Debug.LogError("SM_Tutorial_Terrain_01 must have Read/Write Enabled for topography and collider generation.", this);
            return;
        }

        Mesh mesh = terrainFilter.mesh;
        if (mesh == null || mesh.vertexCount == 0) return;

        Renderer renderer = terrainFilter.GetComponent<Renderer>();
        if (renderer == null) renderer = terrainFilter.GetComponentInChildren<Renderer>(true);
        Bounds bounds = renderer != null ? renderer.bounds : new Bounds(transform.position, Vector3.one);
        Vector3[] vertices = mesh.vertices;
        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 worldVertex = terrainFilter.transform.TransformPoint(vertices[i]);
            float northFactor = Mathf.InverseLerp(bounds.min.z, bounds.max.z, worldVertex.z);
            float signedFactor = (northFactor - 0.5f) * 2f;
            float magnitude = Mathf.Clamp01(Mathf.Abs(signedFactor) / Mathf.Max(0.01f, transition));
            magnitude = magnitude * magnitude * (3f - 2f * magnitude);
            float offset = signedFactor >= 0f ? northRise * magnitude : -southDrop * magnitude;
            worldVertex.y += offset;
            vertices[i] = terrainFilter.transform.InverseTransformPoint(worldVertex);
        }

        mesh.vertices = vertices;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        MeshCollider collider = terrainFilter.GetComponent<MeshCollider>();
        if (collider == null) collider = terrainFilter.gameObject.AddComponent<MeshCollider>();
        collider.convex = false;
        collider.isTrigger = false;
        collider.sharedMesh = null;
        collider.sharedMesh = mesh;
        applied = true;
    }

    MeshFilter FindTerrainMeshFilter()
    {
        foreach (MeshFilter filter in GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter.transform.name == "SM_Tutorial_Terrain_01") return filter;
            if (filter.sharedMesh != null && filter.sharedMesh.name.Contains("SM_Tutorial_Terrain_01")) return filter;
        }

        MeshFilter fallback = null;
        float largestArea = 0f;
        foreach (MeshFilter filter in GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter.sharedMesh == null) continue;
            Bounds bounds = filter.GetComponent<Renderer>()?.bounds ?? new Bounds(filter.transform.position, Vector3.zero);
            float area = bounds.size.x * bounds.size.z;
            if (area > largestArea)
            {
                largestArea = area;
                fallback = filter;
            }
        }
        return fallback;
    }
}
