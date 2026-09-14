using HutongGames.PlayMaker;
using UnityEngine;

/// <summary>Runs the PlayMaker tutorial first, then starts the selected learning scenario.</summary>
[DefaultExecutionOrder(100)]
public sealed class FloodScenarioDebugLauncher : MonoBehaviour
{
    public enum Scenario
    {
        A1_P1_FloodwayPathIdentification,
        A1_P2_Topography,
        A1_P3_BarrierContinuityAndBypassing,
        A1_P4_WaterRedistribution
    }

    [Header("Editor Test Start")]
    [SerializeField] bool startSelectedScenarioOnPlay = true;
    [SerializeField] Scenario selectedScenario = Scenario.A1_P1_FloodwayPathIdentification;

    [Header("Required")]
    [UnityEngine.Tooltip("Assign the same flood prefab used by PlayMakerTerrainFloodScenario03.")]
    [SerializeField] GameObject floodPrefab;

    [Header("Test Durations")]
    [Min(1f)] [SerializeField] float previewDuration = 15f;
    [Min(1f)] [SerializeField] float floodDuration = 25f;

    [Header("Tutorial gate")]
    [SerializeField] string tutorialObjectName = "Tutorial_Manager";
    [SerializeField] string tutorialFinishedState = "Etat 12";

    bool waitingForTutorial;
    bool scenarioStarted;
    PlayMakerTerrainFloodScenario03 defaultP1Flow;

    void Start()
    {
        if (startSelectedScenarioOnPlay) StartSelectedScenario();
    }

    void Update()
    {
        if (!waitingForTutorial || scenarioStarted) return;
        if (TutorialHasFinished()) StartPendingScenario();
    }

    [ContextMenu("Start Selected Scenario")]
    public void StartSelectedScenario()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("Enter Play Mode to start the selected scenario.", this);
            return;
        }
        if (floodPrefab == null)
        {
            Debug.LogError("Assign the flood prefab in Flood Scenario Debug Launcher.", this);
            return;
        }

        if (waitingForTutorial || scenarioStarted) return;
        waitingForTutorial = true;
        // The scene already contains the normal A1-P1 PlayMaker component. It
        // must be paused while the selected debug scenario waits for the tutorial,
        // otherwise both flows would spawn their panels at the same time.
        defaultP1Flow = FindFirstObjectByType<PlayMakerTerrainFloodScenario03>();
        if (defaultP1Flow != null) defaultP1Flow.enabled = false;

        if (TutorialHasFinished()) StartPendingScenario();
        else Debug.Log("Debug launcher is waiting for the PlayMaker tutorial to reach " + tutorialFinishedState + ".", this);
    }

    bool TutorialHasFinished()
    {
        GameObject tutorialObject = FindSceneObject(tutorialObjectName);
        if (tutorialObject == null) return true;
        PlayMakerFSM fsm = tutorialObject.GetComponent<PlayMakerFSM>();
        return fsm == null || fsm.Fsm == null || fsm.Fsm.ActiveStateName == tutorialFinishedState;
    }

    void StartPendingScenario()
    {
        waitingForTutorial = false;
        scenarioStarted = true;

        switch (selectedScenario)
        {
            case Scenario.A1_P1_FloodwayPathIdentification:
            {
                PlayMakerTerrainFloodScenario03 flow = defaultP1Flow;
                if (flow == null) flow = FindFirstObjectByType<PlayMakerTerrainFloodScenario03>();
                if (flow == null) flow = gameObject.AddComponent<PlayMakerTerrainFloodScenario03>();
                flow.enabled = true;
                flow.BeginAfterTutorial(floodPrefab, previewDuration, floodDuration);
                break;
            }
            case Scenario.A1_P2_Topography:
            {
                A1P2ScenarioFlow flow = FindFirstObjectByType<A1P2ScenarioFlow>();
                if (flow == null) flow = gameObject.AddComponent<A1P2ScenarioFlow>();
                flow.Begin(floodPrefab, previewDuration, floodDuration);
                break;
            }
            case Scenario.A1_P3_BarrierContinuityAndBypassing:
            {
                A1P3ScenarioFlow flow = FindFirstObjectByType<A1P3ScenarioFlow>();
                if (flow == null) flow = gameObject.AddComponent<A1P3ScenarioFlow>();
                flow.Begin(floodPrefab, previewDuration, floodDuration);
                break;
            }
            case Scenario.A1_P4_WaterRedistribution:
            {
                A1P4ScenarioFlow flow = FindFirstObjectByType<A1P4ScenarioFlow>();
                if (flow == null) flow = gameObject.AddComponent<A1P4ScenarioFlow>();
                flow.Begin(floodPrefab, previewDuration, floodDuration);
                break;
            }
        }

        Debug.Log($"Debug start: {selectedScenario}", this);
    }

    static GameObject FindSceneObject(string objectName)
    {
        foreach (GameObject root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            if (child.name == objectName) return child.gameObject;
        return null;
    }
}
