using System;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>A1-P6: evaluate protection and unintended consequences at system level.</summary>
public sealed class A1P6ScenarioFlow : MonoBehaviour
{
    enum Phase { Idle, WaitingForA, Preview, Question, BuildingDykes, Flood, Confidence }

    [Header("A1-P6 buildings")]
    [SerializeField] GameObject houseL;
    [SerializeField] GameObject houseM;
    [SerializeField] GameObject houseN;
    [SerializeField] GameObject houseO;
    [SerializeField] GameObject houseP;
    [SerializeField] GameObject houseQ;
    [SerializeField] GameObject houseR;
    [SerializeField] GameObject houseS;
    [SerializeField] GameObject hospitalB;

    [Header("A1-P6 dyke limits (metres)")]
    [Min(1)] public int maxDykes = 2;
    [Min(0f)] public float maxTotalDykeLengthMeters = 8f;

    [Header("A1-P6 feedback simulation")]
    [Min(0f)] public float feedbackSimulationDuration = 10f;

    InputAction questA;
    GameObject floodPrefab, prompt, preview, previewHint, waterDirectionHint, activeFlood;
    TerrainFloodSimulation simulation;
    float viewingDuration, floodDuration, elapsed;
    int selectedAnswer = -1;
    Phase phase;

    void Awake()
    {
        ScenarioFeedbackStage.HideAllSolutions();
        SetP6ScenarioObjects(false);
        SetP6Buildings(false);
    }

    public void Begin(GameObject prefab, float previewSeconds, float simulationSeconds)
    {
        FloodStudyLogger.BeginScenario("A1-P6", "System-Level Consequences", previewSeconds);
        SetP5ScenarioObjects(false);
        // The old A1P6_1/A1P6_2 procedural comparison is not used here.
        SetP6ScenarioObjects(false);
        SetP6Buildings(true);
        floodPrefab = prefab;
        viewingDuration = previewSeconds;
        floodDuration = simulationSeconds;
        questA ??= new InputAction("A1-P6 Continue", InputActionType.Button,
            "<XRController>{RightHand}/primaryButton");
        questA.Enable();
        prompt = A1P6Panel.CreateStartPrompt();
        phase = Phase.WaitingForA;
    }

    public void BeginDebug(GameObject prefab, float previewSeconds, float simulationSeconds)
    {
        Begin(prefab, previewSeconds, simulationSeconds);
        StartPreview();
    }

    void Update()
    {
        if (phase == Phase.BuildingDykes) EnforceMaximumDykes();
        if (phase == Phase.WaitingForA && ParticipantInput.StartWasPressedThisFrame(questA)) StartPreview();
        if (phase == Phase.BuildingDykes && ParticipantInput.StartWasPressedThisFrame(questA)) StartFlood();
        if (phase != Phase.Preview && phase != Phase.Flood) return;
        elapsed += Time.deltaTime;
        if (phase == Phase.Preview && elapsed >= viewingDuration) ShowQuestion();
        else if (phase == Phase.Flood && elapsed >= floodDuration) FinishFlood();
    }

    void EnforceMaximumDykes()
    {
        DykeManagerTutorial manager = UnityEngine.Object.FindFirstObjectByType<DykeManagerTutorial>(FindObjectsInactive.Include);
        if (manager == null) return;
        A1P1ScenarioDyke[] dykes = manager.GetComponentsInChildren<A1P1ScenarioDyke>(true);
        for (int i = maxDykes; i < dykes.Length; i++)
            if (dykes[i] != null) UnityEngine.Object.Destroy(dykes[i].gameObject);
    }

    void StartPreview()
    {
        Cleanup(ref prompt); Cleanup(ref preview); Cleanup(ref previewHint);
        SetDykeConstruction(false, 0, 0f); ClearDykes();
        preview = new GameObject("Scenario_A1_P6_PriorityLabels");
        CreatePriorityLabels(preview.transform);
        previewHint = ScenarioGuidanceOverlay.ShowObservation("Compare building priorities and the consequences of redirected water", viewingDuration);
        FloodStudyLogger.BeginObservation();
        elapsed = 0f;
        phase = Phase.Preview;
    }

    void ShowQuestion()
    {
        Cleanup(ref previewHint);
        FloodStudyLogger.EndObservation();
        phase = Phase.Question;
        A1P6Panel.ShowQuestion(StartPreview, BeginDykeBuilding);
    }

    void BeginDykeBuilding(int answer)
    {
        selectedAnswer = answer;
        FloodStudyLogger.RecordAnswer(answer);
        FloodStudyLogger.BeginBuildingPhase();
        Cleanup(ref preview);
        SetDykeConstruction(true, maxDykes, maxTotalDykeLengthMeters);
        prompt = A1P6Panel.CreateBuildingPrompt();
        waterDirectionHint = ScenarioGuidanceOverlay.ShowWaterFromLeft();
        phase = Phase.BuildingDykes;
    }

    void StartFlood()
    {
        if (floodPrefab == null)
        {
            Debug.LogError("A1-P6 flood prefab is missing.", this);
            return;
        }
        Cleanup(ref prompt); Cleanup(ref waterDirectionHint);
        SetDykeConstruction(false, 0, 0f);
        Transform parent = Find("FakeFloodEnvironment")?.transform;
        activeFlood = Instantiate(floodPrefab, parent, false);
        activeFlood.name = "Scenario_A1_P6_WithParticipantDykes";
        simulation = activeFlood.GetComponent<TerrainFloodSimulation>();
        if (simulation == null)
        {
            Debug.LogError("TerrainFloodSimulation is missing on the A1-P6 flood prefab.", this);
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
        // Keep the participant's dykes and scenario buildings visible through
        // the confidence and written-feedback screens. They are cleaned up
        // after the separate feedback scenario.
        phase = Phase.Confidence;
        A1P6Panel.ShowConfidence(selectedAnswer, StartFeedbackScenario);
    }

    void StartFeedbackScenario()
    {
        ScenarioFeedbackStage.Show("A1 - P6 | System-Level Consequences", floodPrefab, feedbackSimulationDuration,
            new[] { "A1P6_Solution_A", "A1P6_Solution_B" }, () =>
        {
            SetP6ScenarioObjects(false);
            SetP6Buildings(false);
            FloodStudyLogger.EndScenario();
            GeneralDiscomfortSurvey.Show("after_A1_P6", ParticipationEndUI.Show);
        });
    }

    static void SetDykeConstruction(bool enabled, int maxDykes, float maxTotalLengthMeters)
    {
        DykeManagerTutorial manager = UnityEngine.Object.FindFirstObjectByType<DykeManagerTutorial>(FindObjectsInactive.Include);
        if (manager != null)
        {
            manager.gameObject.SetActive(true);
            manager.freePlacement = enabled;
            manager.enabled = enabled;
            if (enabled) manager.ConfigureFreePlacementLimits(maxDykes, 0f, 0f, maxTotalLengthMeters);
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
        SetObjectActive("A1P5_1", active);
        SetObjectActive("A1P5_2", active);
        SetObjectActive("A1P5_3", active);
    }

    static void SetP6ScenarioObjects(bool active)
    {
        SetObjectActive("A1P6_1", active);
        SetObjectActive("A1P6_2", active);
    }

    void SetP6Buildings(bool active)
    {
        GameObject[] assigned = { houseL, houseM, houseN, houseO, houseP, houseQ, houseR, houseS, hospitalB };
        string[] names = { "HOUSE_L", "HOUSE_M", "HOUSE_N", "HOUSE_O", "HOUSE_P", "HOUSE_Q", "HOUSE_R", "HOUSE_S", "HOSPITAL_B" };
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

    void CreatePriorityLabels(Transform parent)
    {
        CreatePriorityLabel(parent, "ImportantDistrict_LM", "IMPORTANT DISTRICT", new[] { "HOUSE_L", "HOUSE_M" });
        CreatePriorityLabel(parent, "ImportantDistrict_OQ", "IMPORTANT DISTRICT", new[] { "HOUSE_O", "HOUSE_P", "HOUSE_Q" });
        CreatePriorityLabel(parent, "LowPriorityDistrict_RS", "LOW-PRIORITY DISTRICT", new[] { "HOUSE_R", "HOUSE_S" });
        CreatePriorityLabel(parent, "VeryImportantHospital", "VERY IMPORTANT BUILDING", new[] { "HOSPITAL_B" });
    }

    void CreatePriorityLabel(Transform parent, string name, string text, string[] objectNames)
    {
        if (!TryGetBounds(objectNames, out Bounds bounds)) return;
        GameObject label = P2Panel.CanvasRoot(name, new Vector2(560, 105), .00115f);
        label.transform.SetParent(parent, true);
        label.transform.position = bounds.center + Vector3.up * (bounds.extents.y + .35f);
        if (Camera.main != null)
            label.transform.rotation = Quaternion.LookRotation(label.transform.position - Camera.main.transform.position, Vector3.up);
        FloodLearningUI.Text("Text", label.transform, text, 29, FontStyles.Bold, -8, 92,
            TextAlignmentOptions.Center, 8);
    }

    static bool TryGetBounds(string[] objectNames, out Bounds bounds)
    {
        bounds = default;
        bool found = false;
        foreach (string objectName in objectNames)
        {
            foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                if (!string.Equals(child.name, objectName, StringComparison.OrdinalIgnoreCase)) continue;
                foreach (Renderer renderer in child.GetComponentsInChildren<Renderer>(true))
                {
                    if (!found) { bounds = renderer.bounds; found = true; }
                    else bounds.Encapsulate(renderer.bounds);
                }
            }
        }
        return found;
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
        SetP6ScenarioObjects(false);
        SetP6Buildings(false);
        questA?.Disable();
        questA?.Dispose();
        Cleanup(ref prompt); Cleanup(ref preview); Cleanup(ref previewHint);
        Cleanup(ref waterDirectionHint); Cleanup(ref activeFlood);
    }
}

static class A1P6Panel
{
    static readonly string[] AnswersA =
    {
        "A) Protecting one area removes the need to examine the rest of the system.",
        "B) Higher upstream water always means that the barrier has failed.",
        "C) A barrier can reduce flooding locally while increasing flood risk elsewhere.",
        "D) Flood barriers only affect the exact area where they are built."
    };
    static readonly string[] AnswersB =
    {
        "A) A barrier may protect one area but raise risk elsewhere.",
        "B) An upstream rise always means the barrier failed.",
        "C) Other areas do not matter after the target is protected.",
        "D) A barrier affects only the place where it is built."
    };
    static bool UseB => ParticipantSession.IsCheckedIn && ParticipantSession.QuestionSet == ParticipantQuestionSet.B;
    static string[] CurrentAnswers => UseB ? AnswersB : AnswersA;
    static int CorrectAnswerIndex => UseB ? 0 : 2;

    public static GameObject CreateStartPrompt()
    {
        GameObject root = P2Panel.CanvasRoot("A1_P6_Start", new Vector2(1100, 350));
        FloodLearningUI.Text("Title", root.transform, "A1 - P6", 40, FontStyles.Bold, -35, 55, TextAlignmentOptions.Center, 40);
        FloodLearningUI.Text("Topic", root.transform, "System-Level Consequences", 42, FontStyles.Bold, -110, 65, TextAlignmentOptions.Center, 40);
        FloodLearningUI.Text("Continue", root.transform, ParticipantInput.IsGamepadParticipant ? "Press B to continue" : "Press A to continue", 35, FontStyles.Normal, -220, 55, TextAlignmentOptions.Center, 40).color = new Color(.3f, .8f, 1f);
        FloodLearningUI.PlaceInFrontOfPlayer(root.transform, 2.1f);
        return root;
    }

    public static GameObject CreateBuildingPrompt()
    {
        GameObject root = P2Panel.CanvasRoot("A1_P6_Build", new Vector2(1100, 390));
        FloodLearningUI.Text("Title", root.transform, "A1 - P6  |  Build protection", 39, FontStyles.Bold, -35, 58, TextAlignmentOptions.Center, 45);
        string instructions = ParticipantInput.IsGamepadParticipant
            ? "Build up to two dykes; the combined length must stay under 8 m. Hold right Grip and move the terrain cursor with the right thumbstick.\nPress Trigger once for the start and once for the end.\nPress A on a dyke to remove it. Release Grip to cancel. Press B when finished."
            : "Build up to two dykes with a combined length of no more than 8 metres.\nBuild with the trigger. Point at a dyke and press Grip to destroy it. Press A when you are finished.";
        FloodLearningUI.Text("Prompt", root.transform, instructions, 30, FontStyles.Bold, -135, 145, TextAlignmentOptions.Center, 55).color = new Color(.35f, .82f, 1f);
        FloodLearningUI.PlaceInFrontOfPlayer(root.transform, 2.1f);
        return root;
    }

    public static void ShowQuestion(Action repeat, Action<int> answer)
    {
        FloodAudio.PlayQuestion();
        GameObject root = P2Panel.CanvasRoot("Quiz_A1_P6", new Vector2(1600, 1180));
        string prefix = UseB ? "B1" : "A1";
        string question = UseB
            ? "What should be considered when judging this protection measure?"
            : "What does this show about flood protection?";
        FloodLearningUI.Text("Title", root.transform, prefix + " - P6  |  System-Level Consequences", 44, FontStyles.Bold, -28, 62, TextAlignmentOptions.Left, 55);
        FloodLearningUI.Text("Scenario", root.transform,
            "You may build a maximum of two flood barriers. A barrier reduces flooding in one area, but water depth increases upstream and more water begins to flow toward another populated area.",
            28, FontStyles.Normal, -105, 170, TextAlignmentOptions.TopLeft, 55);
        FloodLearningUI.Text("Question", root.transform, question, 34, FontStyles.Bold, -305, 65, TextAlignmentOptions.TopLeft, 55);
        for (int i = 0; i < CurrentAnswers.Length; i++)
        {
            int n = i;
            FloodLearningUI.Button("Answer_" + (char)('A' + i), root.transform, CurrentAnswers[i], -385 - i * 135, 112, 24,
                () => { UnityEngine.Object.Destroy(root); answer(n); }, 1500);
        }
        FloodLearningUI.Button("Repeat", root.transform, "Repeat scenario", -965, 82, 29,
            () => { UnityEngine.Object.Destroy(root); repeat(); }, 390);
        FloodLearningUI.PlaceInFrontOfPlayer(root.transform, 2.25f);
    }

    public static void ShowConfidence(int answer, Action onCompleted = null)
    {
        GameObject root = P2Panel.CanvasRoot("Confidence_A1_P6", new Vector2(1500, 1080));
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
            ? "Correct answer: A) A barrier may protect one area but raise risk elsewhere."
            : "Correct answer: C) A barrier can reduce flooding locally while increasing flood risk elsewhere.";
        string explanation = UseB
            ? "Flood protection must be evaluated for the whole system. A barrier can change water storage and redirect risk to another area."
            : "Flood protection can change water storage and flow pathways. A barrier may protect one location but redirect or accumulate water in other areas.\n\nTherefore, the consequences for the whole system must be considered; not only the protected area.";
        TMP_Text result = FloodLearningUI.Text("Result", root.transform,
            answer == CorrectAnswerIndex ? $"Correct!  |  Confidence: {confidence}%" : $"Your answer: {(char)('A' + answer)}  |  Confidence: {confidence}%",
            39, FontStyles.Bold, -105, 62, TextAlignmentOptions.Center, 65);
        result.color = answer == CorrectAnswerIndex ? new Color(.4f, 1f, .55f) : new Color(1f, .68f, .35f);
        FloodLearningUI.Text("Correct", root.transform,
            correctText,
            30, FontStyles.Bold, -205, 145, TextAlignmentOptions.TopLeft, 70).color = new Color(.4f, 1f, .55f);
        FloodLearningUI.Text("ExplanationTitle", root.transform, "Explanation", 34, FontStyles.Bold, -415, 55, TextAlignmentOptions.Left, 70);
        FloodLearningUI.Text("Explanation", root.transform,
            explanation,
            29, FontStyles.Normal, -445, 280, TextAlignmentOptions.TopLeft, 70);
        FloodLearningUI.Button("Finish", root.transform, "Finish", -780, 82, 29,
            () => { UnityEngine.Object.Destroy(root); onCompleted?.Invoke(); }, 360);
        Debug.Log($"{prefix}-P6 result: answer={(char)('A' + answer)}, correct={answer == CorrectAnswerIndex}, confidence={confidence}");
    }
}
