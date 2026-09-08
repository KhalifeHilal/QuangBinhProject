using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public sealed class FloodPathwayScenarioA1P1 : MonoBehaviour
{
    Material pathAMaterial;
    Material pathBMaterial;
    GameObject modelRoot;

    void Awake()
    {
        Renderer terrain = FindTerrainRenderer();
        if (terrain == null)
        {
            Debug.LogWarning("A1-P1 could not find SM_Tutorial_Terrain_01.", this);
            return;
        }

        Bounds bounds = terrain.bounds;
        modelRoot = new GameObject("A1_P1_Pathway_Model");
        modelRoot.transform.SetParent(transform.parent, true);
        float y = bounds.max.y + Mathf.Max(.035f, bounds.size.y * .02f);
        float left = Mathf.Lerp(bounds.min.x, bounds.max.x, .08f);
        float right = Mathf.Lerp(bounds.min.x, bounds.max.x, .92f);
        float centerZ = bounds.center.z;

        Vector3[] pathA =
        {
            new Vector3(left, y, centerZ + bounds.size.z * .16f),
            new Vector3(bounds.center.x, y, centerZ + bounds.size.z * .12f),
            new Vector3(right, y, centerZ + bounds.size.z * .08f)
        };
        Vector3[] pathB =
        {
            new Vector3(left, y, centerZ),
            new Vector3(Mathf.Lerp(left, right, .25f), y, centerZ - bounds.size.z * .18f),
            new Vector3(Mathf.Lerp(left, right, .55f), y, centerZ - bounds.size.z * .25f),
            new Vector3(Mathf.Lerp(left, right, .78f), y, centerZ - bounds.size.z * .12f),
            new Vector3(right, y, centerZ - bounds.size.z * .04f)
        };

        Color sharedPathColor = new Color(.02f, .58f, 1f, .95f);
        pathAMaterial = CreateMaterial(sharedPathColor);
        pathBMaterial = CreateMaterial(sharedPathColor);
        CreatePath("Path_A_Short_HighGround", pathA, pathAMaterial, bounds.size.x * .012f);
        CreatePath("Path_B_Long_LowCorridor", pathB, pathBMaterial, bounds.size.x * .014f);
        CreateLabel("Path \"A\"", pathA[1] + new Vector3(0, .32f, 0));
        CreateLabel("Path \"B\"", pathB[2] + new Vector3(0, .28f, 0));
    }

    void CreatePath(string name, Vector3[] points, Material material, float width)
    {
        GameObject obj = new GameObject(name, typeof(LineRenderer));
        obj.transform.SetParent(modelRoot.transform, true);
        LineRenderer line = obj.GetComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.positionCount = points.Length;
        line.SetPositions(points);
        line.widthMultiplier = Mathf.Clamp(width, .025f, .09f);
        line.numCornerVertices = 6;
        line.numCapVertices = 6;
        line.material = material;
    }

    void CreateLabel(string textValue, Vector3 position)
    {
        GameObject canvasObject = new GameObject(textValue.Contains("A") ? "Path_A_Label" : "Path_B_Label", typeof(RectTransform), typeof(Canvas), typeof(Image));
        canvasObject.transform.SetParent(modelRoot.transform, true);
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        RectTransform rect = (RectTransform)canvasObject.transform;
        rect.sizeDelta = new Vector2(300, 100);
        rect.localScale = Vector3.one * .0012f;
        rect.position = position;
        Camera camera = Camera.main;
        if (camera != null) rect.rotation = Quaternion.LookRotation(rect.position - camera.transform.position, Vector3.up);
        Image background = canvasObject.GetComponent<Image>();
        background.color = new Color(0f, 0f, 0f, .9f);
        background.raycastTarget = false;

        GameObject labelObject = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        labelObject.transform.SetParent(canvasObject.transform, false);
        RectTransform labelRect = (RectTransform)labelObject.transform;
        labelRect.anchorMin = Vector2.zero; labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = Vector2.zero; labelRect.offsetMax = Vector2.zero;
        TMP_Text label = labelObject.GetComponent<TMP_Text>();
        label.text = textValue;
        label.fontSize = 36;
        label.fontStyle = FontStyles.Bold;
        label.alignment = TextAlignmentOptions.Center;
        label.color = Color.white;
        label.raycastTarget = false;
    }

    static Renderer FindTerrainRenderer()
    {
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            if (child.name == "SM_Tutorial_Terrain_01") return child.GetComponentInChildren<Renderer>(true);
        return null;
    }

    static Material CreateMaterial(Color color)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Sprites/Default");
        Material material = new Material(shader) { color = color };
        return material;
    }

    void OnDestroy()
    {
        if (pathAMaterial != null) Destroy(pathAMaterial);
        if (pathBMaterial != null) Destroy(pathBMaterial);
        if (modelRoot != null) Destroy(modelRoot);
    }
}
