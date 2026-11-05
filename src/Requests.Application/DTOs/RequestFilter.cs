using Requests.Domain.Enums;

namespace Requests.Application.DTOs;

public sealed record RequestFilter(
    RequestStatus? Status,
    int Page,
    int PageSize,
    string SortBy,
    string SortDirection);