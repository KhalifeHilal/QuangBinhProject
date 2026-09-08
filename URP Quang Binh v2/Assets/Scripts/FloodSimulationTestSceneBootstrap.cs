using UnityEngine;

public sealed class FloodSimulationTestSceneBootstrap : MonoBehaviour
{
    [SerializeField] GameObject floodPrefab;

    void Start()
    {
        CreateCameraAndLight();

        GameObject terrain = GameObject.CreatePrimitive(PrimitiveType.Plane);
        terrain.name = "SM_Tutorial_Terrain_01";
        terrain.transform.position = Vector3.zero;
        terrain.transform.localScale = new Vector3(1.2f, 1f, .7f);
        SetColor(terrain, new Color(.28f, .48f, .2f));

        GameObject river = GameObject.CreatePrimitive(PrimitiveType.Cube);
        river.name = "SM_Tutorial_River_01";
        river.transform.position = new Vector3(0f, .018f, 0f);
        river.transform.localScale = new Vector3(11f, .025f, 1.15f);
        SetColor(river, new Color(.2f, .28f, .22f));
        Collider riverCollider = river.GetComponent<Collider>();
        if (riverCollider != null) riverCollider.enabled = false;

        if (floodPrefab == null)
        {
            Debug.LogError("Assign FakeFlood_03_TerrainGPU to the test-scene bootstrap.", this);
            return;
        }
        GameObject flood = Instantiate(floodPrefab);
        flood.name = "Left_To_Right_River_Flood_Test";
        TerrainFloodSimulation simulation = flood.GetComponent<TerrainFloodSimulation>();
        simulation.simulationYaw = 0f;
        simulation.referenceTerrainName = terrain.name;
        simulation.referenceRiverName = river.name;
        simulation.riverDepth = .045f;
        simulation.riverInflow = 1.4f;
        simulation.maximumWater = .5f;
        simulation.timeScale = .6f;
        simulation.rainRate = 0f;
        simulation.evaporation = .002f;
        simulation.visibleLeftStartReservoir = true;
        simulation.waterColor = new Color(.01f, .32f, 1f, .92f);
        simulation.Begin();
    }

    static void CreateCameraAndLight()
    {
        GameObject cameraObject = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
        cameraObject.tag = "MainCamera";
        cameraObject.transform.position = new Vector3(0f, 8f, -8.5f);
        cameraObject.transform.rotation = Quaternion.Euler(38f, 0f, 0f);

        GameObject lightObject = new GameObject("Directional Light", typeof(Light));
        Light light = lightObject.GetComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.2f;
        lightObject.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
    }

    static void SetColor(GameObject target, Color color)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        target.GetComponent<Renderer>().material = new Material(shader) { color = color };
    }
}
