using UnityEngine;

// Marker used so Grip deletes only dykes created during the A1-P1 intervention.
public sealed class A1P1ScenarioDyke : MonoBehaviour
{
    Renderer[] renderers;
    Material[][] originalMaterials;
    Material fallbackBlueMaterial;

    void Awake()
    {
        renderers = GetComponentsInChildren<Renderer>(true);
        originalMaterials = new Material[renderers.Length][];
        for (int i = 0; i < renderers.Length; i++) originalMaterials[i] = renderers[i].sharedMaterials;
    }

    public void SetHovered(bool hovered, Material blueMaterial = null)
    {
        if (hovered && blueMaterial == null)
        {
            if (fallbackBlueMaterial == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null) shader = Shader.Find("Standard");
                fallbackBlueMaterial = new Material(shader) { color = new Color(.01f, .25f, 1f, 1f) };
            }
            blueMaterial = fallbackBlueMaterial;
        }
        for (int i = 0; i < renderers.Length; i++)
            if (renderers[i] != null)
                renderers[i].sharedMaterials = hovered
                    ? CreateRepeatedMaterials(blueMaterial, originalMaterials[i].Length)
                    : originalMaterials[i];
    }

    static Material[] CreateRepeatedMaterials(Material material, int count)
    {
        Material[] result = new Material[Mathf.Max(1, count)];
        for (int i = 0; i < result.Length; i++) result[i] = material;
        return result;
    }

    void OnDisable() => SetHovered(false);
    void OnDestroy() { if (fallbackBlueMaterial != null) Destroy(fallbackBlueMaterial); }
}
