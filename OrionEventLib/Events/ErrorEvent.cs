﻿namespace OrionEventLib.Events
{
    public class ErrorEvent : OrionEvent
    {
        public override EventTypes EventType => EventTypes.Error;
        public override SubEventTypes SubEventType => SubEventTypes.None;

        public required string ErrorMessage { get; set; }
        public required bool ExecutionStopped { get; set; }
    }
}
