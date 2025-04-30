namespace OrionEventLib.Events.Mining
{
    public abstract class MiningEvent : OrionEvent
    {
        public override EventTypes EventType => EventTypes.Mining;
    }
}
