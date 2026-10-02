using System;
using System.Security.Cryptography;
using System.Text;

namespace GridLookout.Licensing;

/// <summary>
/// Embedded RSA-3072 public key for licence-envelope signature verification (see
/// <see cref="LicenseEnvelope"/>'s own doc comment for the envelope/payload/signature contract this
/// key verifies). Public by design - the private key is held by the vendor's licence signer (the
/// web shop side) and is never present on a customer machine or in this program; see
/// docs/plans/2026-09-25-gridlookout-licensing.md "Risks" for why shipping the verifier and this
/// key in the open is an accepted trade-off, not an oversight.
///
/// .NET Framework 4.8's <see cref="RSA"/> base class has no ImportFromPem/ExportSubjectPublicKeyInfoPem
/// (those were added to the BCL in .NET 5+ and never backported to Framework - see this project's
/// TargetFramework comment in GridLookout.csproj for why GridLookout stays on net48) - so
/// <see cref="GetParameters"/> below parses the PEM's base64 DER body by hand instead of assuming
/// any higher-level PEM import exists. The DER shape is fixed and narrow
/// (SubjectPublicKeyInfo -&gt; BIT STRING -&gt; RSAPublicKey SEQUENCE of two INTEGERs), so a
/// small hand-rolled reader is simpler and lower-risk than adding a third-party ASN.1 package for
/// one call site.
/// </summary>
public static class LicensePublicKey
{
    public const string Pem =
        "-----BEGIN PUBLIC KEY-----\n" +
        "MIIBojANBgkqhkiG9w0BAQEFAAOCAY8AMIIBigKCAYEApgNkJGoOO1E0SEPXhfZz\n" +
        "LMKWN3y1qLMhrFLjmUuaFizlgMqnluN8XfD1wco+4P8x9TjCzvELjqRiawgnK38g\n" +
        "YyNYC/iG9FS/M6q/rl0vg/sgMjvSVfP293zx7GIbP0GmazzuEhElZbgezlsTqc3E\n" +
        "D/t5CksLoe8Q3OeJp3sc+vq1Q9r5c/1IwHCx4/ICvdHfH60BvIQUwJJJRTaCubMb\n" +
        "WdHLrnvreJ8kvx5LaKYplzwsIdfMHTQbOmdxbgWI6f9c8kcdExpo2bTMp2UY5pqU\n" +
        "XJJwJScBK3Cr+02ADDpkpMr22njSzJ2/pxyC6tsxdAzHmB7ilKBqA0O6edkP3Mly\n" +
        "gDvLYUeovMUAZXfNXlrafwf1t3JRDlYBnZuMEgQDnNcaWdxt6sEfriZmlXhQJUow\n" +
        "dwcMYCrEwC7/NWUBDPzbkuRrGFqY+N35ZduN5ZbQMtf5oDpm1QDRsBIiifxkE5f8\n" +
        "GLI4skOHSRGpj+b0ZhR0qWByUHeTLeSeGWksLBMXaGNXAgMBAAE=\n" +
        "-----END PUBLIC KEY-----\n";

    private static RSAParameters? _cached;

    /// <summary>Parses <see cref="Pem"/> into <see cref="RSAParameters"/>, caching the result for
    /// the process lifetime (the key never changes at runtime). Throws <see cref="FormatException"/>
    /// on any structural surprise - unreachable in production against the constant above, but keeps
    /// the parser honest for its own unit tests.</summary>
    public static RSAParameters GetParameters()
    {
        _cached ??= ParsePem(Pem);
        return _cached.Value;
    }

    /// <summary>Parses any PEM-wrapped "PUBLIC KEY" (X.509 SubjectPublicKeyInfo) block containing an
    /// RSA key into <see cref="RSAParameters"/>. Exposed (not private) so tests can exercise it
    /// directly against both the real embedded constant and hand-built malformed DER.</summary>
    public static RSAParameters ParsePem(string pem)
    {
        if (string.IsNullOrWhiteSpace(pem))
        {
            throw new FormatException("PEM text is empty.");
        }

        var base64 = ExtractBase64Body(pem);
        byte[] der;
        try
        {
            der = Convert.FromBase64String(base64);
        }
        catch (FormatException ex)
        {
            throw new FormatException("PEM body is not valid base64.", ex);
        }

        return ReadSubjectPublicKeyInfo(der);
    }

    private static string ExtractBase64Body(string pem)
    {
        const string beginMarker = "-----BEGIN PUBLIC KEY-----";
        const string endMarker = "-----END PUBLIC KEY-----";

        int beginIndex = pem.IndexOf(beginMarker, StringComparison.Ordinal);
        int endIndex = pem.IndexOf(endMarker, StringComparison.Ordinal);
        if (beginIndex < 0 || endIndex < 0 || endIndex <= beginIndex)
        {
            throw new FormatException("PEM text is missing the PUBLIC KEY begin/end markers.");
        }

        var body = pem.Substring(beginIndex + beginMarker.Length, endIndex - (beginIndex + beginMarker.Length));
        var sb = new StringBuilder(body.Length);
        foreach (var ch in body)
        {
            if (!char.IsWhiteSpace(ch))
            {
                sb.Append(ch);
            }
        }

        return sb.ToString();
    }

    // --- Minimal DER reader for X.509 SubjectPublicKeyInfo -> RSAPublicKey -------------------
    //
    // SubjectPublicKeyInfo ::= SEQUENCE {
    //     algorithm         AlgorithmIdentifier,   -- SEQUENCE { OID rsaEncryption, NULL } - skipped
    //     subjectPublicKey  BIT STRING              -- contains a DER-encoded RSAPublicKey
    // }
    // RSAPublicKey ::= SEQUENCE {
    //     modulus           INTEGER,
    //     publicExponent    INTEGER
    // }

    private static RSAParameters ReadSubjectPublicKeyInfo(byte[] der)
    {
        int pos = 0;
        ExpectTag(der, ref pos, 0x30); // outer SEQUENCE
        ReadLength(der, ref pos);

        // algorithm AlgorithmIdentifier SEQUENCE - contents are not needed (this reader only ever
        // handles RSA keys; a non-RSA SubjectPublicKeyInfo fails later when the BIT STRING's
        // contents don't parse as an RSAPublicKey SEQUENCE instead of failing here more precisely -
        // acceptable for a single hardcoded, source-controlled constant).
        ExpectTag(der, ref pos, 0x30);
        int algorithmLength = ReadLength(der, ref pos);
        pos += algorithmLength;

        // subjectPublicKey BIT STRING
        ExpectTag(der, ref pos, 0x03);
        ReadLength(der, ref pos);
        byte unusedBits = ReadByte(der, ref pos);
        if (unusedBits != 0)
        {
            throw new FormatException("Unexpected unused-bits count in SubjectPublicKey BIT STRING.");
        }

        // RSAPublicKey SEQUENCE
        ExpectTag(der, ref pos, 0x30);
        ReadLength(der, ref pos);

        var modulus = ReadInteger(der, ref pos);
        var exponent = ReadInteger(der, ref pos);

        return new RSAParameters { Modulus = modulus, Exponent = exponent };
    }

    private static byte ReadByte(byte[] data, ref int pos)
    {
        if (pos >= data.Length)
        {
            throw new FormatException("Unexpected end of DER data.");
        }

        return data[pos++];
    }

    private static void ExpectTag(byte[] data, ref int pos, byte expected)
    {
        var actual = ReadByte(data, ref pos);
        if (actual != expected)
        {
            throw new FormatException($"Expected DER tag 0x{expected:X2} but found 0x{actual:X2} at position {pos - 1}.");
        }
    }

    private static int ReadLength(byte[] data, ref int pos)
    {
        byte first = ReadByte(data, ref pos);
        if ((first & 0x80) == 0)
        {
            return first;
        }

        int numBytes = first & 0x7F;
        if (numBytes == 0 || numBytes > 4)
        {
            throw new FormatException("Unsupported DER length encoding.");
        }

        int length = 0;
        for (int i = 0; i < numBytes; i++)
        {
            length = (length << 8) | ReadByte(data, ref pos);
        }

        return length;
    }

    private static byte[] ReadInteger(byte[] data, ref int pos)
    {
        ExpectTag(data, ref pos, 0x02);
        int len = ReadLength(data, ref pos);
        if (len <= 0 || pos + len > data.Length)
        {
            throw new FormatException("Malformed DER INTEGER.");
        }

        int start = pos;
        pos += len;

        // A leading 0x00 byte is DER sign-padding for an INTEGER whose high bit would otherwise be
        // mistaken for a negative sign - strip exactly one when present, never more (a genuinely
        // zero-valued leading byte deeper in the number is significant and must be kept).
        if (len > 1 && data[start] == 0x00)
        {
            start += 1;
            len -= 1;
        }

        var result = new byte[len];
        Array.Copy(data, start, result, 0, len);
        return result;
    }
}
