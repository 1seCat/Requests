using System.Text.Json;
using Requests.Application.Interfaces;
using Requests.Infrastructure.Persistence;
using Requests.Infrastructure.Persistence.Entities;

namespace Requests.Infrastructure.Repositories;

public sealed class EfOutboxWriter : IOutboxWriter
{
    private readonly AppDbContext _db;
    public EfOutboxWriter(AppDbContext db) => _db = db;

    public async Task EnqueueAsync(string eventType, object payload, Guid eventId, Guid correlationId, CancellationToken ct)
    {
        var envelope = new
        {
            eventId,
            eventType,
            occurredAt = DateTime.UtcNow.ToString("O"),
            version = 1,
            correlationId,
            data = payload
        };
        var json = JsonSerializer.Serialize(envelope, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        await _db.OutboxMessages.AddAsync(new OutboxMessage
        {
            Id = eventId,
            EventType = eventType,
            Payload = json,
            CreatedAt = DateTime.UtcNow,
            Status = "Pending",
            RetryCount = 0
        }, ct);
    }
}