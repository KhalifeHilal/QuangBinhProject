using System;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

public sealed class A1P2ScenarioFlow : MonoBehaviour
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
        floodPrefab = prefab; viewingDuration = previewSeconds; floodDuration = simulationSeconds;
        SetScenarioObjects(true);
        questA ??= new InputAction("A1-P2 Continue", InputActionType.Button, "<XRController>{RightHand}/primaryButton");
        questA.Enable();
        prompt = P2Panel.CreateStartPrompt();
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
        CleanupObject(ref prompt); CleanupObject(ref preview); CleanupObject(ref previewHint);
        SetDykeConstruction(false); ClearDykeManagerChildren();
        preview = new GameObject("Scenario_A1_P2_BarrierAlignments");
        preview.AddComponent<FloodBarrierScenarioA1P2>();
        previewHint = ScenarioGuidanceOverlay.ShowObservation("Look at the possible barrier alignments");
        elapsed = 0; phase = Phase.Preview;
    }

    void ShowQuestion()
    {
        CleanupObject(ref previewHint);
        phase = Phase.Question;
        P2Panel.ShowQuestion(StartPreview, BeginDykeBuilding);
    }

    void BeginDykeBuilding(int answer)
    {
        selectedAnswer = answer;
        CleanupObject(ref preview);
        SetDykeConstruction(true);
        prompt = P2Panel.CreateBuildingPrompt();
        waterDirectionHint = ScenarioGuidanceOverlay.ShowWaterFromLeft();
        phase = Phase.BuildingDykes;
    }

    void StartFlood()
    {
        if (floodPrefab == null) { Debug.LogError("A1-P2 flood prefab is missing.", this); return; }
        CleanupObject(ref prompt);
        CleanupObject(ref waterDirectionHint);
        SetDykeConstruction(false);
        Transform parent = Find("FakeFloodEnvironment")?.transform;
        activeFlood = Instantiate(floodPrefab, parent, false);
        activeFlood.name = "Scenario_A1_P2_WithParticipantDykes";
        simulation = activeFlood.GetComponent<TerrainFloodSimulation>();
        if (simulation == null) { Debug.LogError("TerrainFloodSimulation is missing.", this); return; }
        simulation.Begin(); elapsed = 0; phase = Phase.Flood;
    }

    void FinishFlood()
    {
        simulation?.StopSimulation(); CleanupObject(ref activeFlood); ClearDykeManagerChildren();
        SetScenarioObjects(false);
        phase = Phase.Confidence;
        P2Panel.ShowConfidence(selectedAnswer, StartA1P3);
    }

    void StartA1P3()
    {
        SetScenarioObjects(false);
        A1P3ScenarioFlow flow = gameObject.GetComponent<A1P3ScenarioFlow>();
        if (flow == null) flow = gameObject.AddComponent<A1P3ScenarioFlow>();
        flow.Begin(floodPrefab, viewingDuration, floodDuration);
    }

    static void SetDykeConstruction(bool enabled)
    {
        DykeManagerTutorial manager = UnityEngine.Object.FindFirstObjectByType<DykeManagerTutorial>(FindObjectsInactive.Include);
        if (manager != null) { manager.gameObject.SetActive(true); manager.freePlacement = enabled; manager.enabled = enabled; }
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            if (child.name.ToLowerInvariant().Contains("snapping_point")) child.gameObject.SetActive(false);
    }

    static void ClearDykeManagerChildren()
    {
        DykeManagerTutorial manager = UnityEngine.Object.FindFirstObjectByType<DykeManagerTutorial>(FindObjectsInactive.Include);
        if (manager == null) return;
        manager.enabled = false; manager.freePlacement = false;
        for (int i = manager.transform.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(manager.transform.GetChild(i).gameObject);
    }

    static GameObject Find(string name) { foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects()) foreach (Transform child in root.GetComponentsInChildren<Transform>(true)) if (child.name == name) return child.gameObject; return null; }
    static void SetScenarioObjects(bool active)
    {
        GameObject first = Find("A1P2_1");
        GameObject second = Find("A1P2_2");
        if (first != null) first.SetActive(active);
        if (second != null) second.SetActive(active);
    }
    static void CleanupObject(ref GameObject target) { if (target != null) UnityEngine.Object.Destroy(target); target = null; }
    void OnDestroy() { SetScenarioObjects(false); questA?.Disable(); questA?.Dispose(); CleanupObject(ref prompt); CleanupObject(ref preview); CleanupObject(ref previewHint); CleanupObject(ref waterDirectionHint); CleanupObject(ref activeFlood); }
}

public sealed class FloodBarrierScenarioA1P2 : MonoBehaviour
{
    Material material;
    void Awake()
    {
        Renderer terrain = FindRenderer("SM_Tutorial_Terrain_01"); if (terrain == null) return;
        Bounds b = terrain.bounds; float y = b.max.y + .05f;
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit"); if (shader == null) shader = Shader.Find("Sprites/Default");
        material = new Material(shader) { color = new Color(.02f, .58f, 1f) };
        Create("Barrier_A_HighTerrain", new Vector3(b.center.x, y, b.center.z + b.size.z * .22f), b.size.z * .62f, 22f, "Alignment A");
        Create("Barrier_B_LowEnds", new Vector3(b.center.x, y, b.center.z - b.size.z * .2f), b.size.z * .62f, -22f, "Alignment B");
    }
    void Create(string name, Vector3 center, float length, float angle, string label)
    {
        GameObject obj = new GameObject(name, typeof(LineRenderer)); obj.transform.SetParent(transform);
        Vector3 d = Quaternion.Euler(0, angle, 0) * Vector3.forward * length * .5f;
        LineRenderer line = obj.GetComponent<LineRenderer>(); line.useWorldSpace = true; line.positionCount = 2; line.SetPosition(0, center - d); line.SetPosition(1, center + d); line.widthMultiplier = .07f; line.numCapVertices = 6; line.material = material;
        GameObject canvas = P2Panel.CanvasRoot(name + "_Label", new Vector2(330, 95), .0012f); canvas.transform.SetParent(transform); canvas.transform.position = center + Vector3.up * .28f;
        if (Camera.main != null) canvas.transform.rotation = Quaternion.LookRotation(canvas.transform.position - Camera.main.transform.position, Vector3.up);
        FloodLearningUI.Text("Text", canvas.transform, label, 34, FontStyles.Bold, -5, 80, TextAlignmentOptions.Center, 12);
    }
    static Renderer FindRenderer(string name) { foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects()) foreach (Transform child in root.GetComponentsInChildren<Transform>(true)) if (child.name == name) return child.GetComponentInChildren<Renderer>(true); return null; }
    void OnDestroy() { if (material != null) Destroy(material); }
}

static class P2Panel
{
    static readonly string[] Answers =
    {
        "A) The shorter path should be prioritized because water reaches it first.",
        "B) The barrier closer to buildings is more effective regardless of topography.",
        "C) Both alignments are equally effective because the barriers have equal length.",
        "D) The surrounding high terrain can function as part of the continuous boundary that limits possible routes around the barrier."
    };

    public static GameObject CreateStartPrompt()
    {
        GameObject root = CanvasRoot("A1_P2_Start", new Vector2(950, 350));
        FloodLearningUI.Text("Title", root.transform, "A1 - P2", 40, FontStyles.Bold, -35, 55, TextAlignmentOptions.Center, 40);
        FloodLearningUI.Text("Topic", root.transform, "Topography", 45, FontStyles.Bold, -110, 65, TextAlignmentOptions.Center, 40);
        FloodLearningUI.Text("Continue", root.transform, "Press A to continue", 35, FontStyles.Normal, -220, 55, TextAlignmentOptions.Center, 40).color = new Color(.3f, .8f, 1f);
        FloodLearningUI.PlaceInFrontOfPlayer(root.transform, 2.1f); return root;
    }
    public static GameObject CreatePreviewHint()
    {
        GameObject root = CanvasRoot("A1_P2_PreviewHint", new Vector2(900, 145), .0014f);
        FloodLearningUI.Text("Text", root.transform, "Look at the possible barrier alignments", 39, FontStyles.Bold, -25, 90, TextAlignmentOptions.Center, 30);
        FloodLearningUI.PlaceInFrontOfPlayer(root.transform, 2.05f); root.transform.position += Vector3.up * .25f; return root;
    }
    public static GameObject CreateBuildingPrompt()
    {
        GameObject root = CanvasRoot("A1_P2_Build", new Vector2(1100, 390));
        FloodLearningUI.Text("Title", root.transform, "A1 - P2  |  Build protection", 39, FontStyles.Bold, -35, 58, TextAlignmentOptions.Center, 45);
        FloodLearningUI.Text("Prompt", root.transform, "Build dykes for your answer anywhere on the terrain with the trigger.\nPoint at one and press Grip to destroy it. Press A when you are finished.", 30, FontStyles.Bold, -135, 125, TextAlignmentOptions.Center, 55).color = new Color(.35f, .82f, 1f);
        FloodLearningUI.PlaceInFrontOfPlayer(root.transform, 2.1f); return root;
    }
    public static void ShowQuestion(Action repeat, Action<int> answer)
    {
        GameObject root = CanvasRoot("Quiz_A1_P2", new Vector2(1450, 1020));
        FloodLearningUI.Text("Title", root.transform, "A1 - P2  |  Topography", 44, FontStyles.Bold, -28, 62, TextAlignmentOptions.Left, 55);
        FloodLearningUI.Text("Question", root.transform, "Which barrier alignment should receive greater attention when planning flood protection?", 33, FontStyles.Bold, -110, 95, TextAlignmentOptions.TopLeft, 55);
        for (int i = 0; i < 4; i++) { int n = i; FloodLearningUI.Button("Answer_" + i, root.transform, Answers[i], -225 - i * 125, 102, 27, () => { UnityEngine.Object.Destroy(root); answer(n); }); }
        FloodLearningUI.Button("Repeat", root.transform, "Repeat scenario", -840, 82, 29, () => { UnityEngine.Object.Destroy(root); repeat(); }, 390);
        FloodLearningUI.PlaceInFrontOfPlayer(root.transform, 2.25f);
    }
    public static void ShowConfidence(int answer, Action onCompleted)
    {
        GameObject root = CanvasRoot("Confidence_A1_P2", new Vector2(1450, 1020));
        GameObject confidence = FloodLearningUI.Group("ConfidenceGroup", root.transform);
        FloodLearningUI.Text("Question", confidence.transform, "How confident are you in your answer?", 42, FontStyles.Bold, -150, 70, TextAlignmentOptions.Center, 80);
        TMP_Text value = FloodLearningUI.Text("Value", confidence.transform, "60% - Moderately confident", 38, FontStyles.Bold, -270, 65, TextAlignmentOptions.Center, 100);
        Slider slider = FloodLearningUI.Slider("Slider", confidence.transform, -390); slider.minValue = 0; slider.maxValue = 5; slider.wholeNumbers = true; slider.value = 3;
        string[] labels = { "Not confident", "Low confidence", "Slightly confident", "Moderately confident", "Confident", "Very confident" };
        slider.onValueChanged.AddListener(v => { int n = Mathf.Clamp(Mathf.RoundToInt(v), 0, 5); value.text = $"{n * 20}% - {labels[n]}"; });
        FloodLearningUI.Text("Ticks", confidence.transform, "0                 20                 40                 60                 80                100", 25, FontStyles.Normal, -455, 45, TextAlignmentOptions.Center, 170);
        FloodLearningUI.Text("Scale", confidence.transform, "Not confident                                      Moderately confident                                      Very confident", 23, FontStyles.Normal, -510, 55, TextAlignmentOptions.Center, 130).color = new Color(.75f, .85f, .95f);
        FloodLearningUI.Button("Confirm", confidence.transform, "Confirm confidence", -650, 95, 31, () => ShowFeedback(root, answer, Mathf.RoundToInt(slider.value) * 20, onCompleted), 420);
        FloodLearningUI.PlaceInFrontOfPlayer(root.transform, 2.25f);
    }
    static void ShowFeedback(GameObject root, int answer, int confidence, Action onCompleted)
    {
        foreach (Transform child in root.transform) UnityEngine.Object.Destroy(child.gameObject);
        TMP_Text result = FloodLearningUI.Text("Result", root.transform, answer == 3 ? $"Correct!  |  Confidence: {confidence}%" : $"Your answer: {(char)('A' + answer)}  |  Confidence: {confidence}%", 39, FontStyles.Bold, -120, 62, TextAlignmentOptions.Center, 65); result.color = answer == 3 ? new Color(.4f, 1f, .55f) : new Color(1f, .68f, .35f);
        FloodLearningUI.Text("Correct", root.transform, "Correct answer: D) The surrounding high terrain can function as part of the continuous boundary that limits possible routes around the barrier.", 31, FontStyles.Bold, -220, 150, TextAlignmentOptions.TopLeft, 70).color = new Color(.4f, 1f, .55f);
        FloodLearningUI.Text("ExplanationTitle", root.transform, "Explanation", 34, FontStyles.Bold, -410, 55, TextAlignmentOptions.Left, 70);
        FloodLearningUI.Text("Explanation", root.transform, "A barrier is more effective when its endpoints connect to terrain that restricts lateral flow. Existing high ground can extend the effective protective boundary beyond the constructed barrier itself and reduce opportunities for water to bypass it.", 29, FontStyles.Normal, -480, 240, TextAlignmentOptions.TopLeft, 70);
        FloodLearningUI.Button("Continue", root.transform, "Continue", -785, 82, 29, () => { UnityEngine.Object.Destroy(root); onCompleted?.Invoke(); }, 360);
        Debug.Log($"A1-P2 result: answer={(char)('A' + answer)}, correct={answer == 3}, confidence={confidence}");
    }
    public static GameObject CanvasRoot(string name, Vector2 size, float scale = .0015f)
    {
        GameObject root = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(TrackedDeviceGraphicRaycaster), typeof(Image));
        root.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace; root.GetComponent<Canvas>().sortingOrder = 200;
        RectTransform rect = (RectTransform)root.transform; rect.sizeDelta = size; rect.localScale = Vector3.one * scale; Image image = root.GetComponent<Image>(); image.color = new Color(.025f, .055f, .09f, .98f); image.raycastTarget = false; return root;
    }
}
