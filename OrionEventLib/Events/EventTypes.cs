namespace OrionEventLib.Events
{
    public enum EventTypes { Unknown, Mining, Error };
    public enum SubEventTypes
    {
        None,
        HashrateUpdate, Start, Pause, NewChallenge, SubmissionResult, DifficultySubmission //Mining Events
    };
}
