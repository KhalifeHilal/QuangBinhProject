using System;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// A1-P4: Water Redistribution. The scenario uses the same interaction flow as
/// the previous A1 topics: observe, answer, build dykes, run the flood, then
/// report confidence and show feedback.
/// </summary>
public sealed class A1P4ScenarioFlow : MonoBehaviour
{
    enum Phase { Idle, WaitingForA, Preview, Question, BuildingDykes, Flood, Confidence }

    InputAction questA;
    GameObject floodPrefab, prompt, preview, previewHint, waterDirectionHint, activeFlood;
    TerrainFloodSimulation simulation;
    float viewingDuration, floodDuration, elapsed;
    int selectedAnswer = -1;
    Phase phase;

    public void Begin(GameObject prefab, float previewSeconds, float simulationSeconds)
    {
        SetP3ScenarioObjects(false);
        SetP4ScenarioObjects(true);
        floodPrefab = prefab;
        viewingDuration = previewSeconds;
        floodDuration = simulationSeconds;
        questA ??= new InputAction("A1-P4 Continue", InputActionType.Button,
            "<XRController>{RightHand}/primaryButton");
        questA.Enable();
        prompt = A1P4Panel.CreateStartPrompt();
        phase = Phase.WaitingForA;
    }

    public void BeginDebug(GameObject prefab, float previewSeconds, float simulationSeconds)
    {
        Begin(prefab, previewSeconds, simulationSeconds);
        StartPreview();
    }

    void Update()
    {
        if (phase == Phase.WaitingForA && questA.WasPressedThisFrame()) StartPreview();
        if (phase == Phase.BuildingDykes && questA.WasPressedThisFrame()) StartFlood();
        if (phase != Phase.Preview && phase != Phase.Flood) return;
        elapsed += Time.deltaTime;
        if (phase == Phase.Preview && elapsed >= viewingDuration) ShowQuestion();
        else if (phase == Phase.Flood && elapsed >= floodDuration) FinishFlood();
    }

    void StartPreview()
    {
        Cleanup(ref prompt); Cleanup(ref preview); Cleanup(ref previewHint);
        SetDykeConstruction(false); ClearDykes();
        preview = new GameObject("Scenario_A1_P4_WaterRedistribution");
        preview.AddComponent<FloodRedistributionScenarioA1P4>();
        previewHint = ScenarioGuidanceOverlay.ShowObservation("Observe how water accumulates and redistributes");
        elapsed = 0f;
        phase = Phase.Preview;
    }

    void ShowQuestion()
    {
        Cleanup(ref previewHint);
        phase = Phase.Question;
        A1P4Panel.ShowQuestion(StartPreview, BeginDykeBuilding);
    }

    void BeginDykeBuilding(int answer)
    {
        selectedAnswer = answer;
        Cleanup(ref preview);
        SetDykeConstruction(true);
        prompt = A1P4Panel.CreateBuildingPrompt();
        waterDirectionHint = ScenarioGuidanceOverlay.ShowWaterFromLeft();
        phase = Phase.BuildingDykes;
    }

    void StartFlood()
    {
        if (floodPrefab == null)
        {
            Debug.LogError("A1-P4 flood prefab is missing.", this);
            return;
        }
        Cleanup(ref prompt); Cleanup(ref waterDirectionHint);
        SetDykeConstruction(false);
        Transform parent = Find("FakeFloodEnvironment")?.transform;
        activeFlood = Instantiate(floodPrefab, parent, false);
        activeFlood.name = "Scenario_A1_P4_WithParticipantDykes";
        simulation = activeFlood.GetComponent<TerrainFloodSimulation>();
        if (simulation == null)
        {
            Debug.LogError("TerrainFloodSimulation is missing on the A1-P4 flood prefab.", this);
            return;
        }
        simulation.Begin();
        elapsed = 0f;
        phase = Phase.Flood;
    }

    void FinishFlood()
    {
        simulation?.StopSimulation();
        Cleanup(ref activeFlood);
        ClearDykes();
        SetP4ScenarioObjects(false);
        phase = Phase.Confidence;
        A1P4Panel.ShowConfidence(selectedAnswer);
    }

    static void SetDykeConstruction(bool enabled)
    {
        DykeManagerTutorial manager = UnityEngine.Object.FindFirstObjectByType<DykeManagerTutorial>(FindObjectsInactive.Include);
        if (manager != null)
        {
            manager.gameObject.SetActive(true);
            manager.freePlacement = enabled;
            manager.enabled = enabled;
        }
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            if (child.name.ToLowerInvariant().Contains("snapping_point")) child.gameObject.SetActive(false);
    }

    static void ClearDykes()
    {
        DykeManagerTutorial manager = UnityEngine.Object.FindFirstObjectByType<DykeManagerTutorial>(FindObjectsInactive.Include);
        if (manager == null) return;
        manager.enabled = false;
        manager.freePlacement = false;
        for (int i = manager.transform.childCount - 1; i >= 0; i--)
            UnityEngine.Object.Destroy(manager.transform.GetChild(i).gameObject);
    }

    static GameObject Find(string name)
    {
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            if (child.name == name) return child.gameObject;
        return null;
    }

    static void SetP3ScenarioObjects(bool active)
    {
        GameObject first = Find("A1P3_1");
        GameObject second = Find("A1P3_2");
        if (first != null) first.SetActive(active);
        if (second != null) second.SetActive(active);
    }

    static void SetP4ScenarioObjects(bool active)
    {
        GameObject first = Find("A1P4_1");
        GameObject second = Find("A1P4_2");
        if (first != null) first.SetActive(active);
        if (second != null) second.SetActive(active);
    }

    static void Cleanup(ref GameObject target)
    {
        if (target != null) UnityEngine.Object.Destroy(target);
        target = null;
    }

    void OnDestroy()
    {
        SetP4ScenarioObjects(false);
        questA?.Disable();
        questA?.Dispose();
        Cleanup(ref prompt); Cleanup(ref preview); Cleanup(ref previewHint);
        Cleanup(ref waterDirectionHint); Cleanup(ref activeFlood);
    }
}

/// <summary>Procedural preview showing upstream storage and redistribution.</summary>
public sealed class FloodRedistributionScenarioA1P4 : MonoBehaviour
{
    Material barrierMaterial;
    Material waterMaterial;
    Material alternateMaterial;

    void Awake()
    {
        Renderer terrain = FindRenderer("SM_Tutorial_Terrain_01");
        if (terrain == null)
        {
            Debug.LogWarning("A1-P4 could not find SM_Tutorial_Terrain_01.", this);
            return;
        }

        Bounds b = terrain.bounds;
        float y = b.max.y + .055f;
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Sprites/Default");
        barrierMaterial = new Material(shader) { color = new Color(.95f, .48f, .08f, 1f) };
        waterMaterial = new Material(shader) { color = new Color(.02f, .38f, 1f, .92f) };
        alternateMaterial = new Material(shader) { color = new Color(.08f, .78f, 1f, .95f) };

        // The orange barrier blocks the central route. The broad blue upstream
        // band grows in place, while the cyan route carries redistributed flow.
        Vector3 barrierStart = Point(b, .50f, .25f, y);
        Vector3 barrierEnd = Point(b, .50f, .75f, y);
        CreateLine("Barrier_Obstructed_Path", barrierStart, barrierEnd, barrierMaterial, .085f);
        CreateLabel("BARRIER", Point(b, .50f, .50f, y + .30f), 300);

        Vector3[] upstream =
        {
            Point(b, .06f, .47f, y + .015f),
            Point(b, .25f, .46f, y + .015f),
            Point(b, .45f, .50f, y + .015f)
        };
        CreateRoute("Upstream_Accumulation", upstream, waterMaterial, .16f, 0f);
        CreateLabel("UPSTREAM DEPTH INCREASES", Point(b, .25f, .62f, y + .33f), 540);

        Vector3[] protectedRoute =
        {
            Point(b, .55f, .50f, y + .018f),
            Point(b, .68f, .50f, y + .018f),
            Point(b, .82f, .50f, y + .018f)
        };
        CreateRoute("Reduced_Flow_Behind_Barrier", protectedRoute, waterMaterial, .055f, 0f);
        CreateLabel("LESS FLOW BEHIND BARRIER", Point(b, .68f, .62f, y + .30f), 500);

        Vector3[] redistributed =
        {
            Point(b, .43f, .22f, y + .022f),
            Point(b, .57f, .16f, y + .022f),
            Point(b, .73f, .20f, y + .022f),
            Point(b, .92f, .28f, y + .022f)
        };
        CreateRoute("Redistributed_Alternative_Path", redistributed, alternateMaterial, .10f, 1.5f);
        CreateLabel("MORE WATER ON ANOTHER PATH", Point(b, .72f, .14f, y + .30f), 570);
    }

    Vector3 Point(Bounds b, float x01, float z01, float y)
    {
        return new Vector3(Mathf.Lerp(b.min.x, b.max.x, x01), y,
            Mathf.Lerp(b.min.z, b.max.z, z01));
    }

    void CreateLine(string name, Vector3 start, Vector3 end, Material material, float width)
    {
        GameObject obj = new GameObject(name, typeof(LineRenderer));
        obj.transform.SetParent(transform, true);
        LineRenderer line = obj.GetComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.positionCount = 2;
        line.SetPosition(0, start);
        line.SetPosition(1, end);
        line.widthMultiplier = width;
        line.numCapVertices = 6;
        line.material = material;
    }

    void CreateRoute(string name, Vector3[] points, Material material, float width, float markerDelay)
    {
        GameObject obj = new GameObject(name, typeof(LineRenderer));
        obj.transform.SetParent(transform, true);
        LineRenderer line = obj.GetComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.positionCount = points.Length;
        line.SetPositions(points);
        line.widthMultiplier = width;
        line.numCornerVertices = 6;
        line.numCapVertices = 6;
        line.material = material;

        GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        marker.name = name + "_WaterMarker";
        marker.transform.SetParent(transform, true);
        marker.transform.localScale = Vector3.one * .10f;
        marker.GetComponent<Renderer>().material = material;
        UnityEngine.Object.Destroy(marker.GetComponent<Collider>());
        RedistributionFlowMarker animation = marker.AddComponent<RedistributionFlowMarker>();
        animation.Configure(points, markerDelay);
    }

    void CreateLabel(string value, Vector3 position, float width)
    {
        GameObject canvas = P2Panel.CanvasRoot(value.Replace(" ", "_") + "_Label", new Vector2(width, 88), .0012f);
        canvas.transform.SetParent(transform, true);
        canvas.transform.position = position;
        if (Camera.main != null)
            canvas.transform.rotation = Quaternion.LookRotation(canvas.transform.position - Camera.main.transform.position, Vector3.up);
        FloodLearningUI.Text("Text", canvas.transform, value, 34, FontStyles.Bold, -5, 75,
            TextAlignmentOptions.Center, 10);
    }

    static Renderer FindRenderer(string name)
    {
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            if (child.name == name) return child.GetComponentInChildren<Renderer>(true);
        return null;
    }

    void OnDestroy()
    {
        if (barrierMaterial != null) Destroy(barrierMaterial);
        if (waterMaterial != null) Destroy(waterMaterial);
        if (alternateMaterial != null) Destroy(alternateMaterial);
    }
}

public sealed class RedistributionFlowMarker : MonoBehaviour
{
    Vector3[] route;
    float delay;
    float elapsed;

    public void Configure(Vector3[] points, float startDelay)
    {
        route = points;
        delay = startDelay;
        transform.position = points[0];
    }

    void Update()
    {
        if (route == null || route.Length < 2) return;
        elapsed += Time.deltaTime;
        if (elapsed < delay) return;
        float progress = Mathf.Repeat((elapsed - delay) * .13f, 1f) * (route.Length - 1);
        int segment = Mathf.Min(Mathf.FloorToInt(progress), route.Length - 2);
        transform.position = Vector3.Lerp(route[segment], route[segment + 1], progress - segment);
    }
}

static class A1P4Panel
{
    static readonly string[] Answers =
    {
        "A) The barrier has completely solved the flooding problem.",
        "B) Increased upstream depth means the barrier is ineffective.",
        "C) The barrier obstructed one pathway, but the system responded through accumulation and redistribution, so other areas must also be examined.",
        "D) The new pathway is unrelated to the intervention because barriers only affect water immediately adjacent to them."
    };

    public static GameObject CreateStartPrompt()
    {
        GameObject root = P2Panel.CanvasRoot("A1_P4_Start", new Vector2(1050, 350));
        FloodLearningUI.Text("Title", root.transform, "A1 - P4", 40, FontStyles.Bold, -35, 55, TextAlignmentOptions.Center, 40);
        FloodLearningUI.Text("Topic", root.transform, "Water Redistribution", 44, FontStyles.Bold, -110, 65, TextAlignmentOptions.Center, 40);
        FloodLearningUI.Text("Continue", root.transform, "Press A to continue", 35, FontStyles.Normal, -220, 55, TextAlignmentOptions.Center, 40).color = new Color(.3f, .8f, 1f);
        FloodLearningUI.PlaceInFrontOfPlayer(root.transform, 2.1f);
        return root;
    }

    public static GameObject CreateBuildingPrompt()
    {
        GameObject root = P2Panel.CanvasRoot("A1_P4_Build", new Vector2(1100, 390));
        FloodLearningUI.Text("Title", root.transform, "A1 - P4  |  Build protection", 39, FontStyles.Bold, -35, 58, TextAlignmentOptions.Center, 45);
        FloodLearningUI.Text("Prompt", root.transform, "Build dykes for your answer anywhere on the terrain with the trigger.\nPoint at one and press Grip to destroy it. Press A when you are finished.", 30, FontStyles.Bold, -135, 125, TextAlignmentOptions.Center, 55).color = new Color(.35f, .82f, 1f);
        FloodLearningUI.PlaceInFrontOfPlayer(root.transform, 2.1f);
        return root;
    }

    public static void ShowQuestion(Action repeat, Action<int> answer)
    {
        GameObject root = P2Panel.CanvasRoot("Quiz_A1_P4", new Vector2(1600, 1180));
        FloodLearningUI.Text("Title", root.transform, "A1 - P4  |  Water Redistribution", 44, FontStyles.Bold, -28, 62, TextAlignmentOptions.Left, 55);
        FloodLearningUI.Text("Scenario", root.transform, "Following construction of a barrier, water depth immediately upstream increases, flooding behind the barrier decreases, and another pathway begins carrying more water.", 29, FontStyles.Normal, -105, 100, TextAlignmentOptions.TopLeft, 55);
        FloodLearningUI.Text("Question", root.transform, "Which interpretation is most appropriate?", 34, FontStyles.Bold, -220, 65, TextAlignmentOptions.TopLeft, 55);
        for (int i = 0; i < Answers.Length; i++)
        {
            int n = i;
            FloodLearningUI.Button("Answer_" + (char)('A' + i), root.transform, Answers[i], -300 - i * 145, 120, 25,
                () => { UnityEngine.Object.Destroy(root); answer(n); }, 1500);
        }
        FloodLearningUI.Button("Repeat", root.transform, "Repeat scenario", -895, 82, 29,
            () => { UnityEngine.Object.Destroy(root); repeat(); }, 390);
        FloodLearningUI.PlaceInFrontOfPlayer(root.transform, 2.25f);
    }

    public static void ShowConfidence(int answer)
    {
        GameObject root = P2Panel.CanvasRoot("Confidence_A1_P4", new Vector2(1500, 1080));
        GameObject group = FloodLearningUI.Group("ConfidenceGroup", root.transform);
        FloodLearningUI.Text("Question", group.transform, "How confident are you in your answer?", 42, FontStyles.Bold, -150, 70, TextAlignmentOptions.Center, 80);
        TMP_Text value = FloodLearningUI.Text("Value", group.transform, "60% - Moderately confident", 38, FontStyles.Bold, -270, 65, TextAlignmentOptions.Center, 100);
        Slider slider = FloodLearningUI.Slider("Slider", group.transform, -390);
        slider.minValue = 0; slider.maxValue = 5; slider.wholeNumbers = true; slider.value = 3;
        string[] labels = { "Not confident", "Low confidence", "Slightly confident", "Moderately confident", "Confident", "Very confident" };
        slider.onValueChanged.AddListener(v =>
        {
            int n = Mathf.Clamp(Mathf.RoundToInt(v), 0, 5);
            value.text = $"{n * 20}% - {labels[n]}";
        });
        FloodLearningUI.Text("Ticks", group.transform, "0                 20                 40                 60                 80                100", 25, FontStyles.Normal, -455, 45, TextAlignmentOptions.Center, 170);
        FloodLearningUI.Text("Scale", group.transform, "Not confident                                      Moderately confident                                      Very confident", 23, FontStyles.Normal, -510, 55, TextAlignmentOptions.Center, 130).color = new Color(.75f, .85f, .95f);
        FloodLearningUI.Button("Confirm", group.transform, "Confirm confidence", -650, 95, 31,
            () => ShowFeedback(root, answer, Mathf.RoundToInt(slider.value) * 20), 420);
        FloodLearningUI.PlaceInFrontOfPlayer(root.transform, 2.25f);
    }

    static void ShowFeedback(GameObject root, int answer, int confidence)
    {
        foreach (Transform child in root.transform) UnityEngine.Object.Destroy(child.gameObject);
        TMP_Text result = FloodLearningUI.Text("Result", root.transform,
            answer == 2 ? $"Correct!  |  Confidence: {confidence}%" : $"Your answer: {(char)('A' + answer)}  |  Confidence: {confidence}%",
            39, FontStyles.Bold, -105, 62, TextAlignmentOptions.Center, 65);
        result.color = answer == 2 ? new Color(.4f, 1f, .55f) : new Color(1f, .68f, .35f);
        FloodLearningUI.Text("Correct", root.transform,
            "Correct answer: C) The barrier obstructed one pathway, but the system responded through accumulation and redistribution, so other areas must also be examined.",
            30, FontStyles.Bold, -205, 175, TextAlignmentOptions.TopLeft, 70).color = new Color(.4f, 1f, .55f);
        FloodLearningUI.Text("ExplanationTitle", root.transform, "Explanation", 34, FontStyles.Bold, -415, 55, TextAlignmentOptions.Left, 70);
        FloodLearningUI.Text("Explanation", root.transform,
            "The observed pattern is consistent with a barrier successfully reducing flow through one route while increasing upstream storage and encouraging water to use another pathway. Effective evaluation therefore requires looking beyond the protected area.",
            29, FontStyles.Normal, -485, 235, TextAlignmentOptions.TopLeft, 70);
        FloodLearningUI.Button("Finish", root.transform, "Finish", -780, 82, 29,
            () => UnityEngine.Object.Destroy(root), 360);
        Debug.Log($"A1-P4 result: answer={(char)('A' + answer)}, correct={answer == 2}, confidence={confidence}");
    }
}
