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
    [Header("Terrain fitting")]
    [Tooltip("Minimum burial depth for the lowest points/feet of a constructed dyke.")]
    [Min(0f)] [SerializeField] float footEmbedDepth = 0.03f;
    [SerializeField] GameObject dykeToDestroy;

    [Header("Scenario placement")]
    public bool freePlacement;
    [Tooltip("Maximum number of free-placement dykes. Zero means unlimited.")]
    [Min(0)] public int maxFreePlacementDykes;
    [Tooltip("Minimum length of one free-placement dyke in metres. Zero disables the minimum.")]
    [Min(0f)] public float minFreePlacementDykeLengthMeters;
    [Tooltip("Maximum length of one free-placement dyke in metres. Zero means unlimited.")]
    [Min(0f)] public float maxFreePlacementDykeLengthMeters;
    [Tooltip("Maximum combined length of all free-placement dykes in metres. Zero means unlimited.")]
    [Min(0f)] public float maxTotalFreePlacementDykeLengthMeters;

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
    Transform placementRiver;
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

    /// <summary>Configures the active scenario's free-placement limits.</summary>
    public void ConfigureFreePlacementLimits(int maxDykes, float minLengthMeters,
        float maxLengthMeters, float maxTotalLengthMeters)
    {
        maxFreePlacementDykes = Mathf.Max(0, maxDykes);
        minFreePlacementDykeLengthMeters = Mathf.Max(0f, minLengthMeters);
        maxFreePlacementDykeLengthMeters = Mathf.Max(0f, maxLengthMeters);
        maxTotalFreePlacementDykeLengthMeters = Mathf.Max(0f, maxTotalLengthMeters);
    }

    /// <summary>Clears scenario-specific placement limits.</summary>
    public void ClearFreePlacementLimits()
    {
        maxFreePlacementDykes = 0;
        minFreePlacementDykeLengthMeters = 0f;
        maxFreePlacementDykeLengthMeters = 0f;
        maxTotalFreePlacementDykeLengthMeters = 0f;
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
        if (!freePlacement || !TryGetPlacementTerrainHit(out RaycastHit hit))
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

        // The river surface can be inside the terrain prefab hierarchy. It is
        // a visual water plane, not a valid dyke foundation, so reject it
        // before accepting the broader terrain hierarchy test below.
        if (placementRiver == null)
        {
            foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                if (child.name == "SM_Tutorial_River_01") { placementRiver = child; break; }
        }
        if (placementRiver != null &&
            (hitTransform == placementRiver || hitTransform.IsChildOf(placementRiver)))
            return false;

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

    bool TryGetPlacementTerrainHit(out RaycastHit terrainHit)
    {
        terrainHit = default;
        if (rightXRRayInteractor == null || rightXRRayInteractor.rayOriginTransform == null) return false;

        if (!freePlacement)
            return rightXRRayInteractor.TryGetCurrent3DRaycastHit(out terrainHit);

        Transform origin = rightXRRayInteractor.rayOriginTransform;
        RaycastHit[] hits = Physics.RaycastAll(origin.position, origin.forward, 100f, ~0, QueryTriggerInteraction.Ignore);
        float closestDistance = float.MaxValue;
        bool found = false;
        foreach (RaycastHit hit in hits)
        {
            if (hit.distance >= closestDistance || !IsTerrainHit(hit)) continue;
            closestDistance = hit.distance;
            terrainHit = hit;
            found = true;
        }
        return found;
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
                if (TryGetPlacementTerrainHit(out RaycastHit raycastHit))
                {
                    if (freePlacement || snapPoints.Contains(raycastHit.collider))
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
                if (TryGetPlacementTerrainHit(out RaycastHit raycastHit))
                {
                    bool validEndPoint = (freePlacement) ||
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

        if (TryGetPlacementTerrainHit(out RaycastHit raycastHit))
        {
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
            if (futureDyke != null)
            {
                // PolygonGenerator creates the preview mesh in the X/Z plane
                // at world Y=0. Move its base onto the terrain hit height so
                // the yellow/green hover effect is not left on the lower river
                // plane. Use the lower endpoint so the preview never floats
                // above either terrain contact.
                Vector3 previewPosition = futureDyke.transform.position;
                previewPosition.y = Mathf.Min(startPoint.y, _endPoint.y) + 0.005f;
                futureDyke.transform.position = previewPosition;
            }
        }
    }

    void DrawNewDyke()
    {
        Vector3 fullDirection = endPoint - startPoint;
        Vector3 horizontalDirection = new Vector3(fullDirection.x, 0f, fullDirection.z);
        float horizontalDistance = horizontalDirection.magnitude;
        if (horizontalDistance < 0.0001f)
        {
            return;
        }

        int currentDykeCount = GetComponentsInChildren<A1P1ScenarioDyke>(true).Length;
        if (freePlacement && maxFreePlacementDykes > 0 && currentDykeCount >= maxFreePlacementDykes)
            return;
        if (freePlacement && minFreePlacementDykeLengthMeters > 0f &&
            horizontalDistance < minFreePlacementDykeLengthMeters)
            return;
        if (freePlacement && maxFreePlacementDykeLengthMeters > 0f &&
            horizontalDistance > maxFreePlacementDykeLengthMeters)
            return;
        if (freePlacement && maxTotalFreePlacementDykeLengthMeters > 0f &&
            CurrentFreePlacementLengthMeters() + horizontalDistance > maxTotalFreePlacementDykeLengthMeters)
            return;

        GameObject dyke = Instantiate(dykePrefab, transform);
        dyke.transform.position = (startPoint + endPoint) / 2;

        // Follow the terrain slope, but do not count the height difference as
        // additional dyke length. The previous Vector3.Distance() made a dyke
        // appear oversized when its endpoints were at different elevations.
        Quaternion quaternion = Quaternion.LookRotation(fullDirection.normalized, Vector3.up);
        dyke.transform.rotation = quaternion;

        dyke.transform.localScale = new Vector3(
            dyke.transform.localScale.x * scaleMultiplier,
            dyke.transform.localScale.y * scaleMultiplier,
            horizontalDistance / 36f);
        BuryDykeFeet(dyke, Mathf.Min(startPoint.y, endPoint.y) - footEmbedDepth);

        if (freePlacement)
        {
            AddMeshColliders(dyke);
            dyke.AddComponent<A1P1ScenarioDyke>();
            ScenarioDykeLength length = dyke.AddComponent<ScenarioDykeLength>();
            length.lengthMeters = horizontalDistance;
        }

        // AddInteraction(dyke);

        dykeBuilt++;
    }

    float CurrentFreePlacementLengthMeters()
    {
        float total = 0f;
        foreach (ScenarioDykeLength length in GetComponentsInChildren<ScenarioDykeLength>(true))
            if (length != null) total += length.lengthMeters;
        return total;
    }

    void BuryDykeFeet(GameObject dyke, float targetLowestWorldY)
    {
        if (dyke == null) return;
        Renderer[] renderers = dyke.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) return;

        float lowestWorldY = float.PositiveInfinity;
        foreach (Renderer renderer in renderers)
        {
            if (renderer != null) lowestWorldY = Mathf.Min(lowestWorldY, renderer.bounds.min.y);
        }
        if (float.IsPositiveInfinity(lowestWorldY)) return;

        // Only move down. If the prefab already reaches below the terrain, do
        // not lift it and expose the feet again.
        if (lowestWorldY > targetLowestWorldY)
            dyke.transform.position += Vector3.up * (targetLowestWorldY - lowestWorldY);
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

/// <summary>Stores the measured horizontal length of a scenario dyke.</summary>
sealed class ScenarioDykeLength : MonoBehaviour
{
    public float lengthMeters;
}
