using System;
using System.Text;
using GridLookout.Licensing;
using Xunit;

namespace GridLookout.Tests.Licensing;

public class Base64UrlTests
{
    [Theory]
    [InlineData("")]
    [InlineData("f")]
    [InlineData("fo")]
    [InlineData("foo")]
    [InlineData("foob")]
    [InlineData("fooba")]
    [InlineData("foobar")]
    public void EncodeThenDecode_RoundTrips_AtEveryPaddingLength(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        var encoded = Base64Url.Encode(bytes);
        var decoded = Base64Url.Decode(encoded);

        Assert.Equal(bytes, decoded);
    }

    [Fact]
    public void Encode_NeverContainsPaddingOrUrlUnsafeCharacters()
    {
        // Bytes chosen so the standard base64 alphabet would emit both '+' and '/' and need padding.
        var bytes = new byte[] { 0xFB, 0xFF, 0xBF, 0xEF, 0x3E };
        var encoded = Base64Url.Encode(bytes);

        Assert.DoesNotContain('+', encoded);
        Assert.DoesNotContain('/', encoded);
        Assert.DoesNotContain('=', encoded);
    }

    [Fact]
    public void Decode_AcceptsUrlSafeCharactersStandardBase64Would_Reject()
    {
        var bytes = new byte[] { 0xFB, 0xFF, 0xBF, 0xEF, 0x3E };
        var encoded = Base64Url.Encode(bytes);

        // Round trip through the SAME encoding - proves Decode handles both '-'/'_' substitution and
        // the missing-padding case together, exactly like a real envelope field would arrive.
        var decoded = Base64Url.Decode(encoded);
        Assert.Equal(bytes, decoded);
    }

    [Fact]
    public void Decode_InvalidBase64_ThrowsFormatException()
    {
        Assert.Throws<FormatException>(() => Base64Url.Decode("not-valid-base64!!!"));
    }
}
