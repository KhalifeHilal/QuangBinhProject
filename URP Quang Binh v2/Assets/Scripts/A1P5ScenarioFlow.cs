using System;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>A1-P5: choose the authored strategy that gives the best protection per metre.</summary>
public sealed class A1P5ScenarioFlow : MonoBehaviour
{
    enum Phase { Idle, WaitingForA, Preview, Question, BuildingDykes, Flood, Confidence }

    [Header("A1-P5 scenario houses")]
    [SerializeField] GameObject houseA;
    [SerializeField] GameObject houseB;
    [SerializeField] GameObject houseE;
    [SerializeField] GameObject houseF;
    [SerializeField] GameObject houseG;
    [SerializeField] GameObject houseH;
    [SerializeField] GameObject houseI;
    [SerializeField] GameObject houseJ;
    [SerializeField] GameObject houseK;

    [Header("A1-P5 dyke limits (metres)")]
    [Min(1)] public int maxDykes = 1;
    [Min(0f)] public float minDykeLengthMeters = 0f;
    [Min(0f)] public float maxDykeLengthMeters = 1.5f;

    InputAction questA;
    GameObject floodPrefab, prompt, preview, previewHint, waterDirectionHint, activeFlood;
    TerrainFloodSimulation simulation;
    float viewingDuration, floodDuration, elapsed;
    int selectedAnswer = -1;
    Phase phase;

    void Awake()
    {
        ScenarioFeedbackStage.HideAllSolutions();
        SetP5ScenarioObjects(false);
        SetP5Houses(false);
    }

    public void Begin(GameObject prefab, float previewSeconds, float simulationSeconds)
    {
        FloodStudyLogger.BeginScenario("A1-P5", "Resource-Efficient Placement", previewSeconds);
        SetP5ScenarioObjects(true);
        SetP5Houses(true);
        floodPrefab = prefab;
        viewingDuration = previewSeconds;
        floodDuration = simulationSeconds;
        questA ??= new InputAction("A1-P5 Continue", InputActionType.Button,
            "<XRController>{RightHand}/primaryButton");
        questA.Enable();
        prompt = A1P5Panel.CreateStartPrompt();
        phase = Phase.WaitingForA;
    }

    public void BeginDebug(GameObject prefab, float previewSeconds, float simulationSeconds)
    {
        Begin(prefab, previewSeconds, simulationSeconds);
        StartPreview();
    }

    void Update()
    {
        if (phase == Phase.WaitingForA && ParticipantInput.StartWasPressedThisFrame(questA)) StartPreview();
        if (phase == Phase.BuildingDykes && ParticipantInput.SimulationStartWasPressedThisFrame(questA)) StartFlood();
        if (phase != Phase.Preview && phase != Phase.Flood) return;
        elapsed += Time.deltaTime;
        if (phase == Phase.Preview && elapsed >= viewingDuration) ShowQuestion();
        else if (phase == Phase.Flood && elapsed >= floodDuration) FinishFlood();
    }

    void StartPreview()
    {
        Cleanup(ref prompt); Cleanup(ref preview); Cleanup(ref previewHint);
        SetDykeConstruction(false, 0, 0f, 0f, 0f); ClearDykes();
        // A1P5_1/2/3 are the authored strategy dykes and labels. Do not add
        // the old procedural corridor preview here.
        SetP5ScenarioObjects(true);
        preview = new GameObject("Scenario_A1_P5_AuthoredStrategies");
        CreateStrategyLabels(preview.transform);
        previewHint = ScenarioGuidanceOverlay.ShowObservation("Compare protection gained per metre of barrier", viewingDuration);
        FloodStudyLogger.BeginObservation();
        elapsed = 0f;
        phase = Phase.Preview;
    }

    void ShowQuestion()
    {
        Cleanup(ref previewHint);
        FloodStudyLogger.EndObservation();
        phase = Phase.Question;
        A1P5Panel.ShowQuestion(StartPreview, BeginDykeBuilding);
    }

    void BeginDykeBuilding(int answer)
    {
        selectedAnswer = answer;
        FloodStudyLogger.RecordAnswer(answer);
        FloodStudyLogger.BeginBuildingPhase();
        Cleanup(ref preview);
        // Hide the authored strategy dykes and their labels before the
        // participant builds one of the strategies themselves.
        SetP5ScenarioObjects(false);
        SetDykeConstruction(true, maxDykes, minDykeLengthMeters, maxDykeLengthMeters, 0f);
        prompt = A1P5Panel.CreateBuildingPrompt();
        waterDirectionHint = ScenarioGuidanceOverlay.ShowWaterFromLeft();
        phase = Phase.BuildingDykes;
    }

    void StartFlood()
    {
        if (floodPrefab == null)
        {
            Debug.LogError("A1-P5 flood prefab is missing.", this);
            return;
        }
        Cleanup(ref prompt); Cleanup(ref waterDirectionHint);
        SetDykeConstruction(false, 0, 0f, 0f, 0f);
        Transform parent = Find("FakeFloodEnvironment")?.transform;
        activeFlood = Instantiate(floodPrefab, parent, false);
        activeFlood.name = "Scenario_A1_P5_WithParticipantDykes";
        simulation = activeFlood.GetComponent<TerrainFloodSimulation>();
        if (simulation == null)
        {
            Debug.LogError("TerrainFloodSimulation is missing on the A1-P5 flood prefab.", this);
            return;
        }
        simulation.Begin();
        FloodStudyLogger.EndBuildingPhase();
        FloodStudyLogger.BeginSimulation();
        elapsed = 0f;
        phase = Phase.Flood;
    }

    void FinishFlood()
    {
        FloodStudyLogger.EndSimulation(simulation);
        simulation?.StopSimulation();
        Cleanup(ref activeFlood);
        // Keep the participant's dyke and scenario buildings visible through
        // the confidence and written-feedback screens. They are cleaned up
        // after the separate feedback scenario.
        phase = Phase.Confidence;
        A1P5Panel.ShowConfidence(selectedAnswer, StartFeedbackScenario);
    }

    void StartFeedbackScenario()
    {
        ScenarioFeedbackStage.Show("A1 - P5 | Resource-Efficient Placement", floodPrefab, floodDuration,
            new[] { "A1P5_Solution" }, () =>
        {
            SetP5ScenarioObjects(false);
            SetP5Houses(false);
            FloodStudyLogger.EndScenario();
            StartA1P6();
        });
    }

    void StartA1P6()
    {
        A1P6ScenarioFlow flow = gameObject.GetComponent<A1P6ScenarioFlow>();
        if (flow == null) flow = gameObject.AddComponent<A1P6ScenarioFlow>();
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

    static void ClearDykes()
    {
        DykeManagerTutorial manager = UnityEngine.Object.FindFirstObjectByType<DykeManagerTutorial>(FindObjectsInactive.Include);
        if (manager == null) return;
        manager.enabled = false;
        manager.freePlacement = false;
        manager.ClearFreePlacementLimits();
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

    static void SetP5ScenarioObjects(bool active)
    {
        // Authored strategy mapping: A1P5_1 = Strategy B,
        // A1P5_2 = Strategy A, A1P5_3 = Strategy C.
        SetObjectActive("A1P5_1", active);
        SetObjectActive("A1P5_2", active);
        SetObjectActive("A1P5_3", active);
    }

    void CreateStrategyLabels(Transform parent)
    {
        string[] objectNames = { "A1P5_1", "A1P5_2", "A1P5_3" };
        string[] labels =
        {
            "STRATEGY B\n1.5 m  |  3 buildings\n2 buildings / m",
            "STRATEGY A\n1.5 m  |  2 buildings\n1.3 buildings / m",
            "STRATEGY C\n2.5 m  |  4 buildings\n1.6 buildings / m"
        };

        for (int i = 0; i < objectNames.Length; i++)
        {
            GameObject strategy = Find(objectNames[i]);
            if (strategy == null || HasStrategyLabel(strategy)) continue;

            Bounds bounds = default;
            bool hasBounds = false;
            foreach (Renderer renderer in strategy.GetComponentsInChildren<Renderer>(true))
            {
                if (!hasBounds) { bounds = renderer.bounds; hasBounds = true; }
                else bounds.Encapsulate(renderer.bounds);
            }

            Vector3 position = hasBounds
                ? bounds.center + Vector3.up * (bounds.extents.y + .25f)
                : strategy.transform.position + Vector3.up * .25f;
            GameObject label = P2Panel.CanvasRoot("A1_P5_" + (char)('A' + i) + "_StrategyLabel", new Vector2(560, 125), .00115f);
            label.transform.SetParent(parent, true);
            label.transform.position = position;
            if (Camera.main != null)
                label.transform.rotation = Quaternion.LookRotation(label.transform.position - Camera.main.transform.position, Vector3.up);
            FloodLearningUI.Text("Text", label.transform, labels[i], 29, FontStyles.Bold, -8, 112,
                TextAlignmentOptions.Center, 8);
        }
    }

    static bool HasStrategyLabel(GameObject strategy)
    {
        foreach (TMP_Text text in strategy.GetComponentsInChildren<TMP_Text>(true))
            if (!string.IsNullOrEmpty(text.text) && text.text.IndexOf("STRATEGY", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        return false;
    }

    /// <summary>
    /// Shows or hides the FBX houses used only by A1-P5. Inspector references
    /// are optional; exact-name lookup also finds nested and inactive FBX
    /// instances in the active scene.
    /// </summary>
    void SetP5Houses(bool active)
    {
        GameObject[] assigned = { houseA, houseB, houseE, houseF, houseG, houseH, houseI, houseJ, houseK };
        string[] names = { "HOUSE_A", "HOUSE_B", "HOUSE_E", "HOUSE_F", "HOUSE_G", "HOUSE_H", "HOUSE_I", "HOUSE_J", "HOUSE_K" };
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

    static void SetObjectActive(string objectName, bool active)
    {
        GameObject target = Find(objectName);
        if (target != null) target.SetActive(active);
    }

    static void Cleanup(ref GameObject target)
    {
        if (target != null) UnityEngine.Object.Destroy(target);
        target = null;
    }

    void OnDestroy()
    {
        FloodStudyLogger.EndScenario();
        SetP5ScenarioObjects(false);
        SetP5Houses(false);
        questA?.Disable();
        questA?.Dispose();
        Cleanup(ref prompt); Cleanup(ref preview); Cleanup(ref previewHint);
        Cleanup(ref waterDirectionHint); Cleanup(ref activeFlood);
    }
}

static class A1P5Panel
{
    static readonly string[] AnswersA =
    {
        "A) Strategy A, with 2 buildings protected by a 1.5 m barrier.",
        "B) Strategy B, with 3 buildings protected by a 1.5 m barrier.",
        "C) Strategy C, with 4 buildings protected by a 2.5 m barrier.",
        "D) Strategies A and C provide the same ratio."
    };
    static readonly string[] AnswersB =
    {
        "A) Strategy A: about 1.3 buildings per metre.",
        "B) Strategy C: about 1.6 buildings per metre.",
        "C) Strategy B: 2 buildings per metre.",
        "D) All three have the same efficiency."
    };
    static bool UseB => ParticipantSession.IsCheckedIn && ParticipantSession.QuestionSet == ParticipantQuestionSet.B;
    static string[] CurrentAnswers => UseB ? AnswersB : AnswersA;
    static int CorrectAnswerIndex => UseB ? 2 : 1;

    public static GameObject CreateStartPrompt()
    {
        GameObject root = P2Panel.CanvasRoot("A1_P5_Start", new Vector2(1100, 350));
        FloodLearningUI.Text("Title", root.transform, "A1 - P5", 40, FontStyles.Bold, -35, 55, TextAlignmentOptions.Center, 40);
        FloodLearningUI.Text("Topic", root.transform, "Resource-Efficient Placement", 42, FontStyles.Bold, -110, 65, TextAlignmentOptions.Center, 40);
        FloodLearningUI.Text("Continue", root.transform, ParticipantInput.IsGamepadParticipant ? "Press B to continue" : "Press A to continue", 35, FontStyles.Normal, -220, 55, TextAlignmentOptions.Center, 40).color = new Color(.3f, .8f, 1f);
        FloodLearningUI.PlaceInFrontOfPlayer(root.transform, 2.1f);
        return root;
    }

    public static GameObject CreateBuildingPrompt()
    {
        GameObject root = P2Panel.CanvasRoot("A1_P5_Build", new Vector2(1100, 390));
        FloodLearningUI.Text("Title", root.transform, "A1 - P5  |  Build protection", 39, FontStyles.Bold, -35, 58, TextAlignmentOptions.Center, 45);
        string instructions = ParticipantInput.IsGamepadParticipant
            ? "Build one dyke up to 1.5 m. Hold right Grip and move the terrain cursor with the right thumbstick.\nPress Trigger once for the start and once for the end.\nPress A on a dyke to remove it. Release Grip to cancel. Press Y when finished."
            : "Build one dyke up to 1.5 metres long to protect the housing block.\nBuild with the trigger. Point at a dyke and press Grip to destroy it. Press A when you are finished.";
        FloodLearningUI.Text("Prompt", root.transform, instructions, 30, FontStyles.Bold, -135, 155, TextAlignmentOptions.Center, 55).color = new Color(.35f, .82f, 1f);
        FloodLearningUI.PlaceInFrontOfPlayer(root.transform, 2.1f);
        return root;
    }

    public static void ShowQuestion(Action repeat, Action<int> answer)
    {
        FloodAudio.PlayQuestion();
        GameObject root = P2Panel.CanvasRoot("Quiz_A1_P5", new Vector2(1600, 1180));
        string prefix = UseB ? "B1" : "A1";
        string question = UseB
            ? "Which strategy protects the most buildings for each metre of barrier?"
            : "Which strategy protects the most buildings per metre of barrier?";
        FloodLearningUI.Text("Title", root.transform, prefix + " - P5  |  Resource-Efficient Placement", 44, FontStyles.Bold, -28, 62, TextAlignmentOptions.Left, 55);
        FloodLearningUI.Text("Scenario", root.transform, "A residential housing block needs protection. Three strategies produce the following outcomes:\nStrategy A: 1.5 m barrier, 2 buildings protected\nStrategy B: 1.5 m barrier, 3 buildings protected\nStrategy C: 2.5 m barrier, 4 buildings protected", 28, FontStyles.Normal, -105, 165, TextAlignmentOptions.TopLeft, 55);
        FloodLearningUI.Text("Question", root.transform, question, 32, FontStyles.Bold, -285, 95, TextAlignmentOptions.TopLeft, 55);
        for (int i = 0; i < CurrentAnswers.Length; i++)
        {
            int n = i;
            FloodLearningUI.Button("Answer_" + (char)('A' + i), root.transform, CurrentAnswers[i], -395 - i * 145, 120, 25,
                () => { UnityEngine.Object.Destroy(root); answer(n); }, 1500);
        }
        FloodLearningUI.Button("Repeat", root.transform, "Repeat scenario", -990, 82, 29,
            () => { UnityEngine.Object.Destroy(root); repeat(); }, 390);
        FloodLearningUI.PlaceInFrontOfPlayer(root.transform, 2.25f);
    }

    public static void ShowConfidence(int answer, Action onCompleted = null)
    {
        GameObject root = P2Panel.CanvasRoot("Confidence_A1_P5", new Vector2(1500, 1080));
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
        FloodLearningUI.Text("Scale", group.transform, "Not confident                                      Moderately confident                                      Very confident", 23, FontStyles.Normal, -510, 55, TextAlignmentOptions.Center, 130).color = new Color(.75f, .85f, .95f);
        FloodLearningUI.Button("Confirm", group.transform, "Confirm confidence", -650, 95, 31,
            () =>
            {
                int confidence = Mathf.RoundToInt(slider.value) * 20;
                FloodStudyLogger.RecordConfidence(confidence);
                ShowFeedback(root, answer, confidence, onCompleted);
            }, 420);
        FloodLearningUI.PlaceInFrontOfPlayer(root.transform, 2.25f);
    }

    static void ShowFeedback(GameObject root, int answer, int confidence, Action onCompleted)
    {
        foreach (Transform child in root.transform) UnityEngine.Object.Destroy(child.gameObject);
        string prefix = UseB ? "B1" : "A1";
        string correctText = UseB
            ? "Correct answer: C) Strategy B: 2 buildings per metre."
            : "Correct answer: B) Strategy B, with 3 buildings protected by a 1.5 m barrier.";
        string explanation = UseB
            ? "Strategy A protects about 1.3 buildings per metre, Strategy B protects 2, and Strategy C protects about 1.6. Strategy B is therefore the most resource-efficient."
            : "Strategy A: 2 / 1.5 = about 1.3 buildings/m\nStrategy B: 3 / 1.5 = 2 buildings/m\nStrategy C: 4 / 2.5 = about 1.6 buildings/m\n\nStrategy B provides the highest protection per metre of barrier, even though Strategy C protects more buildings in total. This makes Strategy B the most resource-efficient placement strategy.";
        TMP_Text result = FloodLearningUI.Text("Result", root.transform,
            answer == CorrectAnswerIndex ? $"Correct!  |  Confidence: {confidence}%" : $"Your answer: {(char)('A' + answer)}  |  Confidence: {confidence}%",
            39, FontStyles.Bold, -105, 62, TextAlignmentOptions.Center, 65);
        result.color = answer == CorrectAnswerIndex ? new Color(.4f, 1f, .55f) : new Color(1f, .68f, .35f);
        FloodLearningUI.Text("Correct", root.transform,
            correctText,
            31, FontStyles.Bold, -205, 120, TextAlignmentOptions.TopLeft, 70).color = new Color(.4f, 1f, .55f);
        FloodLearningUI.Text("ExplanationTitle", root.transform, "Explanation", 34, FontStyles.Bold, -370, 55, TextAlignmentOptions.Left, 70);
        FloodLearningUI.Text("Explanation", root.transform,
            explanation,
            30, FontStyles.Normal, -435, 230, TextAlignmentOptions.TopLeft, 70);
        FloodLearningUI.Button("Finish", root.transform, "Finish", -735, 82, 29,
            () => { UnityEngine.Object.Destroy(root); onCompleted?.Invoke(); }, 360);
        Debug.Log($"{prefix}-P5 result: answer={(char)('A' + answer)}, correct={answer == CorrectAnswerIndex}, confidence={confidence}");
    }
}
