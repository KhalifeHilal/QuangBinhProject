using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class ScenarioGuidanceOverlay
{
    public static GameObject ShowObservation(string message)
    {
        GameObject root = CreateCanvas("Scenario_Observation_Hint", new Vector2(900, 145), .0014f);
        FloodLearningUI.Text("Text", root.transform, message, 40, FontStyles.Bold,
            -25, 90, TextAlignmentOptions.Center, 30);
        FloodLearningUI.PlaceInFrontOfPlayer(root.transform, 2.05f);
        root.transform.position += Vector3.up * .25f;
        return root;
    }

    public static GameObject ShowWaterFromLeft()
    {
        GameObject root = CreateCanvas("Water_From_Left_Hint", new Vector2(760, 180), .0013f);
        TMP_Text text = FloodLearningUI.Text("Text", root.transform, "WATER FROM LEFT   >>>", 46,
            FontStyles.Bold, -25, 115, TextAlignmentOptions.Center, 25);
        text.color = new Color(.2f, .75f, 1f);

        Renderer terrain = FindRenderer("SM_Tutorial_Terrain_01");
        Renderer river = FindRenderer("SM_Tutorial_River_01");
        if (terrain != null)
        {
            Bounds bounds = terrain.bounds;
            float z = river != null ? river.bounds.center.z : bounds.center.z;
            root.transform.position = new Vector3(bounds.min.x - bounds.size.x * .08f,
                bounds.max.y + .35f, z);
            if (Camera.main != null)
                root.transform.rotation = Quaternion.LookRotation(root.transform.position - Camera.main.transform.position, Vector3.up);
        }
        else FloodLearningUI.PlaceInFrontOfPlayer(root.transform, 2.05f);

        root.AddComponent<PulsingScenarioHint>();
        return root;
    }

    static GameObject CreateCanvas(string name, Vector2 size, float scale)
    {
        GameObject root = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(Image));
        root.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
        RectTransform rect = (RectTransform)root.transform;
        rect.sizeDelta = size;
        rect.localScale = Vector3.one * scale;
        Image background = root.GetComponent<Image>();
        background.color = new Color(0, 0, 0, .82f);
        background.raycastTarget = false;
        return root;
    }

    static Renderer FindRenderer(string name)
    {
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            if (child.name == name) return child.GetComponentInChildren<Renderer>(true);
        return null;
    }
}

public sealed class PulsingScenarioHint : MonoBehaviour
{
    Vector3 initialScale;
    void Awake() => initialScale = transform.localScale;
    void Update() => transform.localScale = initialScale * (1f + .13f * Mathf.Sin(Time.time * 2.2f));
}
