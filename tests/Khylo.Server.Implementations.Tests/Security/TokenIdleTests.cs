using System;
using Khylo.Server.Implementations.Security;
using Xunit;

namespace Khylo.Server.Implementations.Tests.Security;

public class TokenIdleTests
{
    private static readonly DateTime Now = new(2026, 11, 9, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(0, false)]
    [InlineData(3, false)]
    [InlineData(89, false)]
    [InlineData(90, false)]
    [InlineData(91, true)]
    [InlineData(400, true)]
    public void AKeyUnusedForMoreThanNinetyDays_StopsWorking(int daysIdle, bool tooLong)
    {
        Assert.Equal(tooLong, AuthorizationContext.IsIdleTooLong(Now.AddDays(-daysIdle), Now));
    }

    [Fact]
    public void ALimitOfNinetyDays_IsWhatTheAppsAreToldAbout()
    {
        Assert.Equal(TimeSpan.FromDays(90), AuthorizationContext.TokenIdleLimit);
    }
}
