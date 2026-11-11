using System;
using System.Security.Cryptography;
using System.Text;
using Khylo.Server.Core.Updates;
using Xunit;

namespace Khylo.Server.Implementations.Tests.Updates;

public class KhyloUpdateSignatureTests
{
    // Made with openssl (the tool the publishing script uses) and a throwaway key: proves the format openssl writes is the one the server reads
    private const string OpenSslPublicKey = """
        -----BEGIN PUBLIC KEY-----
        MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEib/tF3oziXtuxNkLiNeRWITJcji5
        bDrl0FQXW/9yt57UYsGDMrYZ5Flft0AV/pcOvJtAoubdl79+pMbK3o8hGw==
        -----END PUBLIC KEY-----
        """;

    private const string OpenSslSignature = "MEQCICV5WDs5K7gjSiSQQLocjwZ+ZvWLMz/9e68Wid6RbkTJAiB05XuFn/1AhGXn+4V3NMB2DBCKXh0SKF1cjdqI3T7F4Q==";

    private static readonly byte[] _openSslList = Encoding.ASCII.GetBytes("abc123  khylo-server-1.0-windows-x64-setup.exe\n");

    [Fact]
    public void ASignatureMadeByOpenssl_Verifies()
    {
        Assert.True(KhyloUpdateSignature.Verify(_openSslList, Convert.FromBase64String(OpenSslSignature), OpenSslPublicKey));
    }

    [Fact]
    public void ASignatureMadeByOpenssl_FailsWhenOneByteOfTheListChanges()
    {
        var changed = (byte[])_openSslList.Clone();
        changed[0] ^= 1;

        Assert.False(KhyloUpdateSignature.Verify(changed, Convert.FromBase64String(OpenSslSignature), OpenSslPublicKey));
    }

    [Fact]
    public void ASignatureOfOurOwn_VerifiesWithItsKey_AndFailsWithAnother()
    {
        using var signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var list = Encoding.UTF8.GetBytes("a  b\n");
        var signature = signer.SignData(list, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);

        Assert.True(KhyloUpdateSignature.Verify(list, signature, signer.ExportSubjectPublicKeyInfoPem()));
        Assert.False(KhyloUpdateSignature.Verify(list, signature, other.ExportSubjectPublicKeyInfoPem()));
    }

    [Fact]
    public void AnAlteredSignature_Fails()
    {
        var signature = Convert.FromBase64String(OpenSslSignature);
        signature[^1] ^= 1;

        Assert.False(KhyloUpdateSignature.Verify(_openSslList, signature, OpenSslPublicKey));
    }

    [Fact]
    public void AnEmptyListOrAnEmptySignature_Fails()
    {
        Assert.False(KhyloUpdateSignature.Verify(_openSslList, [], OpenSslPublicKey));
        Assert.False(KhyloUpdateSignature.Verify([], Convert.FromBase64String(OpenSslSignature), OpenSslPublicKey));
    }

    [Fact]
    public void AKeyThatIsNotAKey_FailsInsteadOfThrowing()
    {
        Assert.False(KhyloUpdateSignature.Verify(_openSslList, Convert.FromBase64String(OpenSslSignature), "not a key"));
    }

    [Fact]
    public void AKeyOfTheWrongSize_Fails()
    {
        using var big = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        var signature = big.SignData(_openSslList, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);

        Assert.False(KhyloUpdateSignature.Verify(_openSslList, signature, big.ExportSubjectPublicKeyInfoPem()));
    }

    // A wrong key pasted into the code would make every later update refuse to install, so the real one is checked here
    [Fact]
    public void KhylosOwnKey_IsAP256PublicKey_AndRejectsAnythingNotSignedWithIt()
    {
        using var key = ECDsa.Create();
        key.ImportFromPem(KhyloUpdateSignature.PublicKeyPem);
        Assert.Equal(256, key.KeySize);
        Assert.Equal("1.2.840.10045.3.1.7", key.ExportParameters(false).Curve.Oid.Value);

        Assert.False(KhyloUpdateSignature.Verify(_openSslList, Convert.FromBase64String(OpenSslSignature)));
    }

    [Theory]
    [InlineData("DEV-V26.11.08.11", true)]
    [InlineData("V26.11.08.11", true)]
    [InlineData("DEV-V26.10.101", true)]
    [InlineData("V26.10.101", true)]
    [InlineData("DEV-V26.11.08.11'; Remove-Item C:\\ -Recurse; '", false)]
    [InlineData("DEV-V26.11.08.11\n", false)]
    [InlineData("DEV-V26.11.08.11 ", false)]
    [InlineData("v26.11.08.11", false)]
    [InlineData("DEV-V26.11.08.11\\..\\evil", false)]
    [InlineData("..", false)]
    [InlineData("26.11.08.11", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void OnlyARealBuildNumber_IsAValidReleaseName(string? tag, bool valid)
    {
        Assert.Equal(valid, KhyloReleaseFeed.IsValidTag(tag));
    }
}
