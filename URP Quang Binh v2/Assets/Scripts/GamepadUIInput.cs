using TMPro;
using System;
using System.Collections.Generic;
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

        // B is reserved for an actual tutorial/start prompt. Never search
        // globally while an explanation, answer, confidence, or Continue UI
        // is active; otherwise B can change the active flow and leave A's
        // Continue action without a valid target.
        if (IsUsable(selected) && !IsStartControl(selected))
            return;
        if (FindFirstQuestionControl() != null ||
            FindFirstUsableSlider() != null ||
            FindFirstUsableContinueButton() != null)
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
        // Every question starts with answer A selected. If the previous
        // question or confidence panel was destroyed, never keep a stale
        // selection from that old canvas.
        Selectable firstQuestionControl = FindFirstQuestionControl();
        if (firstQuestionControl != null)
        {
            if (!IsUsable(selected) || !IsQuestionControl(selected) ||
                CanvasFor(selected) != CanvasFor(firstQuestionControl))
            {
                Select(eventSystem, firstQuestionControl);
            }
            else if (eventSystem.currentSelectedGameObject != selected.gameObject)
            {
                Select(eventSystem, selected);
            }
            return;
        }

        // Unity's EventSystem can automatically select the Confirm button on
        // a newly-created survey. Prefer the active slider explicitly so the
        // first horizontal thumbstick movement changes its value. Once A has
        // moved focus to Confirm, keep that button selected for submission.
        Selectable firstSlider = FindFirstUsableSlider();
        if (firstSlider != null)
        {
            bool selectedIsSlider = selected is Slider;
            bool selectedIsConfirm = IsConfirmControl(selected) &&
                                     CanvasFor(selected) == CanvasFor(firstSlider);
            if (!IsUsable(selected) ||
                (CanvasFor(selected) != CanvasFor(firstSlider) && !selectedIsConfirm) ||
                (!selectedIsSlider && !selectedIsConfirm))
            {
                Select(eventSystem, firstSlider);
            }
            else if (selectedIsSlider && eventSystem.currentSelectedGameObject != selected.gameObject)
            {
                Select(eventSystem, selected);
            }
            return;
        }

        // Once a simulation/feedback screen is finished, its Continue button
        // becomes interactable. Give it focus explicitly so the right A
        // button can confirm it without requiring a ray or poke interaction.
        Selectable continueButton = FindFirstUsableContinueButton();
        if (continueButton != null)
        {
            if (!IsUsable(selected) || !IsContinueButton(selected) ||
                CanvasFor(selected) != CanvasFor(continueButton))
            {
                Select(eventSystem, continueButton);
            }
            else if (eventSystem.currentSelectedGameObject != selected.gameObject)
            {
                Select(eventSystem, selected);
            }
            return;
        }

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

        Select(eventSystem, selected);
    }

    static Selectable FindFirstUsableSelectable()
    {
        Selectable questionControl = FindFirstQuestionControl();
        if (questionControl != null)
            return questionControl;

        // Confidence and General Discomfort screens both open with their
        // slider as the active control, so the first horizontal thumbstick
        // movement changes its value immediately.
        Selectable slider = FindFirstUsableSlider();
        if (slider != null)
            return slider;

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

    static Selectable FindFirstQuestionControl()
    {
        Selectable[] all = Selectable.allSelectablesArray;
        Selectable first = null;
        int firstIndex = int.MaxValue;
        foreach (Selectable candidate in all)
        {
            if (!IsUsable(candidate) || !IsQuestionControl(candidate))
                continue;

            int index = QuestionControlIndex(candidate);
            if (first == null || index < firstIndex)
            {
                first = candidate;
                firstIndex = index;
            }
        }

        return first;
    }

    static Selectable FindFirstUsableSlider()
    {
        Selectable best = null;
        int bestSortingOrder = int.MinValue;
        foreach (Selectable candidate in Selectable.allSelectablesArray)
        {
            if (!(candidate is Slider) || !IsUsable(candidate))
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

    static Selectable FindFirstUsableContinueButton()
    {
        foreach (Selectable candidate in Selectable.allSelectablesArray)
            if (IsContinueButton(candidate) && IsUsable(candidate))
                return candidate;
        return null;
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
        if (slider == null && horizontal && FindFirstQuestionControl() == null)
        {
            // Keep slider input reliable even if Unity temporarily moved the
            // EventSystem focus to the Confirm button.
            slider = FindFirstUsableSlider() as Slider;
            if (slider != null)
                Select(eventSystem, slider);
        }

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

        // Question controls are a dedicated vertical list. Navigate only in
        // that list, so the thumbstick moves A-D and then Repeat scenario.
        if (IsQuestionControl(selected))
        {
            if (!horizontal)
                MoveQuestionSelection(eventSystem, direction);
            return;
        }

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

        Select(eventSystem, best);
    }

    static void MoveQuestionSelection(EventSystem eventSystem, float direction)
    {
        List<Selectable> controls = new List<Selectable>();
        Canvas canvas = CanvasFor(selected);
        foreach (Selectable candidate in Selectable.allSelectablesArray)
        {
            if (IsUsable(candidate) && IsQuestionControl(candidate) && CanvasFor(candidate) == canvas)
                controls.Add(candidate);
        }

        controls.Sort((left, right) => QuestionControlIndex(left).CompareTo(QuestionControlIndex(right)));
        int currentIndex = controls.IndexOf(selected);
        if (currentIndex < 0 || controls.Count == 0)
            return;

        // Thumbstick down (negative Y) moves from A to B and eventually to
        // Repeat; up moves back through the same list.
        int nextIndex = currentIndex + (direction < 0f ? 1 : -1);
        if (nextIndex < 0 || nextIndex >= controls.Count)
            return;

        Select(eventSystem, controls[nextIndex]);
    }

    static void Select(EventSystem eventSystem, Selectable next)
    {
        if (!IsUsable(next))
            return;

        selected = next;
        selectedCanvas = CanvasFor(selected);
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

    static bool IsAnswerButton(Selectable candidate)
    {
        return candidate is Button &&
               candidate.name.StartsWith("Answer_", StringComparison.OrdinalIgnoreCase);
    }

    static bool IsRepeatButton(Selectable candidate)
    {
        if (!(candidate is Button) || !IsUsable(candidate))
            return false;

        if (candidate.name.Equals("Repeat", StringComparison.OrdinalIgnoreCase) ||
            candidate.name.Equals("RepeatScenario", StringComparison.OrdinalIgnoreCase))
            return true;

        TMP_Text label = candidate.GetComponentInChildren<TMP_Text>(true);
        return label != null && label.text.IndexOf("Repeat", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    static bool IsQuestionControl(Selectable candidate)
    {
        return IsAnswerButton(candidate) || IsRepeatButton(candidate);
    }

    static bool IsConfirmControl(Selectable candidate)
    {
        if (!(candidate is Button) || !IsUsable(candidate))
            return false;

        if (candidate.name.Equals("Confirm", StringComparison.OrdinalIgnoreCase) ||
            candidate.name.Equals("ConfirmConfidence", StringComparison.OrdinalIgnoreCase))
            return true;

        TMP_Text label = candidate.GetComponentInChildren<TMP_Text>(true);
        return label != null && label.text.IndexOf("Confirm", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    static bool IsContinueButton(Selectable candidate)
    {
        if (!(candidate is Button) || !IsUsable(candidate))
            return false;

        if (candidate.name.Equals("Continue", StringComparison.OrdinalIgnoreCase) ||
            candidate.name.Equals("ContinueAfterFeedback", StringComparison.OrdinalIgnoreCase))
            return true;

        TMP_Text label = candidate.GetComponentInChildren<TMP_Text>(true);
        return label != null && label.text.IndexOf("Continue", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    static int AnswerIndex(Selectable candidate)
    {
        if (candidate == null) return int.MaxValue;
        string suffix = candidate.name.Substring("Answer_".Length);
        if (int.TryParse(suffix, out int numeric)) return numeric;
        if (suffix.Length == 1)
        {
            char letter = char.ToUpperInvariant(suffix[0]);
            if (letter >= 'A' && letter <= 'Z') return letter - 'A';
        }
        return int.MaxValue;
    }

    static int QuestionControlIndex(Selectable candidate)
    {
        if (IsAnswerButton(candidate))
            return AnswerIndex(candidate);
        return IsRepeatButton(candidate) ? 4 : int.MaxValue;
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
