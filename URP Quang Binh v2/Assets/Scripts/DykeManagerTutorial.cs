using System.Collections.Generic;
using System.Linq;
using HutongGames.PlayMaker.ActionsInternal;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

public class DykeManagerTutorial : MonoBehaviour
{
    [Header("Input Actions")]
    [SerializeField] InputActionReference primaryRightHandButton = null;
    [SerializeField] InputActionReference rightHandTriggerButton = null;
    [SerializeField] XRRayInteractor rightXRRayInteractor;
    [SerializeField] Collider[] snapPoints;
    [SerializeField] GameObject dykePrefab;
    [SerializeField] Material selectedMaterial;
    [SerializeField] float perMultiplier = 0.1f;
    [SerializeField] int heightDivision = 10;
    [SerializeField] float scaleMultiplier = 0.1f;
    [SerializeField] GameObject dykeToDestroy;

    [Header("Scenario placement")]
    public bool freePlacement;

    [Header("For PlayMaker")]
    public int dykeBuilt = 0;
    public int dykeDestroyed = 0;

    bool inTriggerPress, displayFutureDike;
    Vector3 startPoint, endPoint;
    Collider startCollider;
    GameObject futureDyke;
    PropertiesGAMA propFutureDike;
    PolygonGenerator polyGen = null;
    Dictionary<GameObject, Material> selectedHoveringDykes;
    InputAction scenarioGrip;
    Transform placementTerrain;
    Renderer placementTerrainRenderer;
    GameObject placementMarker;
    A1P1ScenarioDyke hoveredScenarioDyke;

    void Awake()
    {
        scenarioGrip = new InputAction("Destroy A1-P1 Dyke", InputActionType.Button,
            "<XRController>{RightHand}/gripPressed");
    }

    void OnEnable() => scenarioGrip?.Enable();
    void OnDisable()
    {
        scenarioGrip?.Disable();
        SetHoveredScenarioDyke(null);
        if (placementMarker != null) { Destroy(placementMarker); placementMarker = null; }
    }

    void Start()
    {
        propFutureDike = new PropertiesGAMA
        {
            red = 0,
            blue = 0,
            green = 255,
            hasCollider = false,
            hasPrefab = false,
            height = 1,
            is3D = true,
            visible = true
        };

        selectedHoveringDykes = new Dictionary<GameObject, Material>();

        AddInteraction(dykeToDestroy);
    }

    void Update()
    {
        UpdatePlacementMarker();
        UpdateScenarioDykeHover();
        ProcessFreePlacementGrip();
        ProcessRightHandTrigger();

        if (displayFutureDike)
        {
            GenerateFutureDike();
        }
    }

    void UpdateScenarioDykeHover()
    {
        A1P1ScenarioDyke next = freePlacement ? FindScenarioDykeUnderRay() : null;
        SetHoveredScenarioDyke(next);
    }

    A1P1ScenarioDyke FindScenarioDykeUnderRay()
    {
        if (rightXRRayInteractor == null || rightXRRayInteractor.rayOriginTransform == null) return null;
        Transform origin = rightXRRayInteractor.rayOriginTransform;
        RaycastHit[] hits = Physics.RaycastAll(origin.position, origin.forward, 100f, ~0, QueryTriggerInteraction.Collide);
        A1P1ScenarioDyke closestDyke = null;
        float closestDistance = float.MaxValue;
        foreach (RaycastHit hit in hits)
        {
            A1P1ScenarioDyke dyke = hit.collider.GetComponentInParent<A1P1ScenarioDyke>();
            if (dyke != null && hit.distance < closestDistance) { closestDyke = dyke; closestDistance = hit.distance; }
        }
        return closestDyke;
    }

    void SetHoveredScenarioDyke(A1P1ScenarioDyke next)
    {
        if (hoveredScenarioDyke == next) return;
        if (hoveredScenarioDyke != null) hoveredScenarioDyke.SetHovered(false);
        hoveredScenarioDyke = next;
        if (hoveredScenarioDyke != null) hoveredScenarioDyke.SetHovered(true, selectedMaterial);
    }

    void UpdatePlacementMarker()
    {
        if (!freePlacement || !rightXRRayInteractor.TryGetCurrent3DRaycastHit(out RaycastHit hit) || !IsTerrainHit(hit))
        {
            if (placementMarker != null) placementMarker.SetActive(false);
            return;
        }

        if (placementMarker == null)
        {
            placementMarker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            placementMarker.name = "A1P1_Dyke_Placement_Point";
            placementMarker.transform.localScale = Vector3.one * .12f;
            Collider markerCollider = placementMarker.GetComponent<Collider>();
            if (markerCollider != null) markerCollider.enabled = false;
            Renderer markerRenderer = placementMarker.GetComponent<Renderer>();
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            markerRenderer.material = new Material(shader) { color = new Color(.05f, 1f, .25f, 1f) };
        }

        placementMarker.SetActive(true);
        placementMarker.transform.position = hit.point + hit.normal * .025f;
    }

    bool IsTerrainHit(RaycastHit hit)
    {
        if (placementTerrain == null)
        {
            foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                if (child.name == "SM_Tutorial_Terrain_01")
                {
                    placementTerrain = child;
                    placementTerrainRenderer = child.GetComponentInChildren<Renderer>(true);
                    break;
                }
        }
        if (placementTerrain == null || hit.collider == null) return false;
        Transform hitTransform = hit.collider.transform;
        if (hitTransform == placementTerrain || hitTransform.IsChildOf(placementTerrain) || placementTerrain.IsChildOf(hitTransform))
            return true;

        // Some imported map prefabs keep the ground collider outside the visible
        // SM_Tutorial_Terrain_01 hierarchy. Accept only hits inside the map footprint
        // and close to its ground surface, never elevated building hits.
        if (placementTerrainRenderer == null) return false;
        Bounds bounds = placementTerrainRenderer.bounds;
        float heightTolerance = Mathf.Max(.15f, bounds.size.y * .2f);
        return hit.point.x >= bounds.min.x && hit.point.x <= bounds.max.x &&
               hit.point.z >= bounds.min.z && hit.point.z <= bounds.max.z &&
               hit.point.y <= bounds.max.y + heightTolerance && hit.normal.y > .35f;
    }

    void ProcessFreePlacementGrip()
    {
        if (!freePlacement || scenarioGrip == null || !scenarioGrip.WasPressedThisFrame()) return;
        A1P1ScenarioDyke scenarioDyke = FindScenarioDykeUnderRay();
        if (scenarioDyke != null)
        {
            SetHoveredScenarioDyke(null);
            Destroy(scenarioDyke.gameObject);
        }
    }

    void ProcessRightHandTrigger()
    {
        if (rightHandTriggerButton != null && rightHandTriggerButton.action.triggered)
        {
            if (!inTriggerPress)
            {
                inTriggerPress = true;
                if (rightXRRayInteractor.TryGetCurrent3DRaycastHit(out RaycastHit raycastHit))
                {
                    if ((freePlacement && IsTerrainHit(raycastHit)) || (!freePlacement && snapPoints.Contains(raycastHit.collider)))
                    {
                        startCollider = raycastHit.collider;
                        startPoint = freePlacement ? raycastHit.point : startCollider.transform.position;
                        displayFutureDike = true;
                    }
                }
            }
        }

        if (rightHandTriggerButton != null && !rightHandTriggerButton.action.inProgress)
        {
            displayFutureDike = false;
            if (futureDyke != null)
            {
                DestroyImmediate(futureDyke);
                futureDyke = null;
            }

            if (inTriggerPress)
            {
                inTriggerPress = false;
                if (rightXRRayInteractor.TryGetCurrent3DRaycastHit(out RaycastHit raycastHit))
                {
                    bool validEndPoint = (freePlacement && IsTerrainHit(raycastHit)) ||
                        (!freePlacement && snapPoints.Contains(raycastHit.collider) && raycastHit.collider != startCollider);
                    if (validEndPoint)
                    {
                        endPoint = freePlacement ? raycastHit.point : raycastHit.collider.transform.position;
                        if (Vector3.Distance(startPoint, endPoint) > 0.05f) DrawNewDyke();
                    }
                }
            }
        }
    }

    void GenerateFutureDike()
    {
        if (polyGen == null)
        {
            polyGen = PolygonGenerator.GetInstance();
        }

        if (rightXRRayInteractor.TryGetCurrent3DRaycastHit(out RaycastHit raycastHit))
        {
            if (freePlacement && !IsTerrainHit(raycastHit))
            {
                if (futureDyke != null) { Destroy(futureDyke); futureDyke = null; }
                return;
            }
            if (futureDyke != null)
            {
                DestroyImmediate(futureDyke);
            }

            Vector2[] pts = new Vector2[5];
            Vector3 _endPoint = raycastHit.point;
            Vector2 direction = new Vector2(_endPoint.x - startPoint.x, _endPoint.z - startPoint.z).normalized;
            Vector2 Per = Vector2.Perpendicular(direction);
            Per = new Vector2(Per.x * perMultiplier, Per.y * perMultiplier);

            pts[0] = new Vector2(startPoint.x + Per.x, startPoint.z + Per.y);
            pts[1] = new Vector2(_endPoint.x + Per.x, _endPoint.z + Per.y);
            pts[2] = new Vector2(_endPoint.x - Per.x, _endPoint.z - Per.y);
            pts[3] = new Vector2(startPoint.x - Per.x, startPoint.z - Per.y);
            pts[4] = pts[0];


            futureDyke = polyGen.GeneratePolygons(false, "FutureDyke", pts, propFutureDike, heightDivision);
        }
    }

    void DrawNewDyke()
    {
        GameObject dyke = Instantiate(dykePrefab, transform);
        dyke.transform.position = (startPoint + endPoint) / 2;

        Vector3 direction = endPoint - startPoint;
        direction.y = 0;
        Quaternion quaternion = Quaternion.LookRotation(direction, Vector3.up);
        dyke.transform.rotation = quaternion;

        float distance = Vector3.Distance(startPoint, endPoint);
        dyke.transform.localScale = new Vector3(dyke.transform.localScale.x * scaleMultiplier, dyke.transform.localScale.y * scaleMultiplier, distance / 36);

        if (freePlacement)
        {
            AddMeshColliders(dyke);
            dyke.AddComponent<A1P1ScenarioDyke>();
        }

        // AddInteraction(dyke);

        dykeBuilt++;
    }

    static void AddMeshColliders(GameObject dyke)
    {
        Collider[] existingColliders = dyke.GetComponentsInChildren<Collider>(true);
        foreach (Collider existing in existingColliders)
            if (!(existing is MeshCollider)) Destroy(existing);

        MeshFilter[] meshFilters = dyke.GetComponentsInChildren<MeshFilter>(true);
        foreach (MeshFilter filter in meshFilters)
        {
            if (filter.sharedMesh == null) continue;
            MeshCollider collider = filter.GetComponent<MeshCollider>();
            if (collider == null) collider = filter.gameObject.AddComponent<MeshCollider>();
            collider.sharedMesh = filter.sharedMesh;
            collider.convex = true;
            collider.isTrigger = true;
        }
    }

    void AddInteraction(GameObject dyke)
    {
        dyke.AddComponent<BoxCollider>();
        XRBaseInteractable interaction = dyke.AddComponent<XRSimpleInteractable>();
        interaction.selectEntered.AddListener(SelectInteraction);
        interaction.firstHoverEntered.AddListener(HoverEnterInteraction);
        interaction.hoverExited.AddListener(HoverExitInteraction);
    }

    void HoverExitInteraction(HoverExitEventArgs ev)
    {
        if (ev.interactableObject == null) return;
        GameObject obj = ev.interactableObject.transform.gameObject;
        if (selectedHoveringDykes.ContainsKey(obj))
        {
            obj.GetComponent<MeshRenderer>().material = selectedHoveringDykes[obj];
            selectedHoveringDykes.Remove(obj);
        }
    }

    void HoverEnterInteraction(HoverEnterEventArgs ev)
    {
        if (ev.interactableObject == null) return;
        GameObject obj = ev.interactableObject.transform.gameObject;
        if (!selectedHoveringDykes.ContainsKey(obj))
        {
            selectedHoveringDykes.Add(obj, obj.GetComponent<MeshRenderer>().material);
            obj.GetComponent<MeshRenderer>().material = selectedMaterial;
        }
    }

    void SelectInteraction(SelectEnterEventArgs ev)
    {
        XRSimpleInteractable interaction = ev.interactableObject as XRSimpleInteractable;
        if (interaction != null)
        {
            interaction.selectEntered.RemoveListener(SelectInteraction);
            interaction.firstHoverEntered.RemoveListener(HoverEnterInteraction);
            interaction.hoverExited.RemoveListener(HoverExitInteraction);

            Destroy(interaction.gameObject);

            dykeDestroyed++;
        }
    }

    public void ActivateMainScene()
    {
        SceneManager.LoadScene("Main Scene - ArtUpdate_Flood");
    }

    public void OnTriggerActivate()
    {
        Debug.Log("Trigger Pressed");
    }

    void OnDestroy()
    {
        scenarioGrip?.Disable();
        scenarioGrip?.Dispose();
    }
}
