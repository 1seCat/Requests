namespace Requests.Application.DTOs;
public sealed record RequestDto(
    long Id, string ExternalId, string Title, string? Description,
    string Status, DateTime CreatedAt, DateTime UpdatedAt, string RowVersion);