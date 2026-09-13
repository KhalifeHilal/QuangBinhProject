using System;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public sealed class A1P3ScenarioFlow : MonoBehaviour
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
        SetP2ScenarioObjects(false);
        SetP3ScenarioObjects(true);
        floodPrefab = prefab;
        viewingDuration = previewSeconds;
        floodDuration = simulationSeconds;
        questA ??= new InputAction("A1-P3 Continue", InputActionType.Button, "<XRController>{RightHand}/primaryButton");
        questA.Enable();
        prompt = P3Panel.CreateStartPrompt();
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
        preview = new GameObject("Scenario_A1_P3_WesternEndpointBypass");
        preview.AddComponent<FloodBarrierScenarioA1P3>();
        previewHint = ScenarioGuidanceOverlay.ShowObservation("Observe what happens around the barrier");
        elapsed = 0f;
        phase = Phase.Preview;
    }

    void ShowQuestion()
    {
        Cleanup(ref previewHint);
        phase = Phase.Question;
        P3Panel.ShowQuestion(StartPreview, BeginDykeBuilding);
    }

    void BeginDykeBuilding(int answer)
    {
        selectedAnswer = answer;
        Cleanup(ref preview);
        SetDykeConstruction(true);
        prompt = P3Panel.CreateBuildingPrompt();
        waterDirectionHint = ScenarioGuidanceOverlay.ShowWaterFromLeft();
        phase = Phase.BuildingDykes;
    }

    void StartFlood()
    {
        if (floodPrefab == null) { Debug.LogError("A1-P3 flood prefab is missing.", this); return; }
        Cleanup(ref prompt); Cleanup(ref waterDirectionHint);
        SetDykeConstruction(false);
        Transform parent = Find("FakeFloodEnvironment")?.transform;
        activeFlood = Instantiate(floodPrefab, parent, false);
        activeFlood.name = "Scenario_A1_P3_WithParticipantDykes";
        simulation = activeFlood.GetComponent<TerrainFloodSimulation>();
        if (simulation == null) { Debug.LogError("TerrainFloodSimulation is missing.", this); return; }
        simulation.Begin();
        elapsed = 0f;
        phase = Phase.Flood;
    }

    void FinishFlood()
    {
        simulation?.StopSimulation();
        Cleanup(ref activeFlood);
        ClearDykes();
        SetP3ScenarioObjects(false);
        phase = Phase.Confidence;
        P3Panel.ShowConfidence(selectedAnswer);
    }

    static void SetDykeConstruction(bool enabled)
    {
        DykeManagerTutorial manager = UnityEngine.Object.FindFirstObjectByType<DykeManagerTutorial>(FindObjectsInactive.Include);
        if (manager != null) { manager.gameObject.SetActive(true); manager.freePlacement = enabled; manager.enabled = enabled; }
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
        for (int i = manager.transform.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(manager.transform.GetChild(i).gameObject);
    }

    static GameObject Find(string name)
    {
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            if (child.name == name) return child.gameObject;
        return null;
    }

    static void SetP2ScenarioObjects(bool active)
    {
        GameObject first = Find("A1P2_1");
        GameObject second = Find("A1P2_2");
        if (first != null) first.SetActive(active);
        if (second != null) second.SetActive(active);
    }

    static void SetP3ScenarioObjects(bool active)
    {
        GameObject first = Find("A1P3_1");
        GameObject second = Find("A1P3_2");
        if (first != null) first.SetActive(active);
        if (second != null) second.SetActive(active);
    }

    static void Cleanup(ref GameObject target) { if (target != null) UnityEngine.Object.Destroy(target); target = null; }

    void OnDestroy()
    {
        SetP3ScenarioObjects(false);
        questA?.Disable(); questA?.Dispose();
        Cleanup(ref prompt); Cleanup(ref preview); Cleanup(ref previewHint); Cleanup(ref waterDirectionHint); Cleanup(ref activeFlood);
    }
}

public sealed class FloodBarrierScenarioA1P3 : MonoBehaviour
{
    Material barrierMaterial;
    Material waterMaterial;

    void Awake()
    {
        Renderer terrain = FindRenderer("SM_Tutorial_Terrain_01");
        if (terrain == null) { Debug.LogWarning("A1-P3 could not find SM_Tutorial_Terrain_01.", this); return; }
        Bounds b = terrain.bounds;
        float y = b.max.y + .055f;
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Sprites/Default");
        barrierMaterial = new Material(shader) { color = new Color(.95f, .48f, .08f, 1f) };
        waterMaterial = new Material(shader) { color = new Color(.02f, .58f, 1f, .9f) };

        Vector3 westEnd = new Vector3(Mathf.Lerp(b.min.x, b.max.x, .45f), y, Mathf.Lerp(b.min.z, b.max.z, .32f));
        Vector3 eastEnd = new Vector3(Mathf.Lerp(b.min.x, b.max.x, .56f), y, Mathf.Lerp(b.min.z, b.max.z, .72f));
        CreateLine("Intact_Barrier", westEnd, eastEnd, barrierMaterial, .085f);
        CreateLabel("INTACT BARRIER", Vector3.Lerp(westEnd, eastEnd, .58f) + Vector3.up * .3f, 390);

        Vector3[] bypassRoute =
        {
            new Vector3(Mathf.Lerp(b.min.x, b.max.x, .12f), y + .015f, Mathf.Lerp(b.min.z, b.max.z, .47f)),
            new Vector3(Mathf.Lerp(b.min.x, b.max.x, .34f), y + .015f, Mathf.Lerp(b.min.z, b.max.z, .39f)),
            westEnd + new Vector3(-b.size.x * .035f, .015f, -b.size.z * .065f),
            westEnd + new Vector3(b.size.x * .055f, .015f, -b.size.z * .075f),
            new Vector3(Mathf.Lerp(b.min.x, b.max.x, .68f), y + .015f, Mathf.Lerp(b.min.z, b.max.z, .36f)),
            new Vector3(Mathf.Lerp(b.min.x, b.max.x, .84f), y + .015f, Mathf.Lerp(b.min.z, b.max.z, .43f))
        };
        CreateRoute("Delayed_Water_Bypass", bypassRoute);
        CreateLabel("PROTECTED AREA", new Vector3(Mathf.Lerp(b.min.x, b.max.x, .75f), y + .3f, Mathf.Lerp(b.min.z, b.max.z, .6f)), 390);
    }

    void CreateLine(string name, Vector3 start, Vector3 end, Material material, float width)
    {
        GameObject obj = new GameObject(name, typeof(LineRenderer));
        obj.transform.SetParent(transform, false);
        LineRenderer line = obj.GetComponent<LineRenderer>();
        line.useWorldSpace = true; line.positionCount = 2; line.SetPosition(0, start); line.SetPosition(1, end);
        line.widthMultiplier = width; line.numCapVertices = 6; line.material = material;
    }

    void CreateRoute(string name, Vector3[] points)
    {
        GameObject route = new GameObject(name, typeof(LineRenderer));
        route.transform.SetParent(transform, false);
        LineRenderer line = route.GetComponent<LineRenderer>();
        line.useWorldSpace = true; line.positionCount = points.Length; line.SetPositions(points);
        line.widthMultiplier = .055f; line.numCornerVertices = 6; line.numCapVertices = 6;
        line.material = waterMaterial;

        GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        marker.name = "Delayed_Bypass_Water"; marker.transform.SetParent(transform, false);
        marker.transform.localScale = Vector3.one * .12f;
        marker.GetComponent<Renderer>().material = waterMaterial;
        Destroy(marker.GetComponent<Collider>());
        DelayedBypassAnimation animation = marker.AddComponent<DelayedBypassAnimation>();
        animation.SetRoute(points, line);
    }

    void CreateLabel(string value, Vector3 position, float width)
    {
        GameObject canvas = P2Panel.CanvasRoot(value.Replace(" ", "_") + "_Label", new Vector2(width, 90), .0012f);
        canvas.transform.SetParent(transform, false); canvas.transform.position = position;
        if (Camera.main != null) canvas.transform.rotation = Quaternion.LookRotation(canvas.transform.position - Camera.main.transform.position, Vector3.up);
        FloodLearningUI.Text("Text", canvas.transform, value, 38, FontStyles.Bold, -5, 75, TextAlignmentOptions.Center, 10);
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
    }
}

public sealed class DelayedBypassAnimation : MonoBehaviour
{
    Vector3[] route;
    Renderer markerRenderer;
    LineRenderer routeRenderer;
    float elapsed;

    public void SetRoute(Vector3[] points, LineRenderer line)
    {
        route = points;
        routeRenderer = line;
        transform.position = points[0];
        markerRenderer = GetComponent<Renderer>();
        if (markerRenderer != null) markerRenderer.enabled = false;
        if (routeRenderer != null) routeRenderer.enabled = false;
    }

    void Update()
    {
        if (route == null || route.Length < 2) return;
        elapsed += Time.deltaTime;
        if (elapsed < 2f) return;
        if (markerRenderer != null) markerRenderer.enabled = true;
        if (routeRenderer != null) routeRenderer.enabled = true;
        float progress = Mathf.Repeat((elapsed - 2f) * .16f, 1f) * (route.Length - 1);
        int segment = Mathf.Min(Mathf.FloorToInt(progress), route.Length - 2);
        transform.position = Vector3.Lerp(route[segment], route[segment + 1], progress - segment);
    }
}

static class P3Panel
{
    static readonly string[] Answers =
    {
        "A) The barrier generated water behind itself through increased pressure.",
        "B) The western endpoint did not adequately close the available flood pathway, allowing water to bypass the barrier.",
        "C) Every barrier causes delayed flooding regardless of its position.",
        "D) The barrier was positioned too far from the buildings."
    };

    public static GameObject CreateStartPrompt()
    {
        GameObject root = P2Panel.CanvasRoot("A1_P3_Start", new Vector2(1000, 350));
        FloodLearningUI.Text("Title", root.transform, "A1 - P3", 40, FontStyles.Bold, -35, 55, TextAlignmentOptions.Center, 40);
        FloodLearningUI.Text("Topic", root.transform, "Barrier Continuity & Bypassing", 44, FontStyles.Bold, -110, 65, TextAlignmentOptions.Center, 40);
        FloodLearningUI.Text("Continue", root.transform, "Press A to continue", 35, FontStyles.Normal, -220, 55, TextAlignmentOptions.Center, 40).color = new Color(.3f, .8f, 1f);
        FloodLearningUI.PlaceInFrontOfPlayer(root.transform, 2.1f);
        return root;
    }

    public static GameObject CreateBuildingPrompt()
    {
        GameObject root = P2Panel.CanvasRoot("A1_P3_Build", new Vector2(1100, 390));
        FloodLearningUI.Text("Title", root.transform, "A1 - P3  |  Build protection", 39, FontStyles.Bold, -35, 58, TextAlignmentOptions.Center, 45);
        FloodLearningUI.Text("Prompt", root.transform, "Build dykes for your answer anywhere on the terrain with the trigger.\nPoint at one and press Grip to destroy it. Press A when you are finished.", 30, FontStyles.Bold, -135, 125, TextAlignmentOptions.Center, 55).color = new Color(.35f, .82f, 1f);
        FloodLearningUI.PlaceInFrontOfPlayer(root.transform, 2.1f);
        return root;
    }

    public static void ShowQuestion(Action repeat, Action<int> answer)
    {
        GameObject root = P2Panel.CanvasRoot("Quiz_A1_P3", new Vector2(1550, 1120));
        FloodLearningUI.Text("Title", root.transform, "A1 - P3  |  Barrier Continuity & Bypassing", 44, FontStyles.Bold, -28, 62, TextAlignmentOptions.Left, 55);
        FloodLearningUI.Text("Scenario", root.transform, "A barrier substantially reduces direct flow toward a settlement. Several minutes later, however, water enters the protected area from around its western end. The barrier itself has not overtopped or failed.", 28, FontStyles.Normal, -105, 105, TextAlignmentOptions.TopLeft, 55);
        FloodLearningUI.Text("Question", root.transform, "What is the most plausible explanation?", 34, FontStyles.Bold, -220, 65, TextAlignmentOptions.TopLeft, 55);
        for (int i = 0; i < Answers.Length; i++)
        {
            int n = i;
            FloodLearningUI.Button("Answer_" + (char)('A' + i), root.transform, Answers[i], -300 - i * 135, 112, 26, () => { UnityEngine.Object.Destroy(root); answer(n); }, 1440);
        }
        FloodLearningUI.Button("Repeat", root.transform, "Repeat scenario", -865, 82, 29, () => { UnityEngine.Object.Destroy(root); repeat(); }, 390);
        FloodLearningUI.PlaceInFrontOfPlayer(root.transform, 2.25f);
    }

    public static void ShowConfidence(int answer)
    {
        GameObject root = P2Panel.CanvasRoot("Confidence_A1_P3", new Vector2(1500, 1080));
        GameObject group = FloodLearningUI.Group("ConfidenceGroup", root.transform);
        FloodLearningUI.Text("Question", group.transform, "How confident are you in your answer?", 42, FontStyles.Bold, -150, 70, TextAlignmentOptions.Center, 80);
        TMP_Text value = FloodLearningUI.Text("Value", group.transform, "60% - Moderately confident", 38, FontStyles.Bold, -270, 65, TextAlignmentOptions.Center, 100);
        Slider slider = FloodLearningUI.Slider("Slider", group.transform, -390);
        slider.minValue = 0; slider.maxValue = 5; slider.wholeNumbers = true; slider.value = 3;
        string[] labels = { "Not confident", "Low confidence", "Slightly confident", "Moderately confident", "Confident", "Very confident" };
        slider.onValueChanged.AddListener(v => { int n = Mathf.Clamp(Mathf.RoundToInt(v), 0, 5); value.text = $"{n * 20}% - {labels[n]}"; });
        FloodLearningUI.Text("Ticks", group.transform, "0                 20                 40                 60                 80                100", 25, FontStyles.Normal, -455, 45, TextAlignmentOptions.Center, 170);
        FloodLearningUI.Text("Scale", group.transform, "Not confident                                      Moderately confident                                      Very confident", 23, FontStyles.Normal, -510, 55, TextAlignmentOptions.Center, 130).color = new Color(.75f, .85f, .95f);
        FloodLearningUI.Button("Confirm", group.transform, "Confirm confidence", -650, 95, 31, () => ShowFeedback(root, answer, Mathf.RoundToInt(slider.value) * 20), 420);
        FloodLearningUI.PlaceInFrontOfPlayer(root.transform, 2.25f);
    }

    static void ShowFeedback(GameObject root, int answer, int confidence)
    {
        foreach (Transform child in root.transform) UnityEngine.Object.Destroy(child.gameObject);
        TMP_Text result = FloodLearningUI.Text("Result", root.transform, answer == 1 ? $"Correct!  |  Confidence: {confidence}%" : $"Your answer: {(char)('A' + answer)}  |  Confidence: {confidence}%", 39, FontStyles.Bold, -105, 62, TextAlignmentOptions.Center, 65);
        result.color = answer == 1 ? new Color(.4f, 1f, .55f) : new Color(1f, .68f, .35f);
        FloodLearningUI.Text("Correct", root.transform, "Correct answer: B) The western endpoint did not adequately close the available flood pathway, allowing water to bypass the barrier.", 31, FontStyles.Bold, -205, 145, TextAlignmentOptions.TopLeft, 70).color = new Color(.4f, 1f, .55f);
        FloodLearningUI.Text("ExplanationTitle", root.transform, "Explanation", 34, FontStyles.Bold, -390, 55, TextAlignmentOptions.Left, 70);
        FloodLearningUI.Text("Explanation", root.transform, "A barrier can remain structurally intact and still fail as a protective intervention if water can move around one of its endpoints. The relevant issue is continuity of the protective boundary, not simply whether the central section remains intact.", 29, FontStyles.Normal, -460, 230, TextAlignmentOptions.TopLeft, 70);
        Debug.Log($"A1-P3 result: answer={(char)('A' + answer)}, correct={answer == 1}, confidence={confidence}");
    }
}
