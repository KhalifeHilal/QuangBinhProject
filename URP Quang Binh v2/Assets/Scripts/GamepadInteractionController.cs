using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using Unity.XR.CoreUtils;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

/// <summary>
/// Turns the Quest 3 controllers into the participant Gamepad condition.
/// The GAMEPAD model replaces the left controller model and the right
/// controller model is hidden. The left stick controls locomotion and the
/// right stick is reserved for direct UI navigation.
///
/// Existing XR ray, poke, near/far and direct interactors are disabled in this
/// condition because UI selection is handled by GamepadUIInput.
/// </summary>
[DefaultExecutionOrder(-100)]
public sealed class GamepadInteractionController : MonoBehaviour
{
    [Header("Gamepad visual")]
    [SerializeField] Transform gamepadPrefab;
    [SerializeField] Vector3 gamepadLocalPosition;
    [SerializeField] Vector3 gamepadLocalEulerAngles;
    [Min(.01f)] [SerializeField] float gamepadLocalScale = 1f;

    [Header("Gamepad locomotion")]
    [Min(0f)] [SerializeField] float forwardBackwardSpeed = 5f;
    [Min(0f)] [SerializeField] float verticalSpeed = 2f;
    [SerializeField] float minimumHeight;
    [SerializeField] float maximumHeight = 150f;
    [Range(0f, .5f)] [SerializeField] float stickDeadzone = .12f;

    GameObject gamepadVisual;
    GamepadDykeBuilder dykeBuilder;
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

    readonly List<GameObject> disabledInteractorObjects = new List<GameObject>();
    readonly List<Behaviour> disabledLegacyMovement = new List<Behaviour>();

    void Awake()
    {
        rightController = GetComponent<ActionBasedController>();
        XROrigin origin = FindFirstObjectByType<XROrigin>();
        movementRoot = origin != null ? origin.transform : transform.root;
        dykeBuilder = GetComponent<GamepadDykeBuilder>();
        if (dykeBuilder == null)
            dykeBuilder = gameObject.AddComponent<GamepadDykeBuilder>();
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
        {
            dykeBuilder?.Cancel();
            return;
        }

        UpdateGamepadMovement();
        // Process dyke input from this already-running controller loop. This
        // avoids a second per-frame MonoBehaviour Update.
        dykeBuilder?.Tick();
        // The right stick controls the terrain cursor while the right grip is
        // held. Do not let UI navigation consume that same input during a
        // construction draft.
        if (!GamepadDykeBuilder.IsBuildModeActive && !ParticipantInput.RightGripIsPressed())
            GamepadUIInput.Tick();
        if (ParticipantInput.GamepadStartWasPressedThisFrame())
            GamepadUIInput.SubmitTutorialStart(EventSystem.current);
    }

    void LateUpdate()
    {
        if (gamepadModeActive)
            GamepadUIInput.RefreshTutorialInstructions();
    }

    void EnableGamepadMode()
    {
        ParticipantInput.SetGamepadModeEnabled(true);
        RemoveRightQuestControllerModel();
        SpawnGamepadVisual();
        DisableLegacyInteractions();
        DisableLegacyMovement();
        GamepadUIInput.ResetSelection();
        gamepadModeActive = true;
        GamepadUIInput.RefreshTutorialInstructions();
    }

    void DisableGamepadMode()
    {
        gamepadModeActive = false;
        dykeBuilder?.Cancel();
        ParticipantInput.SetGamepadModeEnabled(false);
        GamepadUIInput.ResetSelection();
        RestoreLegacyMovement();
        RestoreLegacyInteractions();
        RestoreRightQuestControllerModel();
        RestoreLeftQuestControllerModel();

        if (gamepadVisual != null)
            Destroy(gamepadVisual);
        gamepadVisual = null;
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

    void UpdateGamepadMovement()
    {
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

        // A slight diagonal input does not combine forward movement with
        // flying; the stronger axis determines the locomotion direction.
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
        // The scene is already being torn down, so do not reactivate objects.
        if (!gamepadModeActive)
            return;

        gamepadModeActive = false;
        ParticipantInput.SetGamepadModeEnabled(false);
    }
}
