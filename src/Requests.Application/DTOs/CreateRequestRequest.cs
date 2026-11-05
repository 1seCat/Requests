namespace Requests.Application.DTOs;
public sealed record CreateRequestRequest(string ExternalId, string Title, string? Description);