namespace Requests.Infrastructure.Persistence.Entities;
public sealed class ProcessedMessage
{
    public Guid EventId { get; set; }
    public DateTime ProcessedAt { get; set; }
}