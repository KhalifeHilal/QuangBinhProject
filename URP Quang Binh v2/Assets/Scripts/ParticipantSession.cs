using UnityEngine;

public enum ParticipantQuestionSet { A, B, C, D }
public enum ParticipantInteractionMode { Controller, Gamepad, Virtual_Hands }

/// <summary>Stores the checked-in participant configuration for the current run.</summary>
public static class ParticipantSession
{
    public static bool IsCheckedIn { get; private set; }
    public static int Number { get; private set; }
    public static ParticipantQuestionSet QuestionSet { get; private set; }

    // Repeats question positions 1..12 for any participant number.
    public static int QuestionIndex => Number <= 0 ? 0 : ((Number - 1) % 12) + 1;

    // Participants 1-30 are the primary slots. 31-36 are two reserve slots
    // for each condition. The complete six-slot reserve cycle repeats for
    // larger participant IDs so additional replacements keep a stable mode.
    public static ParticipantInteractionMode InteractionMode
    {
        get
        {
            int slot = Number <= 0 ? 0 : ((Number - 1) % 36) + 1;
            if (slot >= 1 && slot <= 10) return ParticipantInteractionMode.Controller;
            if (slot >= 31 && slot <= 32) return ParticipantInteractionMode.Controller;
            if (slot >= 11 && slot <= 20) return ParticipantInteractionMode.Gamepad;
            if (slot >= 33 && slot <= 34) return ParticipantInteractionMode.Gamepad;
            return ParticipantInteractionMode.Virtual_Hands;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetForNewRun()
    {
        IsCheckedIn = false;
        Number = 0;
        QuestionSet = ParticipantQuestionSet.A;
    }

    public static void CheckIn(int participantNumber, ParticipantQuestionSet questionSet)
    {
        Number = Mathf.Clamp(participantNumber, 1, 99);
        QuestionSet = questionSet;
        IsCheckedIn = true;
        Debug.Log($"Participant check-in: {Number:00}, set {QuestionSet}, question index {QuestionIndex}, interaction {InteractionMode}");

        // Apply Gamepad-specific tutorial instructions immediately after the
        // condition is selected. This runs before the tutorial panels are
        // restored, so inactive TextMeshPro labels are included as well.
        if (InteractionMode == ParticipantInteractionMode.Gamepad)
            GamepadUIInput.RefreshTutorialInstructions();
    }
}
