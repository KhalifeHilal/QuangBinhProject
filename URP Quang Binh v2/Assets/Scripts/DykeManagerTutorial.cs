using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using HutongGames.PlayMaker.ActionsInternal;
using TMPro;
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
    [Tooltip("Snap a free-placement dyke endpoint to the terrain edge when it is this close (metres).")]
    [Min(0f)] [SerializeField] float terrainEdgeSnapDistanceMeters = 0.10f;
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
    DykeLengthLabel futureLengthLabel;

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
        HideFutureLengthLabel();
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
        // Gamepad participants use GamepadDykeBuilder. The legacy ray-based
        // input must stay completely idle in that condition, otherwise both
        // systems would react to the same trigger/grip press.
        if (ParticipantInput.IsGamepadParticipant)
        {
            if (placementMarker != null) placementMarker.SetActive(false);
            HideFutureLengthLabel();
            return;
        }

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
        EnsurePlacementTerrainReference();
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
            TryDestroyFreePlacementDyke(scenarioDyke);
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
                        startPoint = freePlacement
                            ? SnapToTerrainEdge(raycastHit.point)
                            : startCollider.transform.position;
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
            HideFutureLengthLabel();

            if (inTriggerPress)
            {
                inTriggerPress = false;
                if (TryGetPlacementTerrainHit(out RaycastHit raycastHit))
                {
                    bool validEndPoint = (freePlacement) ||
                        (!freePlacement && snapPoints.Contains(raycastHit.collider) && raycastHit.collider != startCollider);
                    if (validEndPoint)
                    {
                        endPoint = freePlacement
                            ? SnapToTerrainEdge(raycastHit.point)
                            : raycastHit.collider.transform.position;
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
            Vector3 _endPoint = SnapToTerrainEdge(raycastHit.point);
            float horizontalDistance = HorizontalDistance(startPoint, _endPoint);
            EnsureFutureLengthLabel();
            futureLengthLabel.SetPreview(startPoint, _endPoint, horizontalDistance);
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
        else
        {
            HideFutureLengthLabel();
        }
    }

    /// <summary>
    /// Returns whether a free-placement dyke with these endpoints satisfies
    /// the active scenario limits. The Gamepad preview uses this for its red
    /// over-length indication before attempting construction.
    /// </summary>
    public bool CanBuildFreePlacementDyke(Vector3 start, Vector3 end)
    {
        if (!freePlacement || dykePrefab == null) return false;

        float horizontalDistance = HorizontalDistance(start, end);
        if (horizontalDistance < 0.05f) return false;

        int currentDykeCount = GetComponentsInChildren<A1P1ScenarioDyke>(true).Length;
        if (maxFreePlacementDykes > 0 && currentDykeCount >= maxFreePlacementDykes)
            return false;
        if (minFreePlacementDykeLengthMeters > 0f && horizontalDistance < minFreePlacementDykeLengthMeters)
            return false;
        if (maxFreePlacementDykeLengthMeters > 0f && horizontalDistance > maxFreePlacementDykeLengthMeters)
            return false;
        if (maxTotalFreePlacementDykeLengthMeters > 0f &&
            CurrentFreePlacementLengthMeters() + horizontalDistance > maxTotalFreePlacementDykeLengthMeters)
            return false;

        return true;
    }

    /// <summary>Builds a participant dyke from the Gamepad endpoints.</summary>
    public bool TryBuildFreePlacementDyke(Vector3 start, Vector3 end)
    {
        if (!freePlacement) return false;
        startPoint = SnapToTerrainEdge(start);
        endPoint = SnapToTerrainEdge(end);
        if (!CanBuildFreePlacementDyke(startPoint, endPoint)) return false;
        return DrawNewDyke();
    }

    /// <summary>
    /// Finds a tutorial snap point close to the Gamepad terrain cursor. The
    /// PlayMaker tutorial still uses its authored snap points; the Gamepad
    /// cursor only replaces the ray used to select them.
    /// </summary>
    public bool TryGetTutorialSnapPoint(Vector3 cursor, float radius, out Vector3 point)
    {
        point = default;
        if (freePlacement || snapPoints == null) return false;

        float closestDistance = Mathf.Max(.01f, radius);
        bool found = false;
        foreach (Collider snapPoint in snapPoints)
        {
            if (snapPoint == null || !snapPoint.gameObject.activeInHierarchy) continue;
            float distance = Vector3.Distance(cursor, snapPoint.transform.position);
            if (distance > closestDistance) continue;
            closestDistance = distance;
            point = snapPoint.transform.position;
            found = true;
        }
        return found;
    }

    /// <summary>Builds a dyke between two PlayMaker tutorial snap points.</summary>
    public bool TryBuildTutorialDyke(Vector3 start, Vector3 end)
    {
        if (freePlacement || !CanBuildTutorialDyke(start, end)) return false;

        startPoint = start;
        endPoint = end;
        int childCountBefore = transform.childCount;
        if (!DrawNewDyke()) return false;

        // Tutorial dykes normally receive their interaction from the XR ray.
        // Give Gamepad-created tutorial dykes the same collider/marker path so
        // the A button can remove them and PlayMaker still sees dykeDestroyed.
        if (transform.childCount > childCountBefore)
        {
            GameObject dyke = transform.GetChild(transform.childCount - 1).gameObject;
            AddMeshColliders(dyke);
            if (dyke.GetComponent<A1P1ScenarioDyke>() == null)
                dyke.AddComponent<A1P1ScenarioDyke>();
        }
        return true;
    }

    public bool CanBuildTutorialDyke(Vector3 start, Vector3 end)
    {
        return !freePlacement && HorizontalDistance(start, end) > .05f;
    }

    /// <summary>Finds a participant dyke close to a terrain cursor point.</summary>
    public A1P1ScenarioDyke FindFreePlacementDykeAt(Vector3 point, float radius)
    {
        A1P1ScenarioDyke closest = null;
        float closestDistance = float.MaxValue;
        float safeRadius = Mathf.Max(0.01f, radius);
        foreach (A1P1ScenarioDyke dyke in GetComponentsInChildren<A1P1ScenarioDyke>(true))
        {
            if (dyke == null || !dyke.gameObject.activeInHierarchy) continue;
            Collider[] colliders = dyke.GetComponentsInChildren<Collider>(true);
            foreach (Collider collider in colliders)
            {
                if (collider == null || !collider.enabled) continue;
                Vector3 closestPoint = collider.ClosestPoint(point);
                float distance = Vector3.Distance(point, closestPoint);
                if (distance <= safeRadius && distance < closestDistance)
                {
                    closest = dyke;
                    closestDistance = distance;
                }
            }
        }
        return closest;
    }

    /// <summary>Returns a tutorial or participant dyke under a Gamepad cursor.</summary>
    public GameObject FindGamepadDykeAt(Vector3 point, float radius)
    {
        A1P1ScenarioDyke participantDyke = FindFreePlacementDykeAt(point, radius);
        if (participantDyke != null) return participantDyke.gameObject;
        return IsNearDyke(dykeToDestroy, point, radius) ? dykeToDestroy : null;
    }

    /// <summary>Destroys a dyke selected by the Gamepad A button.</summary>
    public bool TryDestroyGamepadDyke(GameObject target)
    {
        if (target == null) return false;
        A1P1ScenarioDyke participantDyke = target.GetComponentInParent<A1P1ScenarioDyke>();
        if (participantDyke != null) return TryDestroyFreePlacementDyke(participantDyke);
        if (dykeToDestroy == null ||
            (target != dykeToDestroy && !target.transform.IsChildOf(dykeToDestroy.transform))) return false;

        dykeDestroyed++;
        FloodStudyLogger.RecordDykeDestroyed(target.GetComponentInParent<ScenarioDykeLength>());
        FloodAudio.PlayDykeRemoved();
        Destroy(dykeToDestroy);
        dykeToDestroy = null;
        return true;
    }

    /// <summary>Destroys a participant dyke and records the removal.</summary>
    public bool TryDestroyFreePlacementDyke(A1P1ScenarioDyke scenarioDyke)
    {
        if (scenarioDyke == null) return false;
        SetHoveredScenarioDyke(null);
        FloodStudyLogger.RecordDykeDestroyed(scenarioDyke.GetComponent<ScenarioDykeLength>());
        FloodAudio.PlayDykeRemoved();
        Destroy(scenarioDyke.gameObject);
        dykeDestroyed++;
        return true;
    }

    static bool IsNearDyke(GameObject target, Vector3 point, float radius)
    {
        if (target == null || !target.activeInHierarchy) return false;
        float safeRadius = Mathf.Max(.01f, radius);
        Collider[] colliders = target.GetComponentsInChildren<Collider>(true);
        foreach (Collider collider in colliders)
            if (collider != null && collider.enabled &&
                Vector3.Distance(point, collider.ClosestPoint(point)) <= safeRadius)
                return true;
        Renderer[] renderers = target.GetComponentsInChildren<Renderer>(true);
        foreach (Renderer renderer in renderers)
            if (renderer != null && renderer.bounds.SqrDistance(point) <= safeRadius * safeRadius)
                return true;
        return false;
    }

    bool DrawNewDyke()
    {
        Vector3 fullDirection = endPoint - startPoint;
        Vector3 horizontalDirection = new Vector3(fullDirection.x, 0f, fullDirection.z);
        float horizontalDistance = horizontalDirection.magnitude;
        if (horizontalDistance < 0.0001f)
        {
            return false;
        }

        if (freePlacement && !CanBuildFreePlacementDyke(startPoint, endPoint))
            return false;

        if (dykePrefab == null)
        {
            Debug.LogError("DykeManagerTutorial has no dyke prefab assigned.", this);
            return false;
        }

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
            length.dykeId = FloodStudyLogger.RecordDykeBuilt(startPoint, endPoint, horizontalDistance);
            DykeLengthLabel.Attach(dyke, horizontalDistance);
        }

        // AddInteraction(dyke);

        dykeBuilt++;
        FloodAudio.PlayDykeBuilt();
        return true;
    }

    static float HorizontalDistance(Vector3 start, Vector3 end)
    {
        Vector3 delta = end - start;
        delta.y = 0f;
        return delta.magnitude;
    }

    Vector3 SnapToTerrainEdge(Vector3 point)
    {
        EnsurePlacementTerrainReference();
        if (!freePlacement || terrainEdgeSnapDistanceMeters <= 0f || placementTerrainRenderer == null)
            return point;

        Bounds bounds = placementTerrainRenderer.bounds;
        float threshold = terrainEdgeSnapDistanceMeters;
        bool snapped = false;

        bool insideX = point.x >= bounds.min.x - threshold && point.x <= bounds.max.x + threshold;
        bool insideZ = point.z >= bounds.min.z - threshold && point.z <= bounds.max.z + threshold;
        if (insideZ && Mathf.Abs(point.x - bounds.min.x) <= threshold)
        {
            point.x = bounds.min.x;
            snapped = true;
        }
        else if (insideZ && Mathf.Abs(point.x - bounds.max.x) <= threshold)
        {
            point.x = bounds.max.x;
            snapped = true;
        }

        if (insideX && Mathf.Abs(point.z - bounds.min.z) <= threshold)
        {
            point.z = bounds.min.z;
            snapped = true;
        }
        else if (insideX && Mathf.Abs(point.z - bounds.max.z) <= threshold)
        {
            point.z = bounds.max.z;
            snapped = true;
        }

        if (!snapped) return point;

        // The edge can have a different elevation from the original hit. Use
        // a fresh terrain raycast so the dyke follows the actual terrain edge.
        float rayStartY = bounds.max.y + Mathf.Max(2f, bounds.size.y + 1f);
        float rayDistance = Mathf.Max(10f, bounds.size.y + 20f);
        Ray edgeRay = new Ray(new Vector3(point.x, rayStartY, point.z), Vector3.down);
        if (Physics.Raycast(edgeRay, out RaycastHit edgeHit, rayDistance, ~0,
            QueryTriggerInteraction.Ignore) && IsTerrainHit(edgeHit))
            point.y = edgeHit.point.y;

        return point;
    }

    void EnsurePlacementTerrainReference()
    {
        if (placementTerrain != null && placementTerrainRenderer != null) return;

        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
        {
            if (!string.Equals(child.name, "SM_Tutorial_Terrain_01", System.StringComparison.OrdinalIgnoreCase))
                continue;

            placementTerrain = child;
            placementTerrainRenderer = child.GetComponentInChildren<Renderer>(true);
            return;
        }
    }

    void EnsureFutureLengthLabel()
    {
        if (futureLengthLabel != null) return;
        GameObject labelObject = new GameObject("FutureDykeLengthLabel");
        futureLengthLabel = labelObject.AddComponent<DykeLengthLabel>();
    }

    void HideFutureLengthLabel()
    {
        if (futureLengthLabel == null) return;
        Destroy(futureLengthLabel.gameObject);
        futureLengthLabel = null;
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

            ScenarioDykeLength length = interaction.gameObject.GetComponent<ScenarioDykeLength>();
            Destroy(interaction.gameObject);

            dykeDestroyed++;
            FloodStudyLogger.RecordDykeDestroyed(length);
            FloodAudio.PlayDykeRemoved();
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
        HideFutureLengthLabel();
    }
}

/// <summary>Stores the measured horizontal length of a scenario dyke.</summary>
sealed class ScenarioDykeLength : MonoBehaviour
{
    public float lengthMeters;
    public int dykeId;
}

/// <summary>
/// Displays the current horizontal dyke length above a participant-built dyke.
/// For a drag preview the label is kept independent from the temporary preview
/// mesh, so it can be updated without allocating a new canvas every frame.
/// </summary>
sealed class DykeLengthLabel : MonoBehaviour
{
    [Min(.01f)] [SerializeField] float worldScale = .18f;
    [Min(0f)] [SerializeField] float heightOffset = .28f;

    TextMeshPro label;
    float lengthMeters;
    bool preview;
    Vector3 previewStart;
    Vector3 previewEnd;

    void Awake()
    {
        CreateLabel();
    }

    public static void Attach(GameObject dyke, float length)
    {
        if (dyke == null) return;
        DykeLengthLabel display = dyke.GetComponent<DykeLengthLabel>();
        if (display == null) display = dyke.AddComponent<DykeLengthLabel>();
        display.SetLength(length);
    }

    public void SetLength(float length)
    {
        preview = false;
        lengthMeters = Mathf.Max(0f, length);
        UpdateText();
    }

    public void SetPreview(Vector3 start, Vector3 end, float length)
    {
        preview = true;
        previewStart = start;
        previewEnd = end;
        lengthMeters = Mathf.Max(0f, length);
        UpdateText();
    }

    void LateUpdate()
    {
        if (label == null) CreateLabel();
        if (label == null) return;

        Bounds bounds;
        if (preview)
        {
            Vector3 center = (previewStart + previewEnd) * .5f;
            float y = Mathf.Max(previewStart.y, previewEnd.y);
            bounds = new Bounds(new Vector3(center.x, y, center.z), Vector3.zero);
        }
        else
        {
            Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
            if (!TryGetBounds(renderers, out bounds))
                bounds = new Bounds(transform.position, Vector3.zero);
        }

        label.transform.position = bounds.center + Vector3.up * (bounds.extents.y + heightOffset);
        label.transform.localScale = Vector3.one * worldScale;

        Camera camera = Camera.main;
        if (camera != null)
        {
            Vector3 direction = label.transform.position - camera.transform.position;
            if (direction.sqrMagnitude > .0001f)
                label.transform.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
        }
    }

    void CreateLabel()
    {
        if (label != null) return;
        GameObject textObject = new GameObject("Length");
        label = textObject.AddComponent<TextMeshPro>();
        label.font = TMP_Settings.defaultFontAsset;
        FloodLearningUIStyle.ApplyFont(label);
        label.alignment = TextAlignmentOptions.Center;
        label.horizontalAlignment = HorizontalAlignmentOptions.Center;
        label.verticalAlignment = VerticalAlignmentOptions.Middle;
        label.fontSize = 2.2f;
        label.enableWordWrapping = false;
        label.color = Color.white;
        label.outlineWidth = .22f;
        label.outlineColor = Color.black;
        UpdateText();
    }

    void UpdateText()
    {
        if (label != null)
            label.text = lengthMeters.ToString("F1", CultureInfo.InvariantCulture) + " m";
    }

    static bool TryGetBounds(Renderer[] renderers, out Bounds bounds)
    {
        bounds = default;
        bool hasBounds = false;
        if (renderers == null) return false;
        foreach (Renderer renderer in renderers)
        {
            if (renderer == null) continue;
            if (!hasBounds) { bounds = renderer.bounds; hasBounds = true; }
            else bounds.Encapsulate(renderer.bounds);
        }
        return hasBounds;
    }

    void OnDestroy()
    {
        if (label != null) Destroy(label.gameObject);
    }
}
