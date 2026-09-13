using UnityEngine;

public enum ParticipantQuestionSet { A, B, C, D }
public enum ParticipantInteractionMode { ControllerBuild, UIPlacement, CurrentRayAndTrigger }

/// <summary>Stores the checked-in participant configuration for the current run.</summary>
public static class ParticipantSession
{
    public static bool IsCheckedIn { get; private set; }
    public static int Number { get; private set; }
    public static ParticipantQuestionSet QuestionSet { get; private set; }

    // Repeats question positions 1..12 for any participant number.
    public static int QuestionIndex => Number <= 0 ? 0 : ((Number - 1) % 12) + 1;

    // Repeats the three interaction cohorts every 36 participants.
    public static ParticipantInteractionMode InteractionMode
    {
        get
        {
            int cyclePosition = Number <= 0 ? 0 : (Number - 1) % 36;
            if (cyclePosition < 12) return ParticipantInteractionMode.ControllerBuild;
            if (cyclePosition < 24) return ParticipantInteractionMode.UIPlacement;
            return ParticipantInteractionMode.CurrentRayAndTrigger;
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
    }
}
