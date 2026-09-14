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

    InputAction questA;
    GameObject floodPrefab, prompt, preview, previewHint, waterDirectionHint, activeFlood;
    TerrainFloodSimulation simulation;
    float viewingDuration, floodDuration, elapsed;
    int selectedAnswer = -1;
    Phase phase;

    public void Begin(GameObject prefab, float previewSeconds, float simulationSeconds)
    {
        SetP5ScenarioObjects(false);
        SetP6ScenarioObjects(true);
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
        preview = new GameObject("Scenario_A1_P6_SystemLevelConsequences");
        preview.AddComponent<FloodSystemLevelScenarioA1P6>();
        previewHint = ScenarioGuidanceOverlay.ShowObservation("Compare local protection with exposure elsewhere");
        elapsed = 0f;
        phase = Phase.Preview;
    }

    void ShowQuestion()
    {
        Cleanup(ref previewHint);
        phase = Phase.Question;
        A1P6Panel.ShowQuestion(StartPreview, BeginDykeBuilding);
    }

    void BeginDykeBuilding(int answer)
    {
        selectedAnswer = answer;
        Cleanup(ref preview);
        SetDykeConstruction(true);
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
        SetDykeConstruction(false);
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
        elapsed = 0f;
        phase = Phase.Flood;
    }

    void FinishFlood()
    {
        simulation?.StopSimulation();
        Cleanup(ref activeFlood);
        ClearDykes();
        SetP6ScenarioObjects(false);
        phase = Phase.Confidence;
        A1P6Panel.ShowConfidence(selectedAnswer);
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

    static void SetP5ScenarioObjects(bool active)
    {
        SetObjectActive("A1P5_1", active);
        SetObjectActive("A1P5_2", active);
    }

    static void SetP6ScenarioObjects(bool active)
    {
        SetObjectActive("A1P6_1", active);
        SetObjectActive("A1P6_2", active);
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
        SetP6ScenarioObjects(false);
        questA?.Disable();
        questA?.Dispose();
        Cleanup(ref prompt); Cleanup(ref preview); Cleanup(ref previewHint);
        Cleanup(ref waterDirectionHint); Cleanup(ref activeFlood);
    }
}

/// <summary>Procedural comparison of two strategies and their system-level effects.</summary>
public sealed class FloodSystemLevelScenarioA1P6 : MonoBehaviour
{
    Material pathMaterial;
    Material barrierMaterial;

    void Awake()
    {
        Renderer terrain = FindRenderer("SM_Tutorial_Terrain_01");
        if (terrain == null)
        {
            Debug.LogWarning("A1-P6 could not find SM_Tutorial_Terrain_01.", this);
            return;
        }
        Bounds b = terrain.bounds;
        float y = b.max.y + .055f;
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Sprites/Default");
        pathMaterial = new Material(shader) { color = new Color(.02f, .58f, 1f, .95f) };
        barrierMaterial = new Material(shader) { color = new Color(.95f, .48f, .08f, 1f) };

        Vector3 targetA = Point(b, .23f, .66f, y + .01f);
        Vector3 otherA = Point(b, .79f, .25f, y + .01f);
        CreateLine("Strategy_A_TargetProtection", new[] { Point(b, .05f, .66f, y), targetA }, pathMaterial, .12f);
        CreateLine("Strategy_A_RedirectedFlood", new[] { Point(b, .45f, .48f, y + .015f), otherA, Point(b, .95f, .18f, y + .015f) }, pathMaterial, .105f);
        CreateLine("Strategy_A_Barrier", Point(b, .41f, .34f, y), Point(b, .41f, .78f, y), barrierMaterial, .075f);
        CreateLabel("STRATEGY A\n95% target district protected\nHIGH additional exposure elsewhere", Point(b, .26f, .80f, y + .30f), 640);
        CreateLabel("POPULATED DISTRICT\nadditional flooding", Point(b, .76f, .16f, y + .30f), 500);

        Vector3 targetB = Point(b, .25f, .40f, y + .01f);
        Vector3 openArea = Point(b, .79f, .78f, y + .01f);
        CreateLine("Strategy_B_TargetProtection", new[] { Point(b, .05f, .40f, y), targetB }, pathMaterial, .105f);
        CreateLine("Strategy_B_RedirectedFlood", new[] { Point(b, .45f, .43f, y + .015f), openArea, Point(b, .95f, .86f, y + .015f) }, pathMaterial, .075f);
        CreateLine("Strategy_B_Barrier", Point(b, .41f, .20f, y), Point(b, .41f, .61f, y), barrierMaterial, .075f);
        CreateLabel("STRATEGY B\n88% target district protected\nLOW additional exposure elsewhere", Point(b, .26f, .25f, y + .30f), 640);
        CreateLabel("UNINHABITED OPEN AREA\nredirected water", Point(b, .76f, .89f, y + .30f), 520);
    }

    Vector3 Point(Bounds b, float x01, float z01, float y)
    {
        return new Vector3(Mathf.Lerp(b.min.x, b.max.x, x01), y,
            Mathf.Lerp(b.min.z, b.max.z, z01));
    }

    void CreateLine(string name, Vector3[] points, Material material, float width)
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
    }

    void CreateLine(string name, Vector3 start, Vector3 end, Material material, float width)
    {
        CreateLine(name, new[] { start, end }, material, width);
    }

    void CreateLabel(string value, Vector3 position, float width)
    {
        GameObject canvas = P2Panel.CanvasRoot(value.Substring(0, 1) + "_System_Label", new Vector2(width, 120), .0011f);
        canvas.transform.SetParent(transform, true);
        canvas.transform.position = position;
        if (Camera.main != null)
            canvas.transform.rotation = Quaternion.LookRotation(canvas.transform.position - Camera.main.transform.position, Vector3.up);
        FloodLearningUI.Text("Text", canvas.transform, value, 29, FontStyles.Bold, -8, 108, TextAlignmentOptions.Center, 8);
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
        if (pathMaterial != null) Destroy(pathMaterial);
        if (barrierMaterial != null) Destroy(barrierMaterial);
    }
}

static class A1P6Panel
{
    static readonly string[] Answers =
    {
        "A) Strategy A is necessarily superior because target-area protection should be evaluated independently.",
        "B) Both are equally successful because both protect most of the target area.",
        "C) Strategy A is preferable because redistribution is irrelevant outside the original target.",
        "D) Strategy B may represent the better system-level solution because it achieves substantial protection while producing less additional exposure elsewhere."
    };

    public static GameObject CreateStartPrompt()
    {
        GameObject root = P2Panel.CanvasRoot("A1_P6_Start", new Vector2(1100, 350));
        FloodLearningUI.Text("Title", root.transform, "A1 - P6", 40, FontStyles.Bold, -35, 55, TextAlignmentOptions.Center, 40);
        FloodLearningUI.Text("Topic", root.transform, "System-Level Consequences", 42, FontStyles.Bold, -110, 65, TextAlignmentOptions.Center, 40);
        FloodLearningUI.Text("Continue", root.transform, "Press A to continue", 35, FontStyles.Normal, -220, 55, TextAlignmentOptions.Center, 40).color = new Color(.3f, .8f, 1f);
        FloodLearningUI.PlaceInFrontOfPlayer(root.transform, 2.1f);
        return root;
    }

    public static GameObject CreateBuildingPrompt()
    {
        GameObject root = P2Panel.CanvasRoot("A1_P6_Build", new Vector2(1100, 390));
        FloodLearningUI.Text("Title", root.transform, "A1 - P6  |  Build protection", 39, FontStyles.Bold, -35, 58, TextAlignmentOptions.Center, 45);
        FloodLearningUI.Text("Prompt", root.transform, "Build dykes for your answer anywhere on the terrain with the trigger.\nPoint at one and press Grip to destroy it. Press A when you are finished.", 30, FontStyles.Bold, -135, 125, TextAlignmentOptions.Center, 55).color = new Color(.35f, .82f, 1f);
        FloodLearningUI.PlaceInFrontOfPlayer(root.transform, 2.1f);
        return root;
    }

    public static void ShowQuestion(Action repeat, Action<int> answer)
    {
        GameObject root = P2Panel.CanvasRoot("Quiz_A1_P6", new Vector2(1600, 1180));
        FloodLearningUI.Text("Title", root.transform, "A1 - P6  |  System-Level Consequences", 44, FontStyles.Bold, -28, 62, TextAlignmentOptions.Left, 55);
        FloodLearningUI.Text("Scenario", root.transform, "Strategy A protects 95% of the target district but substantially increases flooding in another populated district. Strategy B protects 88% of the target district and redirects additional water primarily toward an uninhabited open area. All other factors are comparable.", 28, FontStyles.Normal, -105, 145, TextAlignmentOptions.TopLeft, 55);
        FloodLearningUI.Text("Question", root.transform, "Which statement is best supported?", 34, FontStyles.Bold, -275, 65, TextAlignmentOptions.TopLeft, 55);
        for (int i = 0; i < Answers.Length; i++)
        {
            int n = i;
            FloodLearningUI.Button("Answer_" + (char)('A' + i), root.transform, Answers[i], -355 - i * 140, 118, 25,
                () => { UnityEngine.Object.Destroy(root); answer(n); }, 1500);
        }
        FloodLearningUI.Button("Repeat", root.transform, "Repeat scenario", -925, 82, 29,
            () => { UnityEngine.Object.Destroy(root); repeat(); }, 390);
        FloodLearningUI.PlaceInFrontOfPlayer(root.transform, 2.25f);
    }

    public static void ShowConfidence(int answer)
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
            answer == 3 ? $"Correct!  |  Confidence: {confidence}%" : $"Your answer: {(char)('A' + answer)}  |  Confidence: {confidence}%",
            39, FontStyles.Bold, -105, 62, TextAlignmentOptions.Center, 65);
        result.color = answer == 3 ? new Color(.4f, 1f, .55f) : new Color(1f, .68f, .35f);
        FloodLearningUI.Text("Correct", root.transform,
            "Correct answer: D) Strategy B may represent the better system-level solution because it achieves substantial protection while producing less additional exposure elsewhere.",
            30, FontStyles.Bold, -205, 175, TextAlignmentOptions.TopLeft, 70).color = new Color(.4f, 1f, .55f);
        FloodLearningUI.Text("ExplanationTitle", root.transform, "Explanation", 34, FontStyles.Bold, -415, 55, TextAlignmentOptions.Left, 70);
        FloodLearningUI.Text("Explanation", root.transform,
            "Flood-mitigation quality should not be judged only by the originally designated target area. A strategy that provides slightly less local protection may still be preferable if it avoids transferring substantial risk to another vulnerable population.",
            29, FontStyles.Normal, -485, 235, TextAlignmentOptions.TopLeft, 70);
        FloodLearningUI.Button("Finish", root.transform, "Finish", -780, 82, 29,
            () => UnityEngine.Object.Destroy(root), 360);
        Debug.Log($"A1-P6 result: answer={(char)('A' + answer)}, correct={answer == 3}, confidence={confidence}");
    }
}
