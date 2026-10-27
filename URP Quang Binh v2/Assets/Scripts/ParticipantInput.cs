using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Readers;

/// <summary>
/// Input actions used by the Quest-controller Gamepad condition. It deliberately
/// uses the two tracked Quest controllers, not an external Xbox device.
/// </summary>
public static class ParticipantInput
{
    static InputAction confirm;
    static InputAction start;
    static InputAction simulationStart;
    static InputAction rightTrigger;
    static InputAction rightGrip;
    static InputAction leftGrip;
    static InputAction leftStick;
    static InputAction rightStick;
    static bool actionsEnabled;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetForNewRun()
    {
        DisposeActions();
        actionsEnabled = false;
    }

    public static bool IsGamepadParticipant =>
        ParticipantSession.IsCheckedIn &&
        ParticipantSession.InteractionMode == ParticipantInteractionMode.Gamepad;

    public static void SetGamepadModeEnabled(bool enabled)
    {
        EnsureActions();
        if (enabled == actionsEnabled)
        {
            if (enabled && !confirm.enabled) EnableActions();
            return;
        }

        if (enabled) EnableActions();
        else DisableActions();
        actionsEnabled = enabled;
    }

    public static bool ContinueWasPressedThisFrame(InputAction questAction)
    {
        if (IsGamepadParticipant)
        {
            EnsureEnabled();
            return confirm.WasPressedThisFrame();
        }

        return questAction != null && questAction.WasPressedThisFrame();
    }

    /// <summary>A button used by the Gamepad UI to submit the highlighted control.</summary>
    public static bool GamepadConfirmWasPressedThisFrame()
    {
        if (!IsGamepadParticipant) return false;
        EnsureEnabled();
        return confirm.WasPressedThisFrame();
    }

    /// <summary>Reads the right B button for tutorial/scenario start prompts.</summary>
    public static bool GamepadStartWasPressedThisFrame()
    {
        if (!IsGamepadParticipant) return false;
        EnsureEnabled();
        return start.WasPressedThisFrame();
    }

    /// <summary>
    /// B starts or continues non-UI phases for Gamepad participants. Other
    /// interaction modes keep their existing action (usually A).
    /// </summary>
    public static bool StartWasPressedThisFrame(InputAction questAction)
    {
        if (IsGamepadParticipant)
        {
            EnsureEnabled();
            return start.WasPressedThisFrame();
        }

        return questAction != null && questAction.WasPressedThisFrame();
    }

    public static bool TriggerWasPressedThisFrame(InputAction questAction)
    {
        if (IsGamepadParticipant)
        {
            EnsureEnabled();
            return rightTrigger.WasPressedThisFrame();
        }

        return questAction != null && questAction.WasPressedThisFrame();
    }

    public static bool TriggerIsPressed(InputAction questAction)
    {
        if (IsGamepadParticipant)
        {
            EnsureEnabled();
            return rightTrigger.IsPressed();
        }

        return questAction != null && questAction.IsPressed();
    }

    /// <summary>
    /// Y starts the flood simulation and the feedback simulation for Gamepad
    /// participants. Non-Gamepad participants keep their existing A action.
    /// </summary>
    public static bool SimulationStartWasPressedThisFrame(InputAction questAction)
    {
        if (IsGamepadParticipant)
        {
            EnsureEnabled();
            return simulationStart.WasPressedThisFrame();
        }

        return questAction != null && questAction.WasPressedThisFrame();
    }

    public static bool GripWasPressedThisFrame(InputAction questAction)
    {
        if (IsGamepadParticipant)
        {
            EnsureEnabled();
            return rightGrip.WasPressedThisFrame();
        }

        return questAction != null && questAction.WasPressedThisFrame();
    }

    public static bool LeftGripIsPressed()
    {
        if (!IsGamepadParticipant) return false;
        EnsureEnabled();
        return leftGrip.IsPressed();
    }

    public static bool RightGripIsPressed()
    {
        if (!IsGamepadParticipant) return false;
        EnsureEnabled();
        return rightGrip.IsPressed();
    }

    /// <summary>Reads the Quest left thumbstick for locomotion.</summary>
    public static Vector2 ReadGamepadMoveStick()
    {
        if (!IsGamepadParticipant) return Vector2.zero;
        EnsureEnabled();
        return leftStick.ReadValue<Vector2>();
    }

    /// <summary>Reads the Quest right thumbstick for Gamepad UI navigation.</summary>
    public static Vector2 ReadGamepadRightStick()
    {
        if (!IsGamepadParticipant) return Vector2.zero;
        EnsureEnabled();
        return rightStick.ReadValue<Vector2>();
    }

    /// <summary>Creates an XRI reader backed by the Quest right trigger.</summary>
    public static XRInputButtonReader CreateGamepadTriggerReader(string name)
    {
        EnsureActions();
        XRInputButtonReader reader = new XRInputButtonReader
        {
            inputSourceMode = XRInputButtonReader.InputSourceMode.InputAction,
            inputActionPerformed = rightTrigger,
            inputActionValue = rightTrigger
        };
        return reader;
    }

    static void EnsureEnabled()
    {
        SetGamepadModeEnabled(true);
    }

    static void EnsureActions()
    {
        if (confirm != null) return;

        confirm = new InputAction("Quest Gamepad Confirm", InputActionType.Button,
            "<XRController>{RightHand}/primaryButton");
        start = new InputAction("Quest Gamepad Start", InputActionType.Button,
            "<XRController>{RightHand}/secondaryButton");
        simulationStart = new InputAction("Quest Gamepad Simulation Start", InputActionType.Button,
            "<XRController>{LeftHand}/secondaryButton");
        rightTrigger = new InputAction("Quest Gamepad Trigger", InputActionType.Button,
            "<XRController>{RightHand}/triggerPressed");
        rightGrip = new InputAction("Quest Gamepad Right Grip", InputActionType.Button,
            "<XRController>{RightHand}/gripPressed");
        leftGrip = new InputAction("Quest Gamepad Left Grip", InputActionType.Button,
            "<XRController>{LeftHand}/gripPressed");
        // primary2DAxis is the XRI-compatible name for the Quest thumbstick
        // and works with both the Quest 3 controller layout and OpenXR.
        leftStick = new InputAction("Quest Gamepad Movement", InputActionType.Value,
            "<XRController>{LeftHand}/primary2DAxis");
        rightStick = new InputAction("Quest Gamepad UI Navigation", InputActionType.Value,
            "<XRController>{RightHand}/primary2DAxis");
    }

    static void EnableActions()
    {
        confirm.Enable();
        start.Enable();
        simulationStart.Enable();
        rightTrigger.Enable();
        rightGrip.Enable();
        leftGrip.Enable();
        leftStick.Enable();
        rightStick.Enable();
        actionsEnabled = true;
    }

    static void DisableActions()
    {
        confirm.Disable();
        start.Disable();
        simulationStart.Disable();
        rightTrigger.Disable();
        rightGrip.Disable();
        leftGrip.Disable();
        leftStick.Disable();
        rightStick.Disable();
        actionsEnabled = false;
    }

    static void DisposeActions()
    {
        confirm?.Dispose();
        start?.Dispose();
        simulationStart?.Dispose();
        rightTrigger?.Dispose();
        rightGrip?.Dispose();
        leftGrip?.Dispose();
        leftStick?.Dispose();
        rightStick?.Dispose();
        confirm = null;
        start = null;
        simulationStart = null;
        rightTrigger = null;
        rightGrip = null;
        leftGrip = null;
        leftStick = null;
        rightStick = null;
    }
}
