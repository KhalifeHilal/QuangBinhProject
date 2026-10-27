using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.XR.CoreUtils;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

/// <summary>
/// Turns the Quest 3 controllers into the participant Gamepad condition.
/// The GAMEPAD model replaces the left controller model and the right
/// controller model is hidden. The right Quest thumbstick steers one visual
/// ray that starts at the center of the GAMEPAD model.
///
/// The normal XR ray, poke, near/far and direct interactors are disabled for
/// this condition. They are restored if the condition is disabled again.
/// </summary>
[DefaultExecutionOrder(-100)]
public sealed class GamepadInteractionController : MonoBehaviour
{
    public bool IsGamepadRayActive => gamepadModeActive && rayOrigin != null;
    public Transform GamepadRayOrigin => rayOrigin;

    [Header("Gamepad visual")]
    [SerializeField] Transform gamepadPrefab;
    [SerializeField] Vector3 gamepadLocalPosition;
    [SerializeField] Vector3 gamepadLocalEulerAngles;
    [Min(.01f)] [SerializeField] float gamepadLocalScale = 1f;

    [Header("Quest Gamepad ray")]
    [SerializeField] Vector3 rayLocalPosition = new Vector3(-0.05f, 0f, 0f);
    // The GAMEPAD model's forward axis is its local +Y axis. Rotating the
    // ray -90 degrees around X makes the initial ray leave the model forward.
    [SerializeField] Vector3 rayLocalEulerAngles = new Vector3(-90f, 0f, 0f);
    [Min(0.1f)] [SerializeField] float rayLength = 20f;
    [Min(1f)] [SerializeField] float rayYawSpeed = 110f;
    [Min(1f)] [SerializeField] float rayPitchSpeed = 90f;
    [SerializeField] float minimumRayPitch = -70f;
    [SerializeField] float maximumRayPitch = 70f;
    [Range(0f, .5f)] [SerializeField] float stickDeadzone = .12f;
    [Min(.001f)] [SerializeField] float rayWidth = .008f;
    [SerializeField] Color rayColor = new Color(.15f, 1f, .35f, 1f);

    [Header("Gamepad locomotion")]
    [Min(0f)] [SerializeField] float forwardBackwardSpeed = 5f;
    [Min(0f)] [SerializeField] float verticalSpeed = 2f;
    [SerializeField] float minimumHeight;
    [SerializeField] float maximumHeight = 150f;

    GameObject gamepadVisual;
    Transform movementRoot;
    ActionBasedController leftController;
    ActionBasedController rightController;
    Transform originalLeftModel;
    Transform originalLeftModelPrefab;
    Transform originalRightModel;
    Transform originalRightModelPrefab;
    bool originalLeftModelHidden;
    bool originalRightModelHidden;
    bool gamepadModeActive;
    Transform rayOrigin;
    LineRenderer gamepadRay;
    float rayYaw;
    float rayPitch;

    readonly List<GameObject> disabledInteractorObjects = new List<GameObject>();
    readonly List<Behaviour> disabledLegacyMovement = new List<Behaviour>();

    void Awake()
    {
        rightController = GetComponent<ActionBasedController>();
        XROrigin origin = FindFirstObjectByType<XROrigin>();
        movementRoot = origin != null ? origin.transform : transform.root;
    }

    void Update()
    {
        bool shouldUseGamepad = ParticipantInput.IsGamepadParticipant;
        if (shouldUseGamepad != gamepadModeActive)
        {
            if (shouldUseGamepad) EnableGamepadMode();
            else DisableGamepadMode();
        }

        if (!gamepadModeActive)
            return;

        bool rightGripHeld = ParticipantInput.RightGripIsPressed();
        if (rightGripHeld)
            UpdateGamepadRay();
        else
        {
            HideGamepadRay();
            UpdateGamepadMovement();
        }
    }

    void EnableGamepadMode()
    {
        ParticipantInput.SetGamepadModeEnabled(true);
        RemoveRightQuestControllerModel();
        SpawnGamepadVisual();
        DisableLegacyInteractions();
        DisableLegacyMovement();
        gamepadModeActive = true;
    }

    void DisableGamepadMode()
    {
        gamepadModeActive = false;
        ParticipantInput.SetGamepadModeEnabled(false);
        RestoreLegacyMovement();
        RestoreLegacyInteractions();
        RestoreRightQuestControllerModel();
        RestoreLeftQuestControllerModel();

        if (gamepadRay != null && gamepadRay.material != null)
            Destroy(gamepadRay.material);
        if (gamepadVisual != null)
            Destroy(gamepadVisual);
        gamepadVisual = null;
        rayOrigin = null;
        gamepadRay = null;
    }

    void DisableLegacyInteractions()
    {
        disabledInteractorObjects.Clear();

        XRBaseInteractor[] interactors = FindObjectsByType<XRBaseInteractor>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);

        foreach (XRBaseInteractor interactor in interactors)
        {
            if (interactor == null || interactor.gameObject == gameObject)
                continue;

            GameObject interactorObject = interactor.gameObject;
            if (!interactorObject.activeSelf || disabledInteractorObjects.Contains(interactorObject))
                continue;

            disabledInteractorObjects.Add(interactorObject);
            interactorObject.SetActive(false);
        }
    }

    void RestoreLegacyInteractions()
    {
        foreach (GameObject interactorObject in disabledInteractorObjects)
        {
            if (interactorObject != null)
                interactorObject.SetActive(true);
        }

        disabledInteractorObjects.Clear();
    }

    void DisableLegacyMovement()
    {
        disabledLegacyMovement.Clear();

        MoveHorizontal[] horizontal = FindObjectsByType<MoveHorizontal>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (MoveHorizontal movement in horizontal)
        {
            if (movement != null && movement.enabled)
            {
                movement.enabled = false;
                disabledLegacyMovement.Add(movement);
            }
        }

        MoveVertical[] vertical = FindObjectsByType<MoveVertical>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (MoveVertical movement in vertical)
        {
            if (movement != null && movement.enabled)
            {
                movement.enabled = false;
                disabledLegacyMovement.Add(movement);
            }
        }
    }

    void RestoreLegacyMovement()
    {
        foreach (Behaviour movement in disabledLegacyMovement)
        {
            if (movement != null)
                movement.enabled = true;
        }

        disabledLegacyMovement.Clear();
    }

    void UpdateGamepadRay()
    {
        if (rayOrigin == null)
            return;

        if (gamepadRay != null)
            gamepadRay.enabled = true;

        Vector2 stick = ParticipantInput.ReadGamepadRightStick();
        if (stick.sqrMagnitude < stickDeadzone * stickDeadzone)
            return;

        // Make the ray four-directional. A small diagonal component is
        // ignored, so up is only up, down only down, left only left, and
        // right only right.
        if (Mathf.Abs(stick.x) > Mathf.Abs(stick.y))
            stick = new Vector2(Mathf.Sign(stick.x), 0f);
        else
            stick = new Vector2(0f, Mathf.Sign(stick.y));

        // Invert the local model axes so right/up on the thumbsticks move the
        // visible ray right/up instead of mirrored left/down.
        rayYaw -= stick.x * rayYawSpeed * Time.unscaledDeltaTime;
        rayPitch = Mathf.Clamp(rayPitch + stick.y * rayPitchSpeed * Time.unscaledDeltaTime,
            minimumRayPitch, maximumRayPitch);
        rayOrigin.localRotation = Quaternion.Euler(rayPitch, rayYaw, 0f) *
                                  Quaternion.Euler(rayLocalEulerAngles);
    }

    void HideGamepadRay()
    {
        if (gamepadRay != null)
            gamepadRay.enabled = false;
    }

    void UpdateGamepadMovement()
    {
        if (gamepadRay != null)
            gamepadRay.enabled = false;
        if (movementRoot == null)
            return;

        Vector2 stick = ParticipantInput.ReadGamepadMoveStick();
        if (stick.sqrMagnitude < stickDeadzone * stickDeadzone)
            return;

        Camera camera = Camera.main;
        Vector3 forward = camera != null ? camera.transform.forward : movementRoot.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < .001f)
            forward = movementRoot.forward;
        forward.y = 0f;
        forward.Normalize();

        // Keep the two movement directions separate. A slight diagonal input
        // therefore does not combine forward movement with flying.
        Vector3 movement;
        if (Mathf.Abs(stick.y) >= Mathf.Abs(stick.x))
            movement = forward * (stick.y * forwardBackwardSpeed);
        else
            movement = Vector3.up * (stick.x * verticalSpeed);

        movementRoot.position += movement * Time.unscaledDeltaTime;
        Vector3 position = movementRoot.position;
        position.y = Mathf.Clamp(position.y, minimumHeight, maximumHeight);
        movementRoot.position = position;
    }

    void RemoveRightQuestControllerModel()
    {
        if (rightController == null)
            return;

        originalRightModel = rightController.model;
        originalRightModelPrefab = rightController.modelPrefab;
        originalRightModelHidden = rightController.hideControllerModel;
        rightController.modelPrefab = null;
        rightController.hideControllerModel = true;
    }

    void RestoreRightQuestControllerModel()
    {
        if (rightController == null)
            return;

        rightController.modelPrefab = originalRightModelPrefab;
        rightController.model = originalRightModel;
        rightController.hideControllerModel = originalRightModelHidden;
        if (originalRightModel != null)
            originalRightModel.gameObject.SetActive(true);
    }

    void SpawnGamepadVisual()
    {
        if (gamepadVisual != null || gamepadPrefab == null)
            return;

        if (leftController == null)
            leftController = FindLeftController();
        if (leftController == null)
        {
            Debug.LogWarning("Gamepad mode could not find the left ActionBasedController.", this);
            return;
        }

        originalLeftModel = leftController.model;
        originalLeftModelPrefab = leftController.modelPrefab;
        originalLeftModelHidden = leftController.hideControllerModel;
        if (originalLeftModel != null)
            originalLeftModel.gameObject.SetActive(false);

        leftController.modelPrefab = gamepadPrefab;
        Transform parent = leftController.modelParent != null
            ? leftController.modelParent
            : leftController.transform;

        Transform instance = Instantiate(gamepadPrefab, parent, false);
        gamepadVisual = instance.gameObject;
        gamepadVisual.name = "GAMEPAD (Participant Gamepad)";
        gamepadVisual.transform.localPosition = gamepadLocalPosition;
        gamepadVisual.transform.localRotation = Quaternion.Euler(gamepadLocalEulerAngles) * gamepadVisual.transform.localRotation;
        gamepadVisual.transform.localScale *= gamepadLocalScale;
        leftController.model = instance;
        leftController.hideControllerModel = false;
        CreateGamepadRay(instance);
    }

    void CreateGamepadRay(Transform gamepadRoot)
    {
        GameObject rayObject = new GameObject("QuestGamepad_VisualRay");
        rayObject.transform.SetParent(gamepadRoot, false);
        rayOrigin = rayObject.transform;
        rayOrigin.localPosition = rayLocalPosition;
        rayOrigin.localRotation = Quaternion.Euler(rayLocalEulerAngles);

        gamepadRay = rayObject.AddComponent<LineRenderer>();
        gamepadRay.useWorldSpace = false;
        gamepadRay.positionCount = 2;
        gamepadRay.SetPosition(0, Vector3.zero);
        gamepadRay.SetPosition(1, Vector3.forward * rayLength);
        gamepadRay.startWidth = rayWidth;
        gamepadRay.endWidth = rayWidth;
        gamepadRay.startColor = rayColor;
        gamepadRay.endColor = rayColor;
        gamepadRay.numCapVertices = 4;

        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Sprites/Default");
        if (shader != null)
            gamepadRay.material = new Material(shader);
    }

    void RestoreLeftQuestControllerModel()
    {
        if (leftController == null)
            return;

        leftController.model = originalLeftModel;
        leftController.modelPrefab = originalLeftModelPrefab;
        if (originalLeftModel != null)
            originalLeftModel.gameObject.SetActive(true);
        leftController.hideControllerModel = originalLeftModelHidden;
    }

    ActionBasedController FindLeftController()
    {
        ActionBasedController[] controllers = FindObjectsByType<ActionBasedController>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (ActionBasedController controller in controllers)
        {
            if (controller != null && controller.gameObject.name.IndexOf(
                    "left controller", StringComparison.OrdinalIgnoreCase) >= 0)
                return controller;
        }

        return null;
    }

    void OnDestroy()
    {
        // Do not reactivate controller or interactor objects while Unity is
        // destroying the scene. The scene is being torn down already.
        if (!gamepadModeActive)
            return;

        gamepadModeActive = false;
        ParticipantInput.SetGamepadModeEnabled(false);
    }
}
