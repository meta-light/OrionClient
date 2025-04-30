namespace OrionEventLib.Events.Mining
{
    public class MiningPauseEvent : MiningEvent
    {
        public override SubEventTypes SubEventType => SubEventTypes.Pause;

    }
}
