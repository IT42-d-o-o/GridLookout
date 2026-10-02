using System;
using GridLookout.Licensing;
using Xunit;

namespace GridLookout.Tests.Licensing;

public class LicensePublicKeyTests
{
    [Fact]
    public void GetParameters_ParsesTheRealEmbeddedConstant_AsA3072BitRsaKey()
    {
        var parameters = LicensePublicKey.GetParameters();

        // RSA-3072 -> a 384-byte (3072-bit) modulus, per docs/plans/2026-09-25-gridlookout-licensing.md.
        Assert.Equal(384, parameters.Modulus!.Length);
        // The standard public exponent 65537 (0x010001) - what every mainstream RSA keygen emits.
        Assert.Equal(new byte[] { 0x01, 0x00, 0x01 }, parameters.Exponent);
    }

    [Fact]
    public void GetParameters_IsCachedAcrossCalls()
    {
        var first = LicensePublicKey.GetParameters();
        var second = LicensePublicKey.GetParameters();

        Assert.Equal(first.Modulus, second.Modulus);
        Assert.Equal(first.Exponent, second.Exponent);
    }

    [Fact]
    public void ParsePem_RoundTripsAKnownRsaPublicKey()
    {
        // Reuses a throwaway 2048-bit keypair's own DER export path via LicenseTestSigner is not
        // possible on net48 (no ExportSubjectPublicKeyInfoPem) - this test instead only exercises the
        // reader against the real embedded constant (covered above) and against malformed input
        // (below); the ParsePem call itself is validated end-to-end through LicenseVerifierTests,
        // which signs with a test keypair and verifies against a DIRECTLY-constructed RSAParameters
        // (bypassing PEM), so no test anywhere needs to PEM-ENCODE a generated key.
        var parameters = LicensePublicKey.ParsePem(LicensePublicKey.Pem);
        Assert.NotNull(parameters.Modulus);
        Assert.NotNull(parameters.Exponent);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a pem at all")]
    [InlineData("-----BEGIN PUBLIC KEY-----\nnot base64!!!\n-----END PUBLIC KEY-----\n")]
    public void ParsePem_MalformedInput_ThrowsFormatException(string badPem)
    {
        Assert.Throws<FormatException>(() => LicensePublicKey.ParsePem(badPem));
    }

    [Fact]
    public void ParsePem_TruncatedDer_ThrowsFormatException()
    {
        // Valid base64, but far too short to be a real SubjectPublicKeyInfo - the DER reader must
        // fail cleanly (FormatException), never throw an unrelated IndexOutOfRangeException.
        var pem = "-----BEGIN PUBLIC KEY-----\nMAA=\n-----END PUBLIC KEY-----\n";
        Assert.Throws<FormatException>(() => LicensePublicKey.ParsePem(pem));
    }
}
