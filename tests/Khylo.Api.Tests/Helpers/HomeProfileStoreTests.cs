using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Khylo.Api.Helpers;
using Xunit;

namespace Khylo.Api.Tests.Helpers;

public sealed class HomeProfileStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "khylo-pin-tests-" + Guid.NewGuid().ToString("N"));
    private readonly string _file;

    public HomeProfileStoreTests()
    {
        Directory.CreateDirectory(_dir);
        _file = Path.Combine(_dir, "khylo_home_profiles.json");
    }

    [Fact]
    public void RightPinPasses_WrongPinAndNoPinDoNot()
    {
        var store = new HomeProfileStore(_dir);
        var profile = Guid.NewGuid();
        store.SetPin(profile, "4821");

        Assert.True(store.CheckPin(profile, "4821"));
        Assert.False(store.CheckPin(profile, "4822"));
        Assert.False(store.CheckPin(profile, null));
        Assert.False(store.CheckPin(profile, string.Empty));
    }

    [Fact]
    public void ProfileWithoutAPin_Passes()
    {
        Assert.True(new HomeProfileStore(_dir).CheckPin(Guid.NewGuid(), null));
    }

    [Fact]
    public void SamePinOnTwoProfiles_IsStoredDifferently_AndNeverAsThePinOrAPlainHash()
    {
        var store = new HomeProfileStore(_dir);
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        store.SetPin(first, "4821");
        store.SetPin(second, "4821");

        var saved = File.ReadAllText(_file);
        Assert.Contains("pbkdf2$", saved, StringComparison.Ordinal);
        Assert.DoesNotContain("4821", saved, StringComparison.Ordinal);
        Assert.DoesNotContain(LegacyHash(first, "4821"), saved, StringComparison.Ordinal);
        Assert.NotEqual(StoredFor(first), StoredFor(second));
    }

    [Fact]
    public void PinSavedTheOldWay_StillWorks_AndIsKeptTheNewWayAfterwards()
    {
        var profile = Guid.NewGuid();
        File.WriteAllText(_file, $"{{\"Pins\":{{\"{profile}\":\"{LegacyHash(profile, "4821")}\"}}}}");
        var store = new HomeProfileStore(_dir);

        Assert.False(store.CheckPin(profile, "0000"));
        Assert.DoesNotContain("pbkdf2$", File.ReadAllText(_file), StringComparison.Ordinal);

        Assert.True(store.CheckPin(profile, "4821"));
        Assert.Contains("pbkdf2$", File.ReadAllText(_file), StringComparison.Ordinal);
        Assert.DoesNotContain(LegacyHash(profile, "4821"), File.ReadAllText(_file), StringComparison.Ordinal);
        Assert.True(store.CheckPin(profile, "4821"));
        Assert.False(store.CheckPin(profile, "0000"));
    }

    [Fact]
    public void ADamagedStoredPin_NeverLetsAnyoneIn()
    {
        var profile = Guid.NewGuid();
        File.WriteAllText(_file, $"{{\"Pins\":{{\"{profile}\":\"pbkdf2$nonsense$zz$zz\"}}}}");

        Assert.False(new HomeProfileStore(_dir).CheckPin(profile, "4821"));
    }

    [Theory]
    [InlineData("1234", true)]
    [InlineData("00000000", true)]
    [InlineData("123", false)]
    [InlineData("123456789", false)]
    [InlineData("12 4", false)]
    [InlineData("abcd", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void ValidPin_IsFourToEightDigits(string? pin, bool valid)
    {
        Assert.Equal(valid, HomeProfileStore.IsValidPin(pin));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, true);
        }
        catch (IOException)
        {
            // A temporary folder that could not be removed is not a failed test
        }
    }

    private static string LegacyHash(Guid userId, string pin)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(userId.ToString("N") + ":" + pin)));

    private string StoredFor(Guid profile)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(_file));
        return doc.RootElement.GetProperty("Pins").GetProperty(profile.ToString()).GetString()!;
    }
}
