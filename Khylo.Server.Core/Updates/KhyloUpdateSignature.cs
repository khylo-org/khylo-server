using System;
using System.Security.Cryptography;

namespace Khylo.Server.Core.Updates;

/// <summary>
/// Checks that a release's checksum list was signed by Khylo. The list says which installer is the real one, so
/// before anything is installed the list itself has to carry a signature made with Khylo's private key, which
/// never leaves the build computer. Somebody who only controls the release page can't make one.
/// </summary>
public static class KhyloUpdateSignature
{
    /// <summary>
    /// Khylo's public update key (ECDSA, curve P-256). The matching private key signs each release's checksum list
    /// when the release is published (Servers/sign-checksums.sh).
    /// </summary>
    public const string PublicKeyPem = """
        -----BEGIN PUBLIC KEY-----
        MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAE42TDd4zNbR2Z5mDNzCbW3BnrcMBU
        gFgmRmjL7iB3SE9TySqu/UQtmwbN3w7HDBNjSafEkYqo2zEziih/5YxBDA==
        -----END PUBLIC KEY-----
        """;

    /// <summary>
    /// Checks a signature against Khylo's own key.
    /// </summary>
    /// <param name="signed">The exact bytes that were signed.</param>
    /// <param name="signature">The signature (DER, as made by openssl).</param>
    /// <returns>True only when the signature is Khylo's.</returns>
    public static bool Verify(ReadOnlySpan<byte> signed, ReadOnlySpan<byte> signature)
        => Verify(signed, signature, PublicKeyPem);

    /// <summary>
    /// Checks a signature against a given public key.
    /// </summary>
    /// <param name="signed">The exact bytes that were signed.</param>
    /// <param name="signature">The signature (DER, as made by openssl).</param>
    /// <param name="publicKeyPem">The public key in PEM form.</param>
    /// <returns>True only when the signature was made with the matching private key.</returns>
    public static bool Verify(ReadOnlySpan<byte> signed, ReadOnlySpan<byte> signature, string publicKeyPem)
    {
        if (signed.IsEmpty || signature.IsEmpty)
        {
            return false;
        }

        try
        {
            using var key = ECDsa.Create();
            key.ImportFromPem(publicKeyPem);
            return key.KeySize == 256
                && key.VerifyData(signed, signature, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
        }
        catch (Exception ex) when (ex is CryptographicException or ArgumentException)
        {
            return false;
        }
    }
}
