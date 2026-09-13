using UnityEngine;

/// <summary>Starts a selected learning scenario directly when Play Mode begins.</summary>
[DefaultExecutionOrder(100)]
public sealed class FloodScenarioDebugLauncher : MonoBehaviour
{
    public enum Scenario
    {
        A1_P1_FloodwayPathIdentification,
        A1_P2_Topography,
        A1_P3_BarrierContinuityAndBypassing
    }

    [Header("Editor Test Start")]
    [SerializeField] bool startSelectedScenarioOnPlay = true;
    [SerializeField] Scenario selectedScenario = Scenario.A1_P1_FloodwayPathIdentification;

    [Header("Required")]
    [Tooltip("Assign the same flood prefab used by PlayMakerTerrainFloodScenario03.")]
    [SerializeField] GameObject floodPrefab;

    [Header("Test Durations")]
    [Min(1f)] [SerializeField] float previewDuration = 15f;
    [Min(1f)] [SerializeField] float floodDuration = 25f;

    void Start()
    {
        if (startSelectedScenarioOnPlay) StartSelectedScenario();
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

        switch (selectedScenario)
        {
            case Scenario.A1_P1_FloodwayPathIdentification:
            {
                PlayMakerTerrainFloodScenario03 flow = FindFirstObjectByType<PlayMakerTerrainFloodScenario03>();
                if (flow == null) flow = gameObject.AddComponent<PlayMakerTerrainFloodScenario03>();
                flow.BeginDebug(floodPrefab, previewDuration, floodDuration);
                break;
            }
            case Scenario.A1_P2_Topography:
            {
                A1P2ScenarioFlow flow = FindFirstObjectByType<A1P2ScenarioFlow>();
                if (flow == null) flow = gameObject.AddComponent<A1P2ScenarioFlow>();
                flow.BeginDebug(floodPrefab, previewDuration, floodDuration);
                break;
            }
            case Scenario.A1_P3_BarrierContinuityAndBypassing:
            {
                A1P3ScenarioFlow flow = FindFirstObjectByType<A1P3ScenarioFlow>();
                if (flow == null) flow = gameObject.AddComponent<A1P3ScenarioFlow>();
                flow.BeginDebug(floodPrefab, previewDuration, floodDuration);
                break;
            }
        }

        Debug.Log($"Debug start: {selectedScenario}", this);
    }
}
