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
    [SerializeField] float duration = 25f;
    [SerializeField] float pathwayViewingDuration = 15f;
    PlayMakerFSM tutorial;
    InputAction questA;
    GameObject activeScenario;
    TerrainFloodSimulation simulation;
    float elapsed;
    GameObject continuePrompt;
    GameObject observationHint;
    GameObject waterDirectionHint;
    int selectedAnswer = -1;
    Phase phase;

    enum Phase { WaitingForTutorial, WaitingForA, Running, Question, BuildingDykes, InterventionRunning, Confidence }

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
        if (phase == Phase.BuildingDykes && questA.WasPressedThisFrame()) StartInterventionSimulation();
        if ((phase != Phase.Running && phase != Phase.InterventionRunning) || activeScenario == null) return;
        elapsed += Time.deltaTime;
        float currentDuration = phase == Phase.Running ? pathwayViewingDuration : duration;
        if (elapsed >= currentDuration)
        {
            if (phase == Phase.InterventionRunning) StopInterventionSimulation();
            else StopScenario();
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
        activeScenario = new GameObject("Scenario_A1_P1_PathwayPreview");
        activeScenario.transform.SetParent(parent, false);
        activeScenario.AddComponent<FloodPathwayScenarioA1P1>();
        observationHint = ScenarioGuidanceOverlay.ShowObservation("Look at the pathways");
        simulation = null;
        elapsed = 0;
        phase = Phase.Running;
    }

    void StopScenario()
    {
        simulation?.StopSimulation();
        // The pathways remain visible behind the question.
        if (observationHint != null) Destroy(observationHint);
        ClearDykeManagerChildren();
        phase = Phase.Question;
        FloodScenarioQuizPanel.Show(RepeatScenario, BeginDykeBuilding);
    }

    void RepeatScenario()
    {
        if (phase != Phase.Question) return;
        StartPathwayPreview();
    }

    void BeginDykeBuilding(int answer)
    {
        selectedAnswer = answer;
        if (activeScenario != null) Destroy(activeScenario);
        activeScenario = null;
        SetDykeConstruction(true, false);
        SetTutorialDykeBuildingGuide(false);
        continuePrompt = FloodScenarioContinuePrompt.ShowDykeBuilding();
        waterDirectionHint = ScenarioGuidanceOverlay.ShowWaterFromLeft();
        phase = Phase.BuildingDykes;
    }

    void StartInterventionSimulation()
    {
        if (floodPrefab == null) { Debug.LogError("Flood scenario prefab is missing.", this); return; }
        HideA1P1Buildings();
        if (continuePrompt != null) Destroy(continuePrompt);
        if (waterDirectionHint != null) Destroy(waterDirectionHint);
        SetDykeConstruction(false, false);
        Transform parent = Find("FakeFloodEnvironment")?.transform;
        activeScenario = Instantiate(floodPrefab, parent, false);
        activeScenario.name = "Scenario_A1_P1_WithParticipantDykes";
        simulation = activeScenario.GetComponent<TerrainFloodSimulation>();
        if (simulation == null) { Debug.LogError("TerrainFloodSimulation is missing on the flood prefab.", this); return; }
        simulation.Begin();
        elapsed = 0;
        phase = Phase.InterventionRunning;
    }

    void StopInterventionSimulation()
    {
        simulation?.StopSimulation();
        if (activeScenario != null) Destroy(activeScenario);
        activeScenario = null;
        ClearDykeManagerChildren();
        SetA1P1Houses(false);
        phase = Phase.Confidence;
        FloodScenarioQuizPanel.ShowConfidence(selectedAnswer, StartA1P2);
    }

    void StartA1P2()
    {
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
        }
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            Transform[] objects = root.GetComponentsInChildren<Transform>(true);
            foreach (Transform item in objects)
            {
                string lowerName = item.name.ToLowerInvariant();
                // Snap points belong only to the original tutorial. A1-P1 uses free placement.
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
        SetA1P1Houses(false);
        questA?.Disable(); questA?.Dispose();
        if (continuePrompt != null) Destroy(continuePrompt);
        if (observationHint != null) Destroy(observationHint);
        if (waterDirectionHint != null) Destroy(waterDirectionHint);
    }
}
