using Khylo.Controller.Authentication;
using Xunit;

namespace Khylo.Api.Tests.Helpers;

public class PasswordPolicyTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("        ")]
    [InlineData("\t\t\t\t\t\t\t\t")]
    public void ABlankPassword_IsRefused(string? password)
    {
        Assert.Equal("Choose a password.", PasswordPolicy.Problem(password));
    }

    [Theory]
    [InlineData("1")]
    [InlineData("short")]
    [InlineData("1234567")]
    public void AShortPassword_IsRefused(string password)
    {
        Assert.Equal("Passwords need at least 8 characters.", PasswordPolicy.Problem(password));
    }

    [Theory]
    [InlineData("password")]
    [InlineData("PASSWORD123")]
    [InlineData("12345678")]
    [InlineData("aaaaaaaaaa")]
    [InlineData("11111111")]
    public void AnEasyPassword_IsRefused(string password)
    {
        Assert.Equal("That password is too easy to guess. Choose another.", PasswordPolicy.Problem(password));
    }

    [Fact]
    public void ThePasswordCannotBeTheUserName()
    {
        Assert.NotNull(PasswordPolicy.Problem("somebody1", "Somebody1"));
        Assert.Null(PasswordPolicy.Problem("somebody1", "somebody"));
    }

    [Theory]
    [InlineData("correct horse battery")]
    [InlineData("8characters")]
    [InlineData("  spaces are kept  ")]
    [InlineData("Dörfer-Mädchen-42")]
    public void AnOrdinaryPassword_IsFine(string password)
    {
        Assert.Null(PasswordPolicy.Problem(password));
    }
}
