using HutongGames.PlayMaker;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

public sealed class PlayMakerTerrainFloodScenario03 : MonoBehaviour
{
    [SerializeField] GameObject floodPrefab;
    [SerializeField] float duration = 25f;
    PlayMakerFSM tutorial;
    InputAction questA;
    GameObject activeScenario;
    TerrainFloodSimulation simulation;
    float elapsed;
    bool ready;

    void Awake()
    {
        tutorial = GetComponent<PlayMakerFSM>();
        questA = new InputAction("Start Flood Scenario 03", InputActionType.Button,
            "<XRController>{RightHand}/primaryButton");
        questA.Enable();
    }

    void Update()
    {
        if (!ready && tutorial != null && tutorial.Fsm.ActiveStateName == "Etat 12")
        {
            Hide("StartGame"); Hide("TutorialAnimation_01"); Hide("TutorialAnimation_02");
            ready = true;
            Debug.Log("Flood scenario 03 ready. Press the right Quest A button.");
        }
        if (ready && activeScenario == null && questA.WasPressedThisFrame()) StartScenario();
        if (activeScenario == null) return;
        elapsed += Time.deltaTime;
        if (elapsed >= duration) StopScenario();
    }

    void StartScenario()
    {
        if (floodPrefab == null) { Debug.LogError("Flood scenario prefab is missing.", this); return; }
        Transform parent = Find("FakeFloodEnvironment")?.transform;
        activeScenario = Instantiate(floodPrefab, parent, false);
        simulation = activeScenario.GetComponent<TerrainFloodSimulation>();
        simulation.Begin();
        elapsed = 0;
    }

    void StopScenario()
    {
        simulation?.StopSimulation();
        if (activeScenario != null) Destroy(activeScenario);
        activeScenario = null;
    }

    static void Hide(string objectName) { GameObject found = Find(objectName); if (found != null) found.SetActive(false); }
    static GameObject Find(string objectName)
    {
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            if (child.name == objectName) return child.gameObject;
        return null;
    }

    void OnDestroy() { questA?.Disable(); questA?.Dispose(); }
}
