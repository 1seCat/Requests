using Requests.Domain.Enums;

namespace Requests.Domain.Rules;

public static class StatusTransitions
{
    private static readonly IReadOnlyDictionary<RequestStatus, RequestStatus[]> Map =
        new Dictionary<RequestStatus, RequestStatus[]>
        {
            [RequestStatus.New]        = new[] { RequestStatus.InProgress, RequestStatus.Rejected, RequestStatus.Cancelled },
            [RequestStatus.InProgress] = new[] { RequestStatus.Completed, RequestStatus.Rejected, RequestStatus.Cancelled },
            [RequestStatus.Completed]  = Array.Empty<RequestStatus>(),
            [RequestStatus.Rejected]   = Array.Empty<RequestStatus>(),
            [RequestStatus.Cancelled]  = Array.Empty<RequestStatus>(),
        };

    public static bool IsAllowed(RequestStatus from, RequestStatus to)
        => Map.TryGetValue(from, out var allowed) && allowed.Contains(to);

    public static IReadOnlyList<RequestStatus> AllowedFrom(RequestStatus from)
        => Map.TryGetValue(from, out var allowed) ? allowed : Array.Empty<RequestStatus>();
}