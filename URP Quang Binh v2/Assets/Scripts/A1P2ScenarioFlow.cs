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

    [Header("A1-P2 scenario houses")]
    [SerializeField] GameObject houseE;
    [SerializeField] GameObject houseF;
    [SerializeField] GameObject houseG;
    [SerializeField] GameObject houseH;

    [Header("A1-P2 dyke limits (metres)")]
    [Min(1)] public int maxDykes = 1;
    [Min(0f)] public float minDykeLengthMeters = 0f;
    [Min(0f)] public float maxDykeLengthMeters = 3.5f;

    InputAction questA;
    GameObject floodPrefab, prompt, preview, previewHint, waterDirectionHint, activeFlood;
    TerrainFloodSimulation simulation;
    float viewingDuration, floodDuration, elapsed;
    int selectedAnswer = -1;
    Phase phase;

    void Awake()
    {
        ScenarioFeedbackStage.HideAllSolutions();
        SetA1P2Houses(false);
    }

    public void Begin(GameObject prefab, float previewSeconds, float simulationSeconds)
    {
        FloodStudyLogger.BeginScenario("A1-P2", "Topography", previewSeconds);
        SetA1P2Houses(true);
        floodPrefab = prefab; viewingDuration = previewSeconds; floodDuration = simulationSeconds;
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
        SetDykeConstruction(false, 0, 0f, 0f, 0f); ClearDykeManagerChildren();
        preview = new GameObject("Scenario_A1_P2_BarrierAlignments");
        preview.AddComponent<FloodBarrierScenarioA1P2>();
        previewHint = ScenarioGuidanceOverlay.ShowObservation("Look at the possible barrier alignments", viewingDuration);
        FloodStudyLogger.BeginObservation();
        elapsed = 0; phase = Phase.Preview;
    }

    void ShowQuestion()
    {
        CleanupObject(ref previewHint);
        FloodStudyLogger.EndObservation();
        phase = Phase.Question;
        P2Panel.ShowQuestion(StartPreview, BeginDykeBuilding);
    }

    void BeginDykeBuilding(int answer)
    {
        selectedAnswer = answer;
        FloodStudyLogger.RecordAnswer(answer);
        FloodStudyLogger.BeginBuildingPhase();
        CleanupObject(ref preview);
        SetDykeConstruction(true, maxDykes, minDykeLengthMeters, maxDykeLengthMeters, 0f);
        prompt = P2Panel.CreateBuildingPrompt();
        waterDirectionHint = ScenarioGuidanceOverlay.ShowWaterFromLeft();
        phase = Phase.BuildingDykes;
    }

    void StartFlood()
    {
        if (floodPrefab == null) { Debug.LogError("A1-P2 flood prefab is missing.", this); return; }
        CleanupObject(ref prompt);
        CleanupObject(ref waterDirectionHint);
        SetDykeConstruction(false, 0, 0f, 0f, 0f);
        Transform parent = Find("FakeFloodEnvironment")?.transform;
        activeFlood = Instantiate(floodPrefab, parent, false);
        activeFlood.name = "Scenario_A1_P2_WithParticipantDykes";
        simulation = activeFlood.GetComponent<TerrainFloodSimulation>();
        if (simulation == null) { Debug.LogError("TerrainFloodSimulation is missing.", this); return; }
        simulation.Begin(); FloodStudyLogger.EndBuildingPhase(); FloodStudyLogger.BeginSimulation(); elapsed = 0; phase = Phase.Flood;
    }

    void FinishFlood()
    {
        FloodStudyLogger.EndSimulation(simulation);
        simulation?.StopSimulation(); CleanupObject(ref activeFlood);
        // Keep the participant's dykes and scenario buildings visible through
        // the confidence and written-feedback screens. They are cleaned up
        // after the separate feedback scenario.
        phase = Phase.Confidence;
        P2Panel.ShowConfidence(selectedAnswer, StartFeedbackScenario);
    }

    void StartFeedbackScenario()
    {
        ScenarioFeedbackStage.Show("A1 - P2 | Topography", floodPrefab, floodDuration,
            new[] { "A1P2_Solution" }, () =>
        {
            SetA1P2Houses(false);
            FloodStudyLogger.EndScenario();
            StartA1P3();
        });
    }

    void StartA1P3()
    {
        A1P3ScenarioFlow flow = gameObject.GetComponent<A1P3ScenarioFlow>();
        if (flow == null) flow = gameObject.AddComponent<A1P3ScenarioFlow>();
        flow.Begin(floodPrefab, viewingDuration, floodDuration);
    }

    static void SetDykeConstruction(bool enabled, int maxDykes, float minLengthMeters, float maxLengthMeters, float maxTotalLengthMeters)
    {
        DykeManagerTutorial manager = UnityEngine.Object.FindFirstObjectByType<DykeManagerTutorial>(FindObjectsInactive.Include);
        if (manager != null)
        {
            manager.gameObject.SetActive(true);
            manager.freePlacement = enabled;
            manager.enabled = enabled;
            if (enabled) manager.ConfigureFreePlacementLimits(maxDykes, minLengthMeters, maxLengthMeters, maxTotalLengthMeters);
            else manager.ClearFreePlacementLimits();
        }
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            if (child.name.ToLowerInvariant().Contains("snapping_point")) child.gameObject.SetActive(false);
    }

    static void ClearDykeManagerChildren()
    {
        DykeManagerTutorial manager = UnityEngine.Object.FindFirstObjectByType<DykeManagerTutorial>(FindObjectsInactive.Include);
        if (manager == null) return;
        manager.enabled = false; manager.freePlacement = false; manager.ClearFreePlacementLimits();
        for (int i = manager.transform.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(manager.transform.GetChild(i).gameObject);
    }

    static GameObject Find(string name) { foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects()) foreach (Transform child in root.GetComponentsInChildren<Transform>(true)) if (child.name == name) return child.gameObject; return null; }

    /// <summary>
    /// Shows or hides the four FBX house instances used only by A1-P2. Direct
    /// inspector references are optional; exact-name lookup also finds nested
    /// and currently inactive FBX instances.
    /// </summary>
    void SetA1P2Houses(bool active)
    {
        GameObject[] assigned = { houseE, houseF, houseG, houseH };
        string[] names = { "HOUSE_E", "HOUSE_F", "HOUSE_G", "HOUSE_H" };
        var touched = new System.Collections.Generic.HashSet<int>();

        for (int i = 0; i < names.Length; i++)
        {
            if (assigned[i] != null)
            {
                assigned[i].SetActive(active);
                touched.Add(assigned[i].GetInstanceID());
            }

            foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                if (!string.Equals(child.name, names[i], StringComparison.OrdinalIgnoreCase)) continue;
                if (touched.Add(child.gameObject.GetInstanceID())) child.gameObject.SetActive(active);
            }
        }
    }

    static void CleanupObject(ref GameObject target) { if (target != null) UnityEngine.Object.Destroy(target); target = null; }
    void OnDestroy() { FloodStudyLogger.EndScenario(); SetA1P2Houses(false); questA?.Disable(); questA?.Dispose(); CleanupObject(ref prompt); CleanupObject(ref preview); CleanupObject(ref previewHint); CleanupObject(ref waterDirectionHint); CleanupObject(ref activeFlood); }
}

public sealed class FloodBarrierScenarioA1P2 : MonoBehaviour
{
    Material material;
    void Awake()
    {
        Renderer terrain = FindRenderer("SM_Tutorial_Terrain_01"); if (terrain == null) return;
        Bounds b = terrain.bounds; float y = b.max.y + .05f;
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit"); if (shader == null) shader = Shader.Find("Sprites/Default");
        // The alignments are possible barrier locations, so show them in a
        // neutral grey rather than the blue used for flood pathways.
        material = new Material(shader) { color = new Color(.55f, .55f, .55f, 1f) };
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
        FloodLearningUI.Text("Prompt", root.transform, "Build one dyke up to 3.5 metres long anywhere on the terrain with the trigger.\nPoint at it and press Grip to destroy it. Press A when you are finished.", 30, FontStyles.Bold, -135, 135, TextAlignmentOptions.Center, 55).color = new Color(.35f, .82f, 1f);
        FloodLearningUI.PlaceInFrontOfPlayer(root.transform, 2.1f); return root;
    }
    public static void ShowQuestion(Action repeat, Action<int> answer)
    {
        FloodAudio.PlayQuestion();
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
        FloodLearningUI.Text("Scale", confidence.transform, "Not confident                                      Moderately confident                                      Very confident", 23, FontStyles.Normal, -510, 55, TextAlignmentOptions.Center, 130).color = new Color(.75f, .85f, .95f);
        FloodLearningUI.Button("Confirm", confidence.transform, "Confirm confidence", -650, 95, 31,
            () =>
            {
                int confidenceValue = Mathf.RoundToInt(slider.value) * 20;
                FloodStudyLogger.RecordConfidence(confidenceValue);
                ShowFeedback(root, answer, confidenceValue, onCompleted);
            }, 420);
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
        RectTransform rect = (RectTransform)root.transform; rect.sizeDelta = size; rect.localScale = Vector3.one * scale; Image image = root.GetComponent<Image>(); FloodLearningUIStyle.ApplyPanel(image); image.raycastTarget = false; return root;
    }
}

/// <summary>
/// Displays the participant's built barriers after the written feedback. The
/// barriers stay in the scene and are made translucent so a model solution can
/// be added alongside them later.
/// </summary>
static class ScenarioFeedbackStage
{
    static GameObject panel;
    static string[] activeSolutionNames;
    static readonly string[] allSolutionNames =
    {
        "A1P2_Solution", "A1P3_Solution", "A1P4_Solution", "A1P5_Solution",
        "A1P6_Solution_A", "A1P6_Solution_B"
    };

    public static void HideAllSolutions() => SetSolutionObjectsActive(allSolutionNames, false);

    public static void Show(string scenarioTitle, GameObject floodPrefab, float simulationDuration,
        string[] solutionNames, Action onCompleted)
    {
        Close();
        MakeParticipantDykesTransparent();
        activeSolutionNames = solutionNames ?? System.Array.Empty<string>();
        SetSolutionObjectsActive(activeSolutionNames, true);

        string safeName = scenarioTitle.Replace("|", "_").Replace(" ", "_");
        panel = P2Panel.CanvasRoot("FeedbackScenario_" + safeName, new Vector2(1300, 520));
        ScenarioFeedbackStageController controller = panel.AddComponent<ScenarioFeedbackStageController>();
        controller.Begin(floodPrefab, simulationDuration, ScenarioIdFromTitle(scenarioTitle), activeSolutionNames);
        FloodLearningUI.Text("Title", panel.transform, scenarioTitle + "  |  Feedback scenario", 40,
            FontStyles.Bold, -35, 60, TextAlignmentOptions.Center, 45);
        FloodLearningUI.Text("ParticipantBarriers", panel.transform,
            "Your barriers are shown transparently on the terrain.", 32,
            FontStyles.Normal, -135, 60, TextAlignmentOptions.Center, 55);
        FloodLearningUI.Text("ModelSolution", panel.transform,
            "The model solution is shown together with your barriers.", 30,
            FontStyles.Normal, -225, 60, TextAlignmentOptions.Center, 55);
        FloodLearningUI.Text("SimulationPrompt", panel.transform,
            "Press A to start the feedback simulation.", 30,
            FontStyles.Normal, -300, 55, TextAlignmentOptions.Center, 55).color = new Color(.35f, .82f, 1f);
        UnityEngine.UI.Button continueButton = FloodLearningUI.Button("Continue", panel.transform, "Continue", -375, 82, 29,
            () =>
            {
                Close();
                ClearParticipantDykes();
                onCompleted?.Invoke();
            }, 360);
        continueButton.interactable = controller.SimulationFinished;
        controller.SimulationFinishedChanged += () => continueButton.interactable = true;
        FloodLearningUI.PlaceInFrontOfPlayer(panel.transform, 2.2f);
    }

    public static void Close()
    {
        if (panel != null)
        {
            ScenarioFeedbackStageController controller = panel.GetComponent<ScenarioFeedbackStageController>();
            if (controller != null) controller.Stop();
            UnityEngine.Object.Destroy(panel);
        }
        SetSolutionObjectsActive(activeSolutionNames, false);
        activeSolutionNames = null;
        panel = null;
    }

    static string ScenarioIdFromTitle(string title)
    {
        if (string.IsNullOrEmpty(title)) return "UnknownScenario";
        int separator = title.IndexOf('|');
        string prefix = separator >= 0 ? title.Substring(0, separator) : title;
        return prefix.Replace(" ", string.Empty);
    }

    static void MakeParticipantDykesTransparent()
    {
        DykeManagerTutorial manager = UnityEngine.Object.FindFirstObjectByType<DykeManagerTutorial>(FindObjectsInactive.Include);
        if (manager == null) return;

        manager.enabled = false;
        manager.freePlacement = false;
        manager.ClearFreePlacementLimits();
        foreach (Renderer renderer in manager.GetComponentsInChildren<Renderer>(true))
        {
            Material[] materials = renderer.materials;
            foreach (Material material in materials)
                MakeMaterialTransparent(material, .38f);
            renderer.materials = materials;
        }

        foreach (Collider collider in manager.GetComponentsInChildren<Collider>(true))
            if (collider != null) UnityEngine.Object.Destroy(collider);
    }

    static void MakeMaterialTransparent(Material material, float alpha)
    {
        if (material == null) return;
        Color color = material.HasProperty("_BaseColor") ? material.GetColor("_BaseColor") : material.color;
        color.a = alpha;
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Color")) material.SetColor("_Color", color);

        if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 1f);
        if (material.HasProperty("_Blend")) material.SetFloat("_Blend", 0f);
        if (material.HasProperty("_SrcBlend")) material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        if (material.HasProperty("_DstBlend")) material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        if (material.HasProperty("_ZWrite")) material.SetFloat("_ZWrite", 0f);
        material.SetOverrideTag("RenderType", "Transparent");
        material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
    }

    static void ClearParticipantDykes()
    {
        DykeManagerTutorial manager = UnityEngine.Object.FindFirstObjectByType<DykeManagerTutorial>(FindObjectsInactive.Include);
        if (manager == null) return;
        manager.enabled = false;
        manager.freePlacement = false;
        manager.ClearFreePlacementLimits();
        for (int i = manager.transform.childCount - 1; i >= 0; i--)
            UnityEngine.Object.Destroy(manager.transform.GetChild(i).gameObject);
    }

    static void SetSolutionObjectsActive(string[] names, bool active)
    {
        if (names == null || names.Length == 0) return;
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
        {
            for (int i = 0; i < names.Length; i++)
            {
                if (!string.Equals(child.name, names[i], System.StringComparison.OrdinalIgnoreCase)) continue;
                child.gameObject.SetActive(active);
                break;
            }
        }
    }
}

sealed class ScenarioFeedbackStageController : MonoBehaviour
{
    GameObject activeFlood;
    GameObject floodPrefab;
    TerrainFloodSimulation simulation;
    InputAction questA;
    string scenarioId;
    string[] solutionObjectNames;
    float simulationDuration;
    float elapsed;
    bool waitingForStart;
    public bool SimulationFinished { get; private set; }
    public event Action SimulationFinishedChanged;

    public void Begin(GameObject floodPrefab, float duration, string scenarioId, string[] solutionObjectNames)
    {
        this.floodPrefab = floodPrefab;
        this.scenarioId = scenarioId;
        this.solutionObjectNames = solutionObjectNames ?? System.Array.Empty<string>();
        simulationDuration = Mathf.Max(0f, duration);
        elapsed = 0f;
        waitingForStart = true;
        SimulationFinished = false;
        questA = new InputAction("Start feedback flood", InputActionType.Button,
            "<XRController>{RightHand}/primaryButton");
        questA.Enable();
        if (floodPrefab == null)
        {
            Debug.LogWarning("Feedback scenario has no flood prefab assigned.", this);
            FloodStudyLogger.CaptureTopDownScreenshot(this.scenarioId, "feedback_end", this.solutionObjectNames);
            waitingForStart = false;
            SimulationFinished = true;
            return;
        }
    }

    void StartSimulation()
    {
        if (floodPrefab == null) return;
        Transform parent = Find("FakeFloodEnvironment")?.transform;
        activeFlood = Instantiate(floodPrefab, parent, false);
        activeFlood.name = "FeedbackScenario_FloodSimulation";
        simulation = activeFlood.GetComponent<TerrainFloodSimulation>();
        if (simulation == null)
        {
            Debug.LogError("TerrainFloodSimulation is missing on the feedback flood prefab.", this);
            FloodStudyLogger.CaptureTopDownScreenshot(scenarioId, "feedback_end", solutionObjectNames);
            Destroy(activeFlood);
            activeFlood = null;
            SimulationFinished = true;
            SimulationFinishedChanged?.Invoke();
            return;
        }
        simulation.Begin();
    }

    void Update()
    {
        if (SimulationFinished) return;
        if (waitingForStart)
        {
            if (questA != null && questA.WasPressedThisFrame())
            {
                waitingForStart = false;
                StartSimulation();
            }
            return;
        }
        if (simulation == null || simulationDuration <= 0f)
        {
            if (simulationDuration <= 0f) StopSimulationAtDuration();
            return;
        }
        elapsed += Time.deltaTime;
        if (elapsed >= simulationDuration)
            StopSimulationAtDuration();
    }

    void StopSimulationAtDuration()
    {
        // Capture while the final feedback water state and both the
        // transparent participant dykes and model solution are still present.
        FloodStudyLogger.CaptureTopDownScreenshot(scenarioId, "feedback_end", solutionObjectNames);
        simulation?.StopSimulation();
        if (activeFlood != null) Destroy(activeFlood);
        activeFlood = null;
        simulation = null;
        SimulationFinished = true;
        SimulationFinishedChanged?.Invoke();
    }

    public void Stop()
    {
        waitingForStart = false;
        simulation?.StopSimulation();
        if (activeFlood != null) Destroy(activeFlood);
        activeFlood = null;
        simulation = null;
        questA?.Disable();
        questA?.Dispose();
        questA = null;
    }

    static GameObject Find(string objectName)
    {
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            if (child.name == objectName) return child.gameObject;
        return null;
    }

    void OnDestroy() => Stop();
}

static class GeneralDiscomfortSurvey
{
    static GameObject panel;

    public static void Show(string measurementId, Action onCompleted)
    {
        Close();
        FloodAudio.PlayGeneralDiscomfort();
        panel = P2Panel.CanvasRoot("General_Discomfort_Survey", new Vector2(1350, 760));
        FloodLearningUI.Text("Title", panel.transform, "General Discomfort", 43,
            FontStyles.Bold, -40, 65, TextAlignmentOptions.Center, 45);
        FloodLearningUI.Text("Question", panel.transform, "General Discomfort. How do you feel?", 35,
            FontStyles.Bold, -135, 65, TextAlignmentOptions.Center, 55);

        TMP_Text value = FloodLearningUI.Text("Value", panel.transform,
            "0 - No Sickness at All", 34, FontStyles.Bold, -245, 65,
            TextAlignmentOptions.Center, 80);
        Slider slider = FloodLearningUI.Slider("SicknessSlider", panel.transform, -365);
        slider.minValue = 0f;
        slider.maxValue = 20f;
        slider.wholeNumbers = true;
        slider.value = 0f;
        slider.onValueChanged.AddListener(v =>
        {
            int score = Mathf.Clamp(Mathf.RoundToInt(v), 0, 20);
            value.text = score == 0
                ? "0 - No Sickness at All"
                : score == 20
                    ? "20 - Frank Sickness"
                    : $"{score} / 20";
        });

        FloodLearningUI.Text("ScaleMin", panel.transform, "0 - No Sickness at All", 23,
            FontStyles.Normal, -465, 50, TextAlignmentOptions.Left, 95);
        FloodLearningUI.Text("ScaleMax", panel.transform, "20 - Frank Sickness", 23,
            FontStyles.Normal, -465, 50, TextAlignmentOptions.Right, 95);
        FloodLearningUI.Button("Confirm", panel.transform, "Confirm", -580, 82, 29,
            () =>
            {
                int score = Mathf.RoundToInt(slider.value);
                Debug.Log($"General discomfort score: {score}/20");
                FloodStudyLogger.RecordGeneralDiscomfort(measurementId, score);
                Close();
                onCompleted?.Invoke();
            }, 360);
        FloodLearningUI.PlaceInFrontOfPlayer(panel.transform, 2.25f);
    }

    static void Close()
    {
        if (panel != null) UnityEngine.Object.Destroy(panel);
        panel = null;
    }
}

static class ParticipationEndUI
{
    public static void Show()
    {
        GameObject root = P2Panel.CanvasRoot("Participation_Complete", new Vector2(1200, 430));
        FloodLearningUI.Text("Title", root.transform, "Thanks for your participation", 44,
            FontStyles.Bold, -70, 75, TextAlignmentOptions.Center, 45);
        FloodLearningUI.Text("Message", root.transform, "The application will close in 10 seconds.", 31,
            FontStyles.Normal, -195, 60, TextAlignmentOptions.Center, 55);
        root.AddComponent<ApplicationQuitAfterDelay>();
        FloodLearningUI.PlaceInFrontOfPlayer(root.transform, 2.2f);
    }
}

sealed class ApplicationQuitAfterDelay : MonoBehaviour
{
    const float DelaySeconds = 10f;
    float remaining = DelaySeconds;

    void Update()
    {
        remaining -= Time.unscaledDeltaTime;
        if (remaining > 0f) return;
        enabled = false;
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
