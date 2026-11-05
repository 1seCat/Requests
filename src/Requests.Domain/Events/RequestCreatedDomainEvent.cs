namespace Requests.Domain.Events;
public sealed record RequestCreatedDomainEvent(
    long RequestId, string ExternalId, string Title, string Status);
    