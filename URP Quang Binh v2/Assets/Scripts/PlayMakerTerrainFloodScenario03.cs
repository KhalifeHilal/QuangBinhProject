using HutongGames.PlayMaker;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

public sealed class PlayMakerTerrainFloodScenario03 : MonoBehaviour
{
    [SerializeField] GameObject floodPrefab;
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
        HideA1P1Buildings();
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
        questA?.Disable(); questA?.Dispose();
        if (continuePrompt != null) Destroy(continuePrompt);
        if (observationHint != null) Destroy(observationHint);
        if (waterDirectionHint != null) Destroy(waterDirectionHint);
    }
}
