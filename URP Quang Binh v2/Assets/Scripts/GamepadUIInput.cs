using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Keyboard-free UI navigation for Quest participants in the Gamepad
/// condition. The right thumbstick navigates selectable controls and adjusts
/// sliders; the right A button submits the highlighted control.
/// </summary>
public static class GamepadUIInput
{
    const float StickDeadzone = .35f;
    const float MoveRepeatDelay = .24f;
    const float MoveRepeatInterval = .14f;

    static Selectable selected;
    static Canvas selectedCanvas;
    static float nextMoveTime;

    public static void Tick()
    {
        if (!ParticipantInput.IsGamepadParticipant)
            return;

        EventSystem eventSystem = EventSystem.current;
        if (eventSystem == null)
            return;

        EnsureSelected(eventSystem);
        if (selected == null)
            return;

        Vector2 stick = ParticipantInput.ReadGamepadRightStick();
        HandleStick(eventSystem, stick);

        if (ParticipantInput.GamepadConfirmWasPressedThisFrame())
            Submit(eventSystem);
    }

    /// <summary>
    /// Lets B activate the authored tutorial Start button. Scenario start
    /// prompts are handled by their flow scripts and are not UI Buttons, so
    /// this does not change the A-to-select rule for answers and sliders.
    /// </summary>
    public static void SubmitTutorialStart(EventSystem eventSystem)
    {
        if (eventSystem == null)
            return;

        Button startButton = IsStartControl(selected) ? selected as Button : null;
        if (startButton == null)
        {
            foreach (Selectable candidate in Selectable.allSelectablesArray)
            {
                if (candidate is Button button && IsUsable(button) && IsStartControl(button))
                {
                    startButton = button;
                    break;
                }
            }
        }

        if (startButton == null)
            return;

        selected = startButton;
        selectedCanvas = CanvasFor(selected);
        eventSystem.SetSelectedGameObject(selected.gameObject);
        selected.Select();
        ExecuteEvents.Execute(selected.gameObject,
            new BaseEventData(eventSystem), ExecuteEvents.submitHandler);
    }

    static void EnsureSelected(EventSystem eventSystem)
    {
        Selectable eventSelected = eventSystem.currentSelectedGameObject == null
            ? null
            : eventSystem.currentSelectedGameObject.GetComponentInParent<Selectable>();

        if (IsUsable(eventSelected))
        {
            selected = eventSelected;
            selectedCanvas = CanvasFor(selected);
            return;
        }

        if (!IsUsable(selected))
            selected = FindFirstUsableSelectable();

        if (selected == null)
            return;

        selectedCanvas = CanvasFor(selected);
        eventSystem.SetSelectedGameObject(selected.gameObject);
        selected.Select();
    }

    static Selectable FindFirstUsableSelectable()
    {
        Selectable[] all = Selectable.allSelectablesArray;
        Selectable best = null;
        int bestSortingOrder = int.MinValue;

        foreach (Selectable candidate in all)
        {
            if (!IsUsable(candidate))
                continue;

            Canvas canvas = CanvasFor(candidate);
            int sortingOrder = canvas != null ? canvas.sortingOrder : int.MinValue;
            if (best == null || sortingOrder > bestSortingOrder)
            {
                best = candidate;
                bestSortingOrder = sortingOrder;
            }
        }

        return best;
    }

    static void HandleStick(EventSystem eventSystem, Vector2 stick)
    {
        float magnitude = stick.magnitude;
        if (magnitude < StickDeadzone)
        {
            nextMoveTime = 0f;
            return;
        }

        if (Time.unscaledTime < nextMoveTime)
            return;

        bool horizontal = Mathf.Abs(stick.x) > Mathf.Abs(stick.y);
        float direction = horizontal ? Mathf.Sign(stick.x) : Mathf.Sign(stick.y);

        Slider slider = selected as Slider;
        if (slider != null && horizontal)
        {
            float step = slider.wholeNumbers
                ? 1f
                : Mathf.Max(.01f, (slider.maxValue - slider.minValue) * .02f);
            slider.value = Mathf.Clamp(slider.value + direction * step, slider.minValue, slider.maxValue);
        }
        else
        {
            MoveSelection(eventSystem, horizontal, direction);
        }

        nextMoveTime = Time.unscaledTime +
            (nextMoveTime <= 0f ? MoveRepeatDelay : MoveRepeatInterval);
    }

    static void MoveSelection(EventSystem eventSystem, bool horizontal, float direction)
    {
        if (selected == null)
            return;

        RectTransform currentRect = selected.transform as RectTransform;
        Vector3 axis = horizontal
            ? (currentRect != null ? currentRect.right : Vector3.right)
            : (currentRect != null ? currentRect.up : Vector3.up);
        axis *= direction;

        Vector3 currentPosition = selected.transform.position;
        Selectable best = null;
        float bestScore = float.MaxValue;

        foreach (Selectable candidate in Selectable.allSelectablesArray)
        {
            if (!IsUsable(candidate) || candidate == selected)
                continue;
            if (CanvasFor(candidate) != selectedCanvas)
                continue;

            Vector3 delta = candidate.transform.position - currentPosition;
            float along = Vector3.Dot(delta, axis);
            if (along <= .001f)
                continue;

            float perpendicular = (delta - axis * along).magnitude;
            float score = along + perpendicular * 1.5f;
            if (score < bestScore)
            {
                best = candidate;
                bestScore = score;
            }
        }

        if (best == null)
            return;

        selected = best;
        eventSystem.SetSelectedGameObject(selected.gameObject);
        selected.Select();
    }

    static void Submit(EventSystem eventSystem)
    {
        if (selected == null || !IsUsable(selected))
            return;

        // A confirms the current slider value and moves to the next control
        // below it, such as the confidence Confirm button.
        if (selected is Slider)
        {
            MoveSelection(eventSystem, false, -1f);
            return;
        }

        ExecuteEvents.Execute(selected.gameObject,
            new BaseEventData(eventSystem), ExecuteEvents.submitHandler);
    }

    static bool IsUsable(Selectable candidate)
    {
        return candidate != null && candidate.isActiveAndEnabled && candidate.IsInteractable() &&
               candidate.gameObject.activeInHierarchy;
    }

    static bool IsStartControl(Selectable candidate)
    {
        if (!(candidate is Button) || !IsUsable(candidate))
            return false;

        Transform current = candidate.transform;
        while (current != null)
        {
            if (current.name.IndexOf("start", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            current = current.parent;
        }

        TMP_Text label = candidate.GetComponentInChildren<TMP_Text>(true);
        return label != null && label.text.IndexOf("start", System.StringComparison.OrdinalIgnoreCase) >= 0;
    }

    static Canvas CanvasFor(Selectable candidate)
    {
        return candidate != null ? candidate.GetComponentInParent<Canvas>() : null;
    }

    public static void ResetSelection()
    {
        selected = null;
        selectedCanvas = null;
        nextMoveTime = 0f;
    }
}
