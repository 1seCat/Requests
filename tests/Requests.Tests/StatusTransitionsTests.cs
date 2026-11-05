using Requests.Domain.Enums;
using Requests.Domain.Rules;
using Xunit;

public class StatusTransitionsTests
{
    [Theory]
    [InlineData(RequestStatus.New, RequestStatus.InProgress, true)]
    [InlineData(RequestStatus.New, RequestStatus.Completed, false)]
    [InlineData(RequestStatus.InProgress, RequestStatus.Completed, true)]
    [InlineData(RequestStatus.Completed, RequestStatus.InProgress, false)]
    [InlineData(RequestStatus.Rejected, RequestStatus.New, false)]
    [InlineData(RequestStatus.Cancelled, RequestStatus.New, false)]
    public void Transition_rules(RequestStatus from, RequestStatus to, bool expected)
        => Assert.Equal(expected, StatusTransitions.IsAllowed(from, to));
}