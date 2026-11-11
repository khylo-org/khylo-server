using System;
using Khylo.Api.Helpers;
using Xunit;

namespace Khylo.Api.Tests.Helpers;

// The throttle remembers things between calls, so every test uses account names and addresses of its own
public class LoginThrottleTests
{
    private static string Account() => "acct-" + Guid.NewGuid().ToString("N");

    [Fact]
    public void TenWrongGuessesFromOneAddress_StopThatAddress_NotAnotherOne()
    {
        var account = Account();
        for (var i = 0; i < 10; i++)
        {
            LoginThrottle.RecordFailure(account, "198.51.100.10");
        }

        Assert.NotNull(LoginThrottle.RetryAfter(account, "198.51.100.10"));
        Assert.Null(LoginThrottle.RetryAfter(account, "198.51.100.11"));
    }

    [Fact]
    public void AStrangerGuessingAtTheOwnersAccount_DoesNotKeepTheOwnerOut()
    {
        var account = Account();
        for (var i = 0; i < 10; i++)
        {
            LoginThrottle.RecordFailure(account, "203.0.113.50");
        }

        Assert.NotNull(LoginThrottle.RetryAfter(account, "203.0.113.50"));
        Assert.Null(LoginThrottle.RetryAfter(account, "192.168.1.20"));
    }

    [Fact]
    public void ASuccessClearsTheCountForThatAddress()
    {
        var account = Account();
        for (var i = 0; i < 9; i++)
        {
            LoginThrottle.RecordFailure(account, "198.51.100.20");
        }

        LoginThrottle.RecordSuccess(account, "198.51.100.20");
        LoginThrottle.RecordFailure(account, "198.51.100.20");

        Assert.Null(LoginThrottle.RetryAfter(account, "198.51.100.20"));
    }

    [Fact]
    public void ManyAddressesGuessing_StopAddressesThatNeverSignedIn_ButNotOneThatDid()
    {
        var account = Account();
        LoginThrottle.RecordSuccess(account, "192.168.1.30");

        // 12 addresses, 9 wrong guesses each: none is stopped by its own count, but together they are 108
        for (var address = 1; address <= 12; address++)
        {
            for (var i = 0; i < 9; i++)
            {
                LoginThrottle.RecordFailure(account, "198.51.101." + address);
            }
        }

        Assert.NotNull(LoginThrottle.RetryAfter(account, "198.51.102.1"));
        Assert.Null(LoginThrottle.RetryAfter(account, "192.168.1.30"));
    }

    [Fact]
    public void APin_IsStoppedForEveryone_AfterTenWrongGuesses()
    {
        var key = "pin:" + Guid.NewGuid().ToString("N");
        for (var address = 1; address <= 10; address++)
        {
            LoginThrottle.RecordFailure(key, "198.51.103." + address);
        }

        Assert.NotNull(LoginThrottle.RetryAfter(key, "198.51.104.1", pin: true));
        Assert.Null(LoginThrottle.RetryAfter(key, "198.51.104.1"));
    }

    [Fact]
    public void GuessesAtNamesThatDontExist_OnlyCountAgainstTheAddress()
    {
        var address = "198.51.105.5";
        var made = Account();
        for (var i = 0; i < 10; i++)
        {
            LoginThrottle.RecordFailure(made, address, accountExists: false);
        }

        Assert.Null(LoginThrottle.RetryAfter(made, address));

        for (var i = 0; i < 20; i++)
        {
            LoginThrottle.RecordFailure(Account(), address, accountExists: false);
        }

        Assert.NotNull(LoginThrottle.RetryAfter(Account(), address));
    }

    [Fact]
    public void AnIpv6Address_IsCountedWithItsWholeNetwork()
    {
        var account = Account();
        for (var i = 0; i < 10; i++)
        {
            LoginThrottle.RecordFailure(account, "2001:db8:aaaa:bbbb::1");
        }

        Assert.NotNull(LoginThrottle.RetryAfter(account, "2001:db8:aaaa:bbbb:1234::99"));
        Assert.Null(LoginThrottle.RetryAfter(account, "2001:db8:aaaa:cccc::1"));
    }

    [Fact]
    public void AnIpv4AddressInIpv6Form_IsTheSameAddress()
    {
        var account = Account();
        for (var i = 0; i < 10; i++)
        {
            LoginThrottle.RecordFailure(account, "::ffff:198.51.100.77");
        }

        Assert.NotNull(LoginThrottle.RetryAfter(account, "198.51.100.77"));
    }
}
