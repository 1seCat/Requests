public sealed record RequestStatusChangedDomainEvent(
    long RequestId, string OldStatus, string NewStatus);