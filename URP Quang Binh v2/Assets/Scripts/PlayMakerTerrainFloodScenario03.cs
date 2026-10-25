using HutongGames.PlayMaker;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

public sealed class PlayMakerTerrainFloodScenario03 : MonoBehaviour
{
    [SerializeField] GameObject floodPrefab;
    [Header("A1-P1 scenario houses")]
    [UnityEngine.Tooltip("Optional direct references. If left empty, the objects are found by the exact names HOUSE_A ... HOUSE_D.")]
    [SerializeField] GameObject houseA;
    [SerializeField] GameObject houseB;
    [SerializeField] GameObject houseC;
    [SerializeField] GameObject houseD;
    [SerializeField] float duration = 20f;
    [SerializeField] float pathwayViewingDuration = 15f;
    PlayMakerFSM tutorial;
    InputAction questA;
    GameObject activeScenario;
    GameObject activeFlood;
    TerrainFloodSimulation simulation;
    float elapsed;
    GameObject continuePrompt;
    GameObject observationHint;
    GameObject waterDirectionHint;
    int selectedAnswer = -1;
    Phase phase;

    enum Phase { WaitingForTutorial, WaitingForA, Preview, Question, WaitingForSimulationA, Running, Confidence }

    void Awake()
    {
        tutorial = GetComponent<PlayMakerFSM>();
        questA = new InputAction("Start Flood Scenario 03", InputActionType.Button,
            "<XRController>{RightHand}/primaryButton");
        questA.Enable();

        // These FBX instances are scenario-only content. Keep them hidden while
        // the tutorial is running and until the participant starts A1-P1.
        SetA1P1Houses(false);
    }

    /// <summary>Starts A1-P1 directly without waiting for the PlayMaker tutorial.</summary>
    public void BeginDebug(GameObject prefab, float previewSeconds, float simulationSeconds)
    {
        FloodStudyLogger.BeginScenario("A1-P1", "Floodway Path Identification", previewSeconds);
        floodPrefab = prefab;
        pathwayViewingDuration = previewSeconds;
        duration = simulationSeconds;
        Hide("StartGame"); Hide("TutorialAnimation_01"); Hide("TutorialAnimation_02");
        ClearDykeManagerChildren();
        StartPathwayPreview();
    }

    /// <summary>Arms A1-P1 after the tutorial has completed, without overlapping it.</summary>
    public void BeginAfterTutorial(GameObject prefab, float previewSeconds, float simulationSeconds)
    {
        FloodStudyLogger.BeginScenario("A1-P1", "Floodway Path Identification", previewSeconds);
        floodPrefab = prefab;
        pathwayViewingDuration = previewSeconds;
        duration = simulationSeconds;
        Hide("StartGame"); Hide("TutorialAnimation_01"); Hide("TutorialAnimation_02");
        ClearDykeManagerChildren();
        if (continuePrompt != null) Destroy(continuePrompt);
        continuePrompt = FloodScenarioContinuePrompt.Show();
        phase = Phase.WaitingForA;
    }

    void Update()
    {
        if (phase == Phase.WaitingForTutorial && tutorial != null && tutorial.Fsm.ActiveStateName == "Etat 12")
        {
            Hide("StartGame"); Hide("TutorialAnimation_01"); Hide("TutorialAnimation_02");
            ClearDykeManagerChildren();
            continuePrompt = FloodScenarioContinuePrompt.Show();
            phase = Phase.WaitingForA;
            Debug.Log("A1-P1 ready. Press the right Quest A button to continue.");
        }
        if (phase == Phase.WaitingForA && questA.WasPressedThisFrame()) StartPathwayPreview();
        if (phase == Phase.WaitingForSimulationA && questA.WasPressedThisFrame()) StartSimulation();
        if (phase != Phase.Preview && phase != Phase.Running) return;
        elapsed += Time.deltaTime;
        float currentDuration = phase == Phase.Preview ? pathwayViewingDuration : duration;
        if (elapsed >= currentDuration)
        {
            if (phase == Phase.Preview) StopScenario();
            else FinishSimulation();
        }
    }

    void StartPathwayPreview()
    {
        TutorialTerrainTopography.ActivateAfterTutorial();
        HideA1P1Buildings();
        SetA1P1Houses(true);
        SetDykeConstruction(false, true);
        Transform parent = Find("FakeFloodEnvironment")?.transform;
        if (continuePrompt != null) Destroy(continuePrompt);
        if (activeScenario != null) Destroy(activeScenario);
        if (activeFlood != null) Destroy(activeFlood);
        activeFlood = null;
        simulation = null;
        activeScenario = new GameObject("Scenario_A1_P1_PathwayPreview");
        activeScenario.transform.SetParent(parent, false);
        activeScenario.AddComponent<FloodPathwayScenarioA1P1>();
        observationHint = ScenarioGuidanceOverlay.ShowObservation("Look at the pathways", pathwayViewingDuration);
        FloodStudyLogger.BeginObservation();
        elapsed = 0;
        phase = Phase.Preview;
    }

    void StopScenario()
    {
        // The pathways remain visible behind the question.
        if (observationHint != null) Destroy(observationHint);
        FloodStudyLogger.EndObservation();
        ClearDykeManagerChildren();
        phase = Phase.Question;
        FloodScenarioQuizPanel.Show(RepeatScenario, SubmitAnswerWithoutDykes);
    }

    void RepeatScenario()
    {
        if (phase != Phase.Question) return;
        StartPathwayPreview();
    }

    void SubmitAnswerWithoutDykes(int answer)
    {
        selectedAnswer = answer;
        FloodStudyLogger.RecordAnswer(answer);
        if (activeScenario != null) Destroy(activeScenario);
        activeScenario = null;
        ClearDykeManagerChildren();
        SetDykeConstruction(false, true);
        if (continuePrompt != null) Destroy(continuePrompt);
        continuePrompt = FloodScenarioContinuePrompt.ShowSimulationStart();
        phase = Phase.WaitingForSimulationA;
    }

    void StartSimulation()
    {
        if (floodPrefab == null)
        {
            Debug.LogError("A1-P1 flood prefab is missing.", this);
            return;
        }

        if (continuePrompt != null) Destroy(continuePrompt);
        SetDykeConstruction(false, true);
        Transform parent = Find("FakeFloodEnvironment")?.transform;
        activeFlood = Instantiate(floodPrefab, parent, false);
        activeFlood.name = "Scenario_A1_P1_FlowOnly";
        simulation = activeFlood.GetComponent<TerrainFloodSimulation>();
        if (simulation == null)
        {
            Debug.LogError("TerrainFloodSimulation is missing on the A1-P1 flood prefab.", this);
            Destroy(activeFlood);
            activeFlood = null;
            return;
        }

        simulation.Begin();
        elapsed = 0;
        FloodStudyLogger.BeginSimulation();
        phase = Phase.Running;
    }

    void FinishSimulation()
    {
        FloodStudyLogger.EndSimulation(simulation);
        simulation?.StopSimulation();
        if (activeFlood != null) Destroy(activeFlood);
        activeFlood = null;
        simulation = null;
        ClearDykeManagerChildren();
        SetA1P1Houses(false);
        phase = Phase.Confidence;
        FloodScenarioQuizPanel.ShowConfidence(selectedAnswer, StartA1P2);
    }

    void StartA1P2()
    {
        FloodStudyLogger.EndScenario();
        A1P2ScenarioFlow flow = gameObject.GetComponent<A1P2ScenarioFlow>();
        if (flow == null) flow = gameObject.AddComponent<A1P2ScenarioFlow>();
        flow.Begin(floodPrefab, pathwayViewingDuration, duration);
    }

    static void SetDykeConstruction(bool enabled, bool hideOldDykes)
    {
        DykeManagerTutorial manager = Object.FindFirstObjectByType<DykeManagerTutorial>(FindObjectsInactive.Include);
        if (manager != null)
        {
            manager.gameObject.SetActive(true);
            manager.enabled = enabled;
            manager.freePlacement = enabled;
            manager.ClearFreePlacementLimits();
        }
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            Transform[] objects = root.GetComponentsInChildren<Transform>(true);
            foreach (Transform item in objects)
            {
                string lowerName = item.name.ToLowerInvariant();
                // Snap points and tutorial dykes belong only to the original tutorial.
                if (lowerName.Contains("snapping_point")) item.gameObject.SetActive(false);
                if (hideOldDykes && (lowerName.StartsWith("dikeblock") || lowerName.StartsWith("sm_tempdyke")))
                    Destroy(item.gameObject);
            }
        }
    }

    static void ClearDykeManagerChildren()
    {
        DykeManagerTutorial manager = Object.FindFirstObjectByType<DykeManagerTutorial>(FindObjectsInactive.Include);
        if (manager == null) return;
        manager.enabled = false;
        manager.freePlacement = false;
        manager.ClearFreePlacementLimits();
        for (int i = manager.transform.childCount - 1; i >= 0; i--)
            Destroy(manager.transform.GetChild(i).gameObject);
    }

    static void HideA1P1Buildings()
    {
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            Transform[] objects = root.GetComponentsInChildren<Transform>(true);
            foreach (Transform item in objects)
            {
                string lowerName = item.name.ToLowerInvariant();
                if (lowerName.Contains("sm_building_big_001") ||
                    lowerName.Contains("asm_building_big_001") ||
                    lowerName.Contains("sm_buildung_03") ||
                    lowerName.Contains("sm_building_03"))
                    item.gameObject.SetActive(false);
            }
        }
    }

    /// <summary>
    /// Shows or hides the four FBX house instances used only by A1-P1. Direct
    /// inspector references are preferred, but name lookup also works for
    /// inactive objects and nested FBX prefab instances.
    /// </summary>
    void SetA1P1Houses(bool visible)
    {
        GameObject[] assigned = { houseA, houseB, houseC, houseD };
        string[] names = { "HOUSE_A", "HOUSE_B", "HOUSE_C", "HOUSE_D" };
        var touched = new System.Collections.Generic.HashSet<int>();

        for (int i = 0; i < names.Length; i++)
        {
            GameObject target = assigned[i];
            if (target != null)
            {
                target.SetActive(visible);
                touched.Add(target.GetInstanceID());
            }

            // Find all exact matches. This also catches inactive FBX instances,
            // which Unity's ordinary Find methods intentionally skip.
            foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                foreach (Transform item in root.GetComponentsInChildren<Transform>(true))
                {
                    if (!string.Equals(item.name, names[i], System.StringComparison.OrdinalIgnoreCase)) continue;
                    if (touched.Add(item.gameObject.GetInstanceID())) item.gameObject.SetActive(visible);
                }
            }
        }
    }

    static void SetTutorialDykeBuildingGuide(bool visible)
    {
        GameObject guide = Find("DykeBuilding Phase");
        if (guide == null) return;
        guide.SetActive(visible);
        if (!visible) return;
        foreach (Animator animator in guide.GetComponentsInChildren<Animator>(true))
        {
            animator.Rebind();
            animator.Update(0f);
            animator.enabled = true;
        }
    }

    static void Hide(string objectName) { GameObject found = Find(objectName); if (found != null) found.SetActive(false); }
    static GameObject Find(string objectName)
    {
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            if (child.name == objectName) return child.gameObject;
        return null;
    }

    void OnDestroy()
    {
        FloodStudyLogger.EndScenario();
        SetA1P1Houses(false);
        questA?.Disable(); questA?.Dispose();
        if (continuePrompt != null) Destroy(continuePrompt);
        if (observationHint != null) Destroy(observationHint);
        if (waterDirectionHint != null) Destroy(waterDirectionHint);
        if (activeScenario != null) Destroy(activeScenario);
        if (activeFlood != null) Destroy(activeFlood);
    }
}
