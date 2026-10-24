using System.Security.Cryptography;
using System.Text;

namespace Akusiap.Infrastructure.Payments;

/// <summary>
/// DOKU's non-SNAP request signature (PAY-04): HMAC-SHA256 with the Secret Key over the lines
/// <c>Client-Id</c>, <c>Request-Id</c>, <c>Request-Timestamp</c>, <c>Request-Target</c> and, for requests with a body,
/// <c>Digest</c> (Base64 of the body's SHA-256), joined by <c>\n</c>, Base64-encoded and prefixed with
/// <c>HMACSHA256=</c>. The same scheme signs DOKU's notifications to us, with our notification path as the target.
/// </summary>
public static class DokuSignature
{
    public const string Prefix = "HMACSHA256=";

    public static string Digest(string body) => Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(body)));

    public static string StringToSign(string clientId, string requestId, string timestamp, string target, string? body)
    {
        var text = $"Client-Id:{clientId}\nRequest-Id:{requestId}\nRequest-Timestamp:{timestamp}\nRequest-Target:{target}";
        return body is null ? text : $"{text}\nDigest:{Digest(body)}";
    }

    public static string Sign(string secretKey, string clientId, string requestId, string timestamp, string target, string? body) =>
        SignText(secretKey, StringToSign(clientId, requestId, timestamp, target, body));

    /// <summary>The signature header value for an already built string to sign.</summary>
    public static string SignText(string secretKey, string stringToSign)
    {
        ArgumentNullException.ThrowIfNull(secretKey);
        ArgumentNullException.ThrowIfNull(stringToSign);
        return Prefix + Convert.ToBase64String(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secretKey), Encoding.UTF8.GetBytes(stringToSign)));
    }

    /// <summary>Compares a received signature with the expected one in constant time.</summary>
    public static bool Matches(string expected, string? received) =>
        received is not null
        && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(received.Trim()));
}
