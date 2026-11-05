namespace Requests.Infrastructure.Persistence.Entities;
public sealed class OutboxMessage
{
    public Guid Id { get; set; }
    public string EventType { get; set; } = null!;
    public string Payload { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
    public DateTime? PublishedAt { get; set; }
    public DateTime? ClaimedAt { get; set; }            // added for recovery
    public int RetryCount { get; set; }
    public string Status { get; set; } = "Pending";     // Pending|Processing|Published|Failed
    public string? ErrorMessage { get; set; }
}