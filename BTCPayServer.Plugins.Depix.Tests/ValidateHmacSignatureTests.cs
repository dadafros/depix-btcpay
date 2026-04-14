using System;
using System.Security.Cryptography;
using System.Text;
using BTCPayServer.Plugins.Depix.Services;
using Xunit;

namespace BTCPayServer.Plugins.Depix.Tests;

/// <summary>
/// Unit tests for Utils.ValidateHmacSignature — covers the HMAC-SHA256 webhook
/// authentication logic added in the DePix integration migration.
/// </summary>
public class ValidateHmacSignatureTests
{
    private const string Secret = "test-webhook-secret";

    // Builds a valid X-DePix-Signature header for the given body/timestamp.
    private static string BuildSignature(string body, long timestampUnix, string secret = Secret)
    {
        var signedPayload = $"{timestampUnix}.{body}";
        var secretBytes = Encoding.UTF8.GetBytes(secret);
        var payloadBytes = Encoding.UTF8.GetBytes(signedPayload);
        var hmac = HMACSHA256.HashData(secretBytes, payloadBytes);
        var hex = Convert.ToHexString(hmac).ToLowerInvariant();
        return $"t={timestampUnix},v1={hex}";
    }

    private static long NowUnix() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    // ── Happy path ───────────────────────────────────────────────────────────

    [Fact]
    public void ValidSignature_ReturnsTrue()
    {
        var ts = NowUnix();
        var body = """{"event":"deposit.completed","data":{"id":"chk_abc"}}""";
        var header = BuildSignature(body, ts);

        Assert.True(Utils.ValidateHmacSignature(body, header, Secret));
    }

    [Fact]
    public void ValidSignature_UppercaseHex_ReturnsTrue()
    {
        // DePix API might send uppercase hex — we must accept both cases
        var ts = NowUnix();
        var body = """{"event":"deposit.completed"}""";
        var header = BuildSignature(body, ts).Replace("v1=", "v1=").ToUpper()
                         .Replace("T=", "t=").Replace("V1=", "v1=");

        // Reconstruct with uppercase HMAC only
        var parts = BuildSignature(body, ts);
        var upperHeader = parts.Replace("v1=", "v1=") // leave prefix lowercase
                               .Replace(",v1=", ",v1="); // HMAC value uppercased manually below
        var signedPayload = $"{ts}.{body}";
        var hmacBytes = HMACSHA256.HashData(Encoding.UTF8.GetBytes(Secret), Encoding.UTF8.GetBytes(signedPayload));
        var upperHex = Convert.ToHexString(hmacBytes).ToUpperInvariant(); // uppercase
        var upperCaseHeader = $"t={ts},v1={upperHex}";

        Assert.True(Utils.ValidateHmacSignature(body, upperCaseHeader, Secret));
    }

    [Fact]
    public void ValidSignature_WithExtraWhitespaceAroundComma_ReturnsTrue()
    {
        var ts = NowUnix();
        var body = "{}";
        var raw = BuildSignature(body, ts);
        // Insert spaces around the comma — our parser must handle this
        var spacedHeader = raw.Replace(",", " , ");

        Assert.True(Utils.ValidateHmacSignature(body, spacedHeader, Secret));
    }

    // ── Replay-attack protection ─────────────────────────────────────────────

    [Fact]
    public void Signature_OlderThan5Minutes_ReturnsFalse()
    {
        var oldTs = NowUnix() - 301; // 5m 1s ago
        var body = """{"event":"deposit.completed"}""";
        var header = BuildSignature(body, oldTs);

        Assert.False(Utils.ValidateHmacSignature(body, header, Secret));
    }

    [Fact]
    public void Signature_FutureTimestampBeyond5Minutes_ReturnsFalse()
    {
        var futureTs = NowUnix() + 301;
        var body = "{}";
        var header = BuildSignature(body, futureTs);

        Assert.False(Utils.ValidateHmacSignature(body, header, Secret));
    }

    [Fact]
    public void Signature_TimestampAtBoundary_ReturnsTrue()
    {
        // Exactly 300 s old is on the edge — |diff| == 300 which is NOT > 300, so it must pass
        var ts = NowUnix() - 300;
        var body = "{}";
        var header = BuildSignature(body, ts);

        Assert.True(Utils.ValidateHmacSignature(body, header, Secret));
    }

    // ── Tamper detection ────────────────────────────────────────────────────

    [Fact]
    public void WrongSecret_ReturnsFalse()
    {
        var ts = NowUnix();
        var body = "{}";
        var header = BuildSignature(body, ts, "correct-secret");

        Assert.False(Utils.ValidateHmacSignature(body, header, "wrong-secret"));
    }

    [Fact]
    public void AlteredBody_ReturnsFalse()
    {
        var ts = NowUnix();
        var originalBody = """{"event":"deposit.completed","data":{"id":"chk_abc"}}""";
        var tamperedBody  = """{"event":"deposit.completed","data":{"id":"chk_xyz"}}""";
        var header = BuildSignature(originalBody, ts);

        Assert.False(Utils.ValidateHmacSignature(tamperedBody, header, Secret));
    }

    [Fact]
    public void AlteredTimestampInHeader_ReturnsFalse()
    {
        var ts = NowUnix();
        var body = "{}";
        var header = BuildSignature(body, ts);
        // Replace timestamp with a recent but different value — HMAC was signed with original ts
        var alteredHeader = header.Replace($"t={ts}", $"t={ts - 1}");

        Assert.False(Utils.ValidateHmacSignature(body, alteredHeader, Secret));
    }

    // ── Null / empty guard-rails ─────────────────────────────────────────────

    [Fact]
    public void NullSignatureHeader_ReturnsFalse()
        => Assert.False(Utils.ValidateHmacSignature("{}", null, Secret));

    [Fact]
    public void EmptySignatureHeader_ReturnsFalse()
        => Assert.False(Utils.ValidateHmacSignature("{}", "", Secret));

    [Fact]
    public void EmptySecret_ReturnsFalse()
    {
        var ts = NowUnix();
        var header = BuildSignature("{}", ts);
        Assert.False(Utils.ValidateHmacSignature("{}", header, ""));
    }

    [Fact]
    public void MissingTimestampPart_ReturnsFalse()
        => Assert.False(Utils.ValidateHmacSignature("{}", "v1=abc123", Secret));

    [Fact]
    public void MissingV1Part_ReturnsFalse()
    {
        var ts = NowUnix();
        Assert.False(Utils.ValidateHmacSignature("{}", $"t={ts}", Secret));
    }

    [Fact]
    public void NonNumericTimestamp_ReturnsFalse()
        => Assert.False(Utils.ValidateHmacSignature("{}", "t=not-a-number,v1=abc", Secret));
}
