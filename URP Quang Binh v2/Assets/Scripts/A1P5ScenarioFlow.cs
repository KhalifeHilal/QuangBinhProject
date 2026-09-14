using System;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>A1-P5: choose the corridor that gives the best protection per metre.</summary>
public sealed class A1P5ScenarioFlow : MonoBehaviour
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
        SetP4ScenarioObjects(false);
        SetP5ScenarioObjects(true);
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
        preview = new GameObject("Scenario_A1_P5_ResourceEfficientPlacement");
        preview.AddComponent<FloodResourceCorridorsScenarioA1P5>();
        previewHint = ScenarioGuidanceOverlay.ShowObservation("Compare protection gained per metre of barrier");
        elapsed = 0f;
        phase = Phase.Preview;
    }

    void ShowQuestion()
    {
        Cleanup(ref previewHint);
        phase = Phase.Question;
        A1P5Panel.ShowQuestion(StartPreview, BeginDykeBuilding);
    }

    void BeginDykeBuilding(int answer)
    {
        selectedAnswer = answer;
        Cleanup(ref preview);
        SetDykeConstruction(true);
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
        SetDykeConstruction(false);
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
        elapsed = 0f;
        phase = Phase.Flood;
    }

    void FinishFlood()
    {
        simulation?.StopSimulation();
        Cleanup(ref activeFlood);
        ClearDykes();
        SetP5ScenarioObjects(false);
        phase = Phase.Confidence;
        A1P5Panel.ShowConfidence(selectedAnswer);
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

    static void SetP4ScenarioObjects(bool active)
    {
        SetObjectActive("A1P4_1", active);
        SetObjectActive("A1P4_2", active);
    }

    static void SetP5ScenarioObjects(bool active)
    {
        SetObjectActive("A1P5_1", active);
        SetObjectActive("A1P5_2", active);
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
        SetP5ScenarioObjects(false);
        questA?.Disable();
        questA?.Dispose();
        Cleanup(ref prompt); Cleanup(ref preview); Cleanup(ref previewHint);
        Cleanup(ref waterDirectionHint); Cleanup(ref activeFlood);
    }
}

/// <summary>Three procedural corridors with their material costs and benefits.</summary>
public sealed class FloodResourceCorridorsScenarioA1P5 : MonoBehaviour
{
    Material corridorMaterial;
    Material highlightMaterial;

    void Awake()
    {
        Renderer terrain = FindRenderer("SM_Tutorial_Terrain_01");
        if (terrain == null)
        {
            Debug.LogWarning("A1-P5 could not find SM_Tutorial_Terrain_01.", this);
            return;
        }
        Bounds b = terrain.bounds;
        float y = b.max.y + .055f;
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Sprites/Default");
        corridorMaterial = new Material(shader) { color = new Color(.02f, .58f, 1f, .95f) };
        highlightMaterial = new Material(shader) { color = new Color(.08f, .82f, 1f, .98f) };

        CreateCorridor("Corridor_A", new[] { Point(b, .06f, .68f, y), Point(b, .35f, .62f, y), Point(b, .92f, .67f, y) }, corridorMaterial, .075f,
            "CORRIDOR A\n100 m  |  40 buildings\n0.40 buildings / m", Point(b, .50f, .77f, y + .25f), 590);
        CreateCorridor("Corridor_B", new[] { Point(b, .06f, .47f, y + .015f), Point(b, .32f, .43f, y + .015f), Point(b, .60f, .49f, y + .015f), Point(b, .92f, .45f, y + .015f) }, highlightMaterial, .09f,
            "CORRIDOR B\n60 m  |  32 buildings\n0.53 buildings / m", Point(b, .49f, .30f, y + .25f), 590);
        CreateCorridor("Corridor_C", new[] { Point(b, .06f, .25f, y), Point(b, .37f, .29f, y), Point(b, .92f, .23f, y) }, corridorMaterial, .075f,
            "CORRIDOR C\n80 m  |  36 buildings\n0.45 buildings / m", Point(b, .50f, .13f, y + .25f), 590);
    }

    Vector3 Point(Bounds b, float x01, float z01, float y)
    {
        return new Vector3(Mathf.Lerp(b.min.x, b.max.x, x01), y, Mathf.Lerp(b.min.z, b.max.z, z01));
    }

    void CreateCorridor(string name, Vector3[] points, Material material, float width, string label, Vector3 labelPosition, float labelWidth)
    {
        GameObject lineObject = new GameObject(name, typeof(LineRenderer));
        lineObject.transform.SetParent(transform, true);
        LineRenderer line = lineObject.GetComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.positionCount = points.Length;
        line.SetPositions(points);
        line.widthMultiplier = width;
        line.numCornerVertices = 6;
        line.numCapVertices = 6;
        line.material = material;
        CreateLabel(label, labelPosition, labelWidth);
    }

    void CreateLabel(string value, Vector3 position, float width)
    {
        GameObject canvas = P2Panel.CanvasRoot(value.Substring(0, 1) + "_Resource_Label", new Vector2(width, 125), .00115f);
        canvas.transform.SetParent(transform, true);
        canvas.transform.position = position;
        if (Camera.main != null)
            canvas.transform.rotation = Quaternion.LookRotation(canvas.transform.position - Camera.main.transform.position, Vector3.up);
        FloodLearningUI.Text("Text", canvas.transform, value, 29, FontStyles.Bold, -8, 112, TextAlignmentOptions.Center, 8);
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
        if (corridorMaterial != null) Destroy(corridorMaterial);
        if (highlightMaterial != null) Destroy(highlightMaterial);
    }
}

static class A1P5Panel
{
    static readonly string[] Answers =
    {
        "A) Corridor A, because it produces the largest absolute reduction.",
        "B) Corridor B, because it produces the largest expected reduction per metre of barrier.",
        "C) Corridor C, because its absolute and relative values are intermediate.",
        "D) Corridor A, because wider corridors should always be protected first."
    };

    public static GameObject CreateStartPrompt()
    {
        GameObject root = P2Panel.CanvasRoot("A1_P5_Start", new Vector2(1100, 350));
        FloodLearningUI.Text("Title", root.transform, "A1 - P5", 40, FontStyles.Bold, -35, 55, TextAlignmentOptions.Center, 40);
        FloodLearningUI.Text("Topic", root.transform, "Resource-Efficient Placement", 42, FontStyles.Bold, -110, 65, TextAlignmentOptions.Center, 40);
        FloodLearningUI.Text("Continue", root.transform, "Press A to continue", 35, FontStyles.Normal, -220, 55, TextAlignmentOptions.Center, 40).color = new Color(.3f, .8f, 1f);
        FloodLearningUI.PlaceInFrontOfPlayer(root.transform, 2.1f);
        return root;
    }

    public static GameObject CreateBuildingPrompt()
    {
        GameObject root = P2Panel.CanvasRoot("A1_P5_Build", new Vector2(1100, 390));
        FloodLearningUI.Text("Title", root.transform, "A1 - P5  |  Build protection", 39, FontStyles.Bold, -35, 58, TextAlignmentOptions.Center, 45);
        FloodLearningUI.Text("Prompt", root.transform, "Build dykes for your answer anywhere on the terrain with the trigger.\nPoint at one and press Grip to destroy it. Press A when you are finished.", 30, FontStyles.Bold, -135, 125, TextAlignmentOptions.Center, 55).color = new Color(.35f, .82f, 1f);
        FloodLearningUI.PlaceInFrontOfPlayer(root.transform, 2.1f);
        return root;
    }

    public static void ShowQuestion(Action repeat, Action<int> answer)
    {
        GameObject root = P2Panel.CanvasRoot("Quiz_A1_P5", new Vector2(1600, 1180));
        FloodLearningUI.Text("Title", root.transform, "A1 - P5  |  Resource-Efficient Placement", 44, FontStyles.Bold, -28, 62, TextAlignmentOptions.Left, 55);
        FloodLearningUI.Text("Scenario", root.transform, "You have enough material to protect only one of three flood-entry corridors.\nCorridor A: 100 m barrier required; expected reduction = 40 exposed buildings\nCorridor B: 60 m barrier required; expected reduction = 32 exposed buildings\nCorridor C: 80 m barrier required; expected reduction = 36 exposed buildings", 28, FontStyles.Normal, -105, 165, TextAlignmentOptions.TopLeft, 55);
        FloodLearningUI.Text("Question", root.transform, "If barrier material is the primary constraint and protection efficiency is the objective, which corridor should be prioritized?", 32, FontStyles.Bold, -285, 95, TextAlignmentOptions.TopLeft, 55);
        for (int i = 0; i < Answers.Length; i++)
        {
            int n = i;
            FloodLearningUI.Button("Answer_" + (char)('A' + i), root.transform, Answers[i], -395 - i * 145, 120, 25,
                () => { UnityEngine.Object.Destroy(root); answer(n); }, 1500);
        }
        FloodLearningUI.Button("Repeat", root.transform, "Repeat scenario", -990, 82, 29,
            () => { UnityEngine.Object.Destroy(root); repeat(); }, 390);
        FloodLearningUI.PlaceInFrontOfPlayer(root.transform, 2.25f);
    }

    public static void ShowConfidence(int answer)
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
            answer == 1 ? $"Correct!  |  Confidence: {confidence}%" : $"Your answer: {(char)('A' + answer)}  |  Confidence: {confidence}%",
            39, FontStyles.Bold, -105, 62, TextAlignmentOptions.Center, 65);
        result.color = answer == 1 ? new Color(.4f, 1f, .55f) : new Color(1f, .68f, .35f);
        FloodLearningUI.Text("Correct", root.transform,
            "Correct answer: B) Corridor B, because it produces the largest expected reduction per metre of barrier.",
            31, FontStyles.Bold, -205, 120, TextAlignmentOptions.TopLeft, 70).color = new Color(.4f, 1f, .55f);
        FloodLearningUI.Text("ExplanationTitle", root.transform, "Explanation", 34, FontStyles.Bold, -370, 55, TextAlignmentOptions.Left, 70);
        FloodLearningUI.Text("Explanation", root.transform,
            "A = 40 / 100 = 0.40\nB = 32 / 60 ≈ 0.53\nC = 36 / 80 = 0.45\n\nCorridor B gives the greatest reduction in exposure relative to the amount of barrier required.",
            30, FontStyles.Normal, -435, 230, TextAlignmentOptions.TopLeft, 70);
        FloodLearningUI.Button("Finish", root.transform, "Finish", -735, 82, 29,
            () => UnityEngine.Object.Destroy(root), 360);
        Debug.Log($"A1-P5 result: answer={(char)('A' + answer)}, correct={answer == 1}, confidence={confidence}");
    }
}
