using Requests.Domain.Enums;
namespace Requests.Domain.Exceptions;
public sealed class InvalidStatusTransitionException : Exception
{
    public RequestStatus From { get; }
    public RequestStatus To { get; }
    public InvalidStatusTransitionException(RequestStatus from, RequestStatus to)
        : base($"Status transition {from} -> {to} is not allowed.") { From = from; To = to; }
}