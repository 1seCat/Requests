namespace Requests.Application.Interfaces;
public interface IOutboxWriter
{
    Task EnqueueAsync(string eventType, object payload, Guid eventId, Guid correlationId, CancellationToken ct);
}