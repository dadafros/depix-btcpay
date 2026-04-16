using System;
using System.Security.Cryptography;
using System.Text;
using BTCPayServer.Plugins.DepixApp.Services;
using Xunit;

namespace BTCPayServer.Plugins.DepixApp.Tests;

/// <summary>
/// Unit tests for webhook validation logic used by DepixWebhookController.
/// Tests the auth / guard-rails without needing the full BTCPay DI stack.
/// </summary>
public class DepixWebhookControllerTests
{
    // Builds a valid X-DePix-Signature header
    private static string Sign(string body, string secret, long? ts = null)
    {
        var timestamp = ts ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var signedPayload = $"{timestamp}.{body}";
        var hmac = HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(secret),
            Encoding.UTF8.GetBytes(signedPayload));
        return $"t={timestamp},v1={Convert.ToHexString(hmac).ToLowerInvariant()}";
    }

    // ── Signature acceptance gate ────────────────────────────────────────────

    [Fact]
    public void ValidRequest_PassesSignatureGate()
    {
        const string secret = "my-webhook-secret";
        const string body   = """{"event":"deposit.completed","data":{"id":"chk_1"}}""";
        var sig = Sign(body, secret);

        Assert.True(Utils.ValidateHmacSignature(body, sig, secret));
    }

    [Fact]
    public void WrongSecret_FailsSignatureGate()
    {
        const string body = "{}";
        var sig = Sign(body, "correct");

        Assert.False(Utils.ValidateHmacSignature(body, sig, "wrong"));
    }

    // ── Replay attacks ────────────────────────────────────────────────────────

    [Fact]
    public void ReplayedWebhook_OlderThan5Min_IsRejected()
    {
        const string secret = "s3cr3t";
        const string body   = "{}";
        var staleTs = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 400;
        var sig = Sign(body, secret, staleTs);

        Assert.False(Utils.ValidateHmacSignature(body, sig, secret));
    }

    [Fact]
    public void FreshWebhook_IsAccepted()
    {
        const string secret = "s3cr3t";
        const string body   = "{}";
        var sig = Sign(body, secret);

        Assert.True(Utils.ValidateHmacSignature(body, sig, secret));
    }

    // ── Null/empty payload guard ─────────────────────────────────────────────

    [Fact]
    public void MissingSignatureHeader_IsRejected()
        => Assert.False(Utils.ValidateHmacSignature("{}", null, "secret"));

    [Fact]
    public void EmptySignatureHeader_IsRejected()
        => Assert.False(Utils.ValidateHmacSignature("{}", "", "secret"));

    [Fact]
    public void EmptyWebhookSecret_IsRejected()
    {
        var sig = Sign("{}", "anything");
        Assert.False(Utils.ValidateHmacSignature("{}", sig, ""));
    }

    // ── Tamper detection ─────────────────────────────────────────────────────

    [Fact]
    public void TamperedBody_IsRejected()
    {
        const string secret = "s3cr3t";
        var sig = Sign("""{"id":"chk_A"}""", secret);

        Assert.False(Utils.ValidateHmacSignature("""{"id":"chk_B"}""", sig, secret));
    }
}
