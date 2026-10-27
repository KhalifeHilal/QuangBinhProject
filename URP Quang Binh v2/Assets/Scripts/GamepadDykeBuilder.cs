using System;
using HutongGames.PlayMaker;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Gamepad-condition dyke construction.
///
/// Hold the right grip to show a terrain cursor, move it with the right
/// thumbstick, then press the right trigger once for the start and once for
/// the end point. Releasing grip before the second trigger cancels the draft.
/// The A button removes an existing participant dyke under the cursor.
/// </summary>
[DefaultExecutionOrder(-50)]
public sealed class GamepadDykeBuilder : MonoBehaviour
{
    public static bool IsBuildModeActive { get; private set; }

    [Header("Terrain cursor")]
    [Min(.01f)] [SerializeField] float cursorSpeed = 2.5f;
    [Range(0f, .5f)] [SerializeField] float stickDeadzone = .15f;
    [Min(.01f)] [SerializeField] float cursorHeight = .08f;
    [Min(.01f)] [SerializeField] float deletionRadius = .3f;
    [Min(.1f)] [SerializeField] float terrainRayHeight = 250f;
    [Min(1f)] [SerializeField] float terrainRayDistance = 600f;
    [Min(.05f)] [SerializeField] float tutorialSnapRadius = .45f;
    [Min(.02f)] [SerializeField] float hoverCheckInterval = .1f;
    [Min(.1f)] [SerializeField] float sceneSearchInterval = .5f;

    readonly Color cursorColor = new Color(.1f, .8f, 1f, 1f);
    readonly Color hoverColor = new Color(1f, .72f, .08f, 1f);
    readonly Color invalidColor = new Color(1f, .04f, .04f, 1f);

    DykeManagerTutorial manager;
    Transform terrainTransform;
    Renderer terrainRenderer;
    Collider terrainCollider;
    GameObject visualRoot;
    GameObject cursorMarker;
    GameObject startMarker;
    LineRenderer previewLine;
    Material visualMaterial;
    Renderer cursorRenderer;
    Renderer startRenderer;
    DykeLengthLabel lengthLabel;

    Vector3 cursorPosition;
    Vector3 startPosition;
    bool active;
    bool hasStart;
    bool invalidPreview;
    A1P1ScenarioDyke hoveredDyke;
    A1P1ScenarioDyke highlightedDyke;
    GameObject hoveredDykeObject;
    PlayMakerFSM tutorialManager;
    float nextHoverCheck;
    float nextSceneSearch;
    bool lastVisualHasStart;
    readonly RaycastHit[] terrainHits = new RaycastHit[64];

    void Awake()
    {
        // Create the marker root immediately so it is visible in the runtime
        // hierarchy even while hidden. It is only activated during a valid
        // Gamepad tutorial/build phase.
        CreateVisuals();
        SetVisualVisibility(false);
        manager = FindManager();
        tutorialManager = FindTutorialManager();
    }

    /// <summary>Called by GamepadInteractionController while Gamepad mode is active.</summary>
    public void Tick()
    {
        if (!ParticipantInput.IsGamepadParticipant)
        {
            ExitBuildMode();
            return;
        }

        bool gripHeld = ParticipantInput.RightGripIsPressed();
        if (!gripHeld)
        {
            if (active) ExitBuildMode();
            return;
        }

        if (manager == null && Time.unscaledTime >= nextSceneSearch)
        {
            manager = FindManager();
            nextSceneSearch = Time.unscaledTime + sceneSearchInterval;
        }
        bool tutorialActive = IsPlayMakerTutorialActive();
        // Keep the cursor available for the PlayMaker tutorial even if the
        // dyke manager is temporarily disabled while the FSM changes state.
        // Building still requires the manager; only the visual cursor is
        // allowed to exist without it.
        bool tutorialCursorAvailable = tutorialManager != null &&
            (tutorialActive || manager == null || (manager != null && !manager.freePlacement));
        bool placementAvailable = tutorialActive || tutorialCursorAvailable ||
            (manager != null && manager.isActiveAndEnabled && manager.freePlacement);
        if (!placementAvailable)
        {
            if (active) ExitBuildMode();
            return;
        }

        if (!active)
            EnterBuildMode();

        bool cursorMoved = UpdateCursorPosition();
        bool hoverChanged = false;
        bool triggerPressed = ParticipantInput.TriggerWasPressedThisFrame(null);
        bool confirmPressed = ParticipantInput.GamepadConfirmWasPressedThisFrame();

        // Hover checks walk the participant dykes and query their colliders.
        // They are throttled and skipped while the cursor is stationary so
        // the build cursor does not add a noticeable frame-time cost.
        if (cursorMoved || Time.unscaledTime >= nextHoverCheck || triggerPressed || confirmPressed)
        {
            A1P1ScenarioDyke previous = hoveredDyke;
            UpdateHoveredDyke();
            hoverChanged = previous != hoveredDyke;
            nextHoverCheck = Time.unscaledTime + hoverCheckInterval;
        }

        // A removes only an existing participant dyke under the terrain cursor.
        if (confirmPressed && hoveredDykeObject != null && !hasStart)
        {
            SetHighlightedDyke(null);
            manager.TryDestroyGamepadDyke(hoveredDykeObject);
            hoveredDyke = null;
            hoveredDykeObject = null;
        }

        if (triggerPressed)
            HandleTriggerPress();

        if (cursorMoved || hoverChanged || triggerPressed || confirmPressed || lastVisualHasStart != hasStart)
            UpdateVisuals();
    }

    public void Cancel()
    {
        ExitBuildMode();
    }

    void EnterBuildMode()
    {
        active = true;
        hasStart = false;
        invalidPreview = false;
        IsBuildModeActive = true;

        if (!TryProjectToTerrain(GetInitialCursorPosition(), out cursorPosition))
        {
            Bounds bounds = GetTerrainBounds();
            cursorPosition = bounds.center;
            cursorPosition.y = bounds.max.y;
        }

        CreateVisuals();
        SetVisualVisibility(true);
        UpdateVisuals();
    }

    void ExitBuildMode()
    {
        if (!active && !IsBuildModeActive) return;

        active = false;
        hasStart = false;
        invalidPreview = false;
        hoveredDyke = null;
        SetHighlightedDyke(null);
        hoveredDykeObject = null;
        IsBuildModeActive = false;

        SetVisualVisibility(false);
        if (lengthLabel != null) Destroy(lengthLabel.gameObject);
        lengthLabel = null;
    }

    void CreateVisuals()
    {
        if (visualRoot != null) return;

        visualRoot = new GameObject("Gamepad_Dyke_Build_Cursor");
        cursorMarker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        cursorMarker.name = "TerrainCursor";
        cursorMarker.transform.SetParent(visualRoot.transform, true);
        cursorMarker.transform.localScale = Vector3.one * .18f;
        Collider cursorCollider = cursorMarker.GetComponent<Collider>();
        if (cursorCollider != null) cursorCollider.enabled = false;

        startMarker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        startMarker.name = "DykeStartPoint";
        startMarker.transform.SetParent(visualRoot.transform, true);
        startMarker.transform.localScale = Vector3.one * .11f;
        Collider startCollider = startMarker.GetComponent<Collider>();
        if (startCollider != null) startCollider.enabled = false;
        startMarker.SetActive(false);

        visualMaterial = CreateVisualMaterial(cursorColor);
        cursorRenderer = cursorMarker.GetComponent<Renderer>();
        startRenderer = startMarker.GetComponent<Renderer>();
        cursorRenderer.material = visualMaterial;
        startRenderer.material = visualMaterial;

        previewLine = CreateLine("DykePreview", .06f);
        previewLine.gameObject.SetActive(false);
    }

    LineRenderer CreateLine(string name, float width)
    {
        GameObject lineObject = new GameObject(name);
        lineObject.transform.SetParent(visualRoot.transform, true);
        LineRenderer line = lineObject.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.positionCount = 5;
        line.widthMultiplier = width;
        line.numCapVertices = 4;
        line.numCornerVertices = 4;
        line.material = visualMaterial != null ? visualMaterial : CreateVisualMaterial(cursorColor);
        line.startColor = cursorColor;
        line.endColor = cursorColor;
        return line;
    }

    Material CreateVisualMaterial(Color color)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Sprites/Default");
        Material material = new Material(shader) { color = color };
        return material;
    }

    bool UpdateCursorPosition()
    {
        Vector3 before = cursorPosition;
        Vector2 stick = ParticipantInput.ReadGamepadRightStick();
        if (stick.sqrMagnitude >= stickDeadzone * stickDeadzone)
        {
            Camera camera = Camera.main;
            Vector3 forward = camera != null ? camera.transform.forward : Vector3.forward;
            Vector3 right = camera != null ? camera.transform.right : Vector3.right;
            forward.y = 0f;
            right.y = 0f;
            if (forward.sqrMagnitude < .001f) forward = Vector3.forward;
            if (right.sqrMagnitude < .001f) right = Vector3.right;
            forward.Normalize();
            right.Normalize();

            Vector3 movement = (right * stick.x + forward * stick.y) * cursorSpeed * Time.unscaledDeltaTime;
            Vector3 candidate = cursorPosition + movement;
            if (TryProjectToTerrain(candidate, out Vector3 terrainPoint))
                cursorPosition = terrainPoint;
        }
        // Keep the cursor stable while the stick is idle; there is no reason
        // to raycast the terrain again every frame.

        return (cursorPosition - before).sqrMagnitude > .000001f;
    }

    void HandleTriggerPress()
    {
        if (!hasStart)
        {
            if (!TryResolveBuildPoint(cursorPosition, out Vector3 tutorialStart))
            {
                invalidPreview = true;
                return;
            }
            startPosition = cursorPosition;
            if (manager != null && !manager.freePlacement) startPosition = tutorialStart;
            hasStart = true;
            invalidPreview = false;
            return;
        }

        if (!TryResolveBuildPoint(cursorPosition, out Vector3 endPoint))
        {
            invalidPreview = true;
            return;
        }

        bool built = manager != null &&
            (manager.freePlacement
                ? manager.TryBuildFreePlacementDyke(startPosition, endPoint)
                : manager.TryBuildTutorialDyke(startPosition, endPoint));
        if (built)
        {
            hasStart = false;
            invalidPreview = false;
        }
        else
        {
            invalidPreview = true;
        }
    }

    void UpdateHoveredDyke()
    {
        GameObject nextObject = manager != null
            ? manager.FindGamepadDykeAt(cursorPosition, deletionRadius)
            : null;
        A1P1ScenarioDyke next = nextObject != null
            ? nextObject.GetComponentInParent<A1P1ScenarioDyke>()
            : null;
        if (nextObject == hoveredDykeObject && next == hoveredDyke) return;
        hoveredDykeObject = nextObject;
        hoveredDyke = next;
        SetHighlightedDyke(hoveredDyke);
    }

    void SetHighlightedDyke(A1P1ScenarioDyke next)
    {
        if (highlightedDyke == next) return;
        if (highlightedDyke != null) highlightedDyke.SetHovered(false);
        highlightedDyke = next;
        if (highlightedDyke != null) highlightedDyke.SetHovered(true);
    }

    void UpdateVisuals()
    {
        if (visualRoot == null) return;

        bool lengthValid = !hasStart || IsCurrentBuildPointValid();
        if (lengthValid) invalidPreview = false;
        bool isInvalid = hasStart && (!lengthValid || invalidPreview);
        Color color = isInvalid ? invalidColor :
            (hoveredDyke != null && !hasStart ? hoverColor : cursorColor);

        SetVisualColor(color);
        Vector3 up = Vector3.up * cursorHeight;
        cursorMarker.transform.position = cursorPosition + up;

        startMarker.SetActive(hasStart);
        if (startRenderer != null) startRenderer.enabled = hasStart;
        if (hasStart)
        {
            startMarker.transform.position = startPosition + up;
            previewLine.gameObject.SetActive(true);
            previewLine.SetPosition(0, startPosition + up);
            previewLine.SetPosition(1, cursorPosition + up);
            previewLine.SetPosition(2, cursorPosition + up);
            previewLine.SetPosition(3, cursorPosition + up);
            previewLine.SetPosition(4, cursorPosition + up);

            if (lengthLabel == null)
            {
                GameObject labelObject = new GameObject("GamepadDykeLengthLabel");
                lengthLabel = labelObject.AddComponent<DykeLengthLabel>();
            }
            lengthLabel.SetPreview(startPosition, cursorPosition,
                HorizontalDistance(startPosition, cursorPosition));
        }
        else
        {
            previewLine.gameObject.SetActive(false);
            if (lengthLabel != null)
            {
                Destroy(lengthLabel.gameObject);
                lengthLabel = null;
            }
        }
        lastVisualHasStart = hasStart;
    }

    void SetVisualColor(Color color)
    {
        if (visualMaterial != null) visualMaterial.color = color;
        if (previewLine != null)
        {
            previewLine.startColor = color;
            previewLine.endColor = color;
        }
    }

    void SetVisualVisibility(bool visible)
    {
        if (visualRoot != null) visualRoot.SetActive(true);
        if (cursorRenderer != null) cursorRenderer.enabled = visible;
        if (startRenderer != null) startRenderer.enabled = visible && hasStart;
        if (previewLine != null && !visible) previewLine.gameObject.SetActive(false);
    }

    Vector3 GetInitialCursorPosition()
    {
        // Always start in the centre of the imported terrain. This avoids a
        // controller/camera ray missing the map and leaving the marker hidden.
        return GetTerrainBounds().center;
    }

    bool TryProjectToTerrain(Vector3 source, out Vector3 point)
    {
        point = default;
        EnsureTerrainReference();

        Bounds bounds = GetTerrainBounds();
        float rayY = Mathf.Max(source.y + terrainRayHeight, bounds.max.y + terrainRayHeight);
        Vector3 rayOrigin = new Vector3(source.x, rayY, source.z);
        Ray terrainRay = new Ray(rayOrigin, Vector3.down);
        if (terrainCollider != null && terrainCollider.Raycast(terrainRay, out RaycastHit terrainHit, terrainRayDistance))
        {
            point = terrainHit.point;
            // This is the explicitly cached terrain collider. Do not reject
            // it based on the imported mesh normal; some FBX terrain meshes
            // have their triangles wound with a downward normal even though
            // the collider is the correct ground surface.
            return true;
        }

        int hitCount = Physics.RaycastNonAlloc(rayOrigin, Vector3.down, terrainHits,
            terrainRayDistance, ~0, QueryTriggerInteraction.Ignore);

        float closest = float.MaxValue;
        bool found = false;
        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = terrainHits[i];
            if (hit.distance >= closest || !IsTerrainHit(hit)) continue;
            closest = hit.distance;
            point = hit.point;
            found = true;
        }

        if (found) return true;

        // Keep the cursor inside the imported map footprint if a collider is
        // temporarily disabled while the scenario is being prepared.
        if (bounds.size.sqrMagnitude > .001f &&
            source.x >= bounds.min.x && source.x <= bounds.max.x &&
            source.z >= bounds.min.z && source.z <= bounds.max.z)
        {
            point = new Vector3(source.x, bounds.max.y, source.z);
            return false;
        }

        return false;
    }

    void EnsureTerrainReference()
    {
        if (terrainTransform != null) return;

        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
        {
            if (child.name.Equals("SM_Tutorial_Terrain_01", StringComparison.OrdinalIgnoreCase))
            {
                terrainTransform = child;
                terrainRenderer = child.GetComponent<Renderer>();
                if (terrainRenderer == null)
                    terrainRenderer = child.GetComponentInChildren<Renderer>(true);
                // Prefer the collider on the terrain root. The imported map
                // can also contain a river child with its own collider, which
                // must never become the cursor's projection surface.
                terrainCollider = child.GetComponent<Collider>();
                if (terrainCollider == null)
                {
                    Collider[] colliders = child.GetComponentsInChildren<Collider>(true);
                    foreach (Collider candidate in colliders)
                    {
                        if (candidate == null || candidate.transform.name.IndexOf("river", StringComparison.OrdinalIgnoreCase) >= 0)
                            continue;
                        terrainCollider = candidate;
                        break;
                    }
                }
                return;
            }
        }

        Terrain terrain = Terrain.activeTerrain;
        if (terrain != null)
        {
            terrainTransform = terrain.transform;
            terrainRenderer = terrain.GetComponent<Renderer>();
            terrainCollider = terrain.GetComponent<Collider>();
        }
    }

    Bounds GetTerrainBounds()
    {
        EnsureTerrainReference();
        if (terrainRenderer != null) return terrainRenderer.bounds;
        if (terrainCollider != null) return terrainCollider.bounds;
        if (terrainTransform != null)
        {
            Collider collider = terrainTransform.GetComponentInChildren<Collider>(true);
            if (collider != null) return collider.bounds;
        }
        return new Bounds(transform.position, new Vector3(10f, 1f, 10f));
    }

    bool IsTerrainHit(RaycastHit hit)
    {
        if (hit.collider == null) return false;
        Transform current = hit.collider.transform;
        while (current != null)
        {
            if (current.name.Equals("SM_Tutorial_Terrain_01", StringComparison.OrdinalIgnoreCase) ||
                current.GetComponent<Terrain>() != null)
                return hit.normal.y > .1f;
            if (current.name.IndexOf("river", StringComparison.OrdinalIgnoreCase) >= 0)
                return false;
            current = current.parent;
        }

        Bounds bounds = GetTerrainBounds();
        return hit.normal.y > .1f && bounds.Contains(hit.point);
    }

    bool TryResolveBuildPoint(Vector3 cursor, out Vector3 point)
    {
        point = cursor;
        if (manager == null) return false;
        if (manager.freePlacement) return true;
        return manager.TryGetTutorialSnapPoint(cursor, tutorialSnapRadius, out point);
    }

    bool IsCurrentBuildPointValid()
    {
        if (!hasStart || manager == null) return false;
        if (!TryResolveBuildPoint(cursorPosition, out Vector3 endPoint)) return false;
        return manager.freePlacement
            ? manager.CanBuildFreePlacementDyke(startPosition, endPoint)
            : manager.CanBuildTutorialDyke(startPosition, endPoint);
    }

    bool IsPlayMakerTutorialActive()
    {
        if (tutorialManager == null && Time.unscaledTime >= nextSceneSearch)
        {
            tutorialManager = FindTutorialManager();
            nextSceneSearch = Time.unscaledTime + sceneSearchInterval;
        }

        return tutorialManager != null && tutorialManager.Fsm != null &&
            tutorialManager.Fsm.ActiveStateName != "Etat 12";
    }

    static PlayMakerFSM FindTutorialManager()
    {
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            if (child.name.Equals("Tutorial_Manager", StringComparison.OrdinalIgnoreCase))
                return child.GetComponent<PlayMakerFSM>();
        return null;
    }

    void OnDisable()
    {
        ExitBuildMode();
    }

    void OnDestroy()
    {
        ExitBuildMode();
        if (visualMaterial != null) Destroy(visualMaterial);
    }

    static DykeManagerTutorial FindManager()
    {
        return UnityEngine.Object.FindFirstObjectByType<DykeManagerTutorial>(FindObjectsInactive.Include);
    }

    static float HorizontalDistance(Vector3 start, Vector3 end)
    {
        Vector3 delta = end - start;
        delta.y = 0f;
        return delta.magnitude;
    }
}
