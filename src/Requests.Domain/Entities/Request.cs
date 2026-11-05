using Requests.Domain.Enums;
using Requests.Domain.Exceptions;
using Requests.Domain.Rules;

namespace Requests.Domain.Entities;

public sealed class Request
{
    public long Id { get; private set; }
    public string ExternalId { get; private set; } = null!;
    public string Title { get; private set; } = null!;
    public string? Description { get; private set; }
    public RequestStatus Status { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }
    public byte[] RowVersion { get; private set; } = null!;

    private Request() { } // EF

    public static Request Create(string externalId, string title, string? description, DateTime utcNow)
    {
        if (string.IsNullOrWhiteSpace(externalId)) throw new ArgumentException("ExternalId is required.", nameof(externalId));
        if (externalId.Trim().Length > 100) throw new ArgumentException("ExternalId max 100.", nameof(externalId));
        if (string.IsNullOrWhiteSpace(title)) throw new ArgumentException("Title is required.", nameof(title));
        if (title.Trim().Length > 200) throw new ArgumentException("Title max 200.", nameof(title));
        if (description is { Length: > 2000 }) throw new ArgumentException("Description max 2000.", nameof(description));

        return new Request
        {
            ExternalId = externalId.Trim(),
            Title = title.Trim(),
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            Status = RequestStatus.New,
            CreatedAt = utcNow,
            UpdatedAt = utcNow
        };
    }

    public void ChangeStatus(RequestStatus newStatus, DateTime utcNow)
    {
        if (!StatusTransitions.IsAllowed(Status, newStatus))
            throw new InvalidStatusTransitionException(Status, newStatus);

        Status = newStatus;
        UpdatedAt = utcNow;
    }
}