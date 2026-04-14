#nullable enable
using System;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;

namespace BTCPayServer.Plugins.Depix.Services;

public static class Utils
{
    public static string BuildWebhookUrl(HttpRequest req, string? storeId = null)
    {
        var baseUrl = $"{req.Scheme}://{req.Host}";
        return storeId is null
            ? $"{baseUrl}/depix/webhooks/deposit"
            : $"{baseUrl}/depix/webhooks/deposit/{storeId}";
    }

    public static string ComputeSecretHash(string secretPlain)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(secretPlain ?? ""));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>
    /// Validates the X-DePix-Signature header against the raw request body.
    /// Signature format: t={timestamp},v1={hmac}
    /// The signed payload is "{timestamp}.{body}".
    /// </summary>
    public static bool ValidateHmacSignature(string body, string? signatureHeader, string webhookSecret)
    {
        if (string.IsNullOrEmpty(signatureHeader) || string.IsNullOrEmpty(webhookSecret))
            return false;

        string? timestamp = null;
        string? signature = null;

        foreach (var part in signatureHeader.Split(','))
        {
            var trimmed = part.Trim();
            if (trimmed.StartsWith("t=", StringComparison.Ordinal))
                timestamp = trimmed[2..];
            else if (trimmed.StartsWith("v1=", StringComparison.Ordinal))
                signature = trimmed[3..];
        }

        if (string.IsNullOrEmpty(timestamp) || string.IsNullOrEmpty(signature))
            return false;

        // Replay protection: reject webhooks older/newer than 5 minutes
        if (!long.TryParse(timestamp, out var tsUnix) ||
            Math.Abs((DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeSeconds(tsUnix)).TotalSeconds) > 300)
            return false;

        var signedPayload = $"{timestamp}.{body}";
        var secretBytes = Encoding.UTF8.GetBytes(webhookSecret);
        var payloadBytes = Encoding.UTF8.GetBytes(signedPayload);
        var expectedBytes = HMACSHA256.HashData(secretBytes, payloadBytes);
        var expectedHex = Convert.ToHexString(expectedBytes).ToLowerInvariant();

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expectedHex),
            Encoding.UTF8.GetBytes(signature.ToLowerInvariant()));
    }

    public static bool FixedEqualsHex(string hexA, string hexB)
    {
        if (hexA.Length != hexB.Length) return false;
        try
        {
            var a = Convert.FromHexString(hexA);
            var b = Convert.FromHexString(hexB);
            return CryptographicOperations.FixedTimeEquals(a, b);
        }
        catch { return false; }
    }
}
