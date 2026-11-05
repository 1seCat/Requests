namespace Requests.Application.DTOs;
public sealed record UpdateStatusRequest(string Status, string RowVersion);