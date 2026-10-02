using System;

namespace GridLookout.Licensing;

/// <summary>
/// RFC 4648 section 5 base64url, no padding - the encoding used throughout the licence envelope
/// contract (see <see cref="LicenseEnvelope"/>'s own doc comment). Ported from
/// InfraAtlas.Services.ProductLicenseService's private Base64UrlEncode/Decode helpers - promoted to
/// a standalone, independently-testable static class here since GridLookout has no equivalent
/// ambient JSON-licence-encoding home to hang it off.
/// </summary>
public static class Base64Url
{
    public static string Encode(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static byte[] Decode(string value)
    {
        if (value is null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        var padded = value.Replace('-', '+').Replace('_', '/');
        int remainder = padded.Length % 4;
        if (remainder != 0)
        {
            padded = padded.PadRight(padded.Length + (4 - remainder), '=');
        }

        return Convert.FromBase64String(padded);
    }
}
