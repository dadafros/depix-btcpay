#nullable enable
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Plugins.Depix.Data.Models;
using BTCPayServer.Plugins.Depix.Services;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Plugins.Depix.Controllers;

[ApiController]
[Route("depix/webhooks")]
public class DepixWebhookController(
    StoreRepository stores,
    PaymentMethodHandlerDictionary handlers,
    DepixService depixService,
    ISecretProtector secretProtector)
    : ControllerBase
{
    // Store-scoped webhook: /depix/webhooks/deposit/{storeId}
    [HttpPost("deposit/{storeId}")]
    [AllowAnonymous]
    public async Task<IActionResult> DepositStore([FromRoute] string storeId)
    {
        var store = await stores.FindStore(storeId);
        if (store is null) return NotFound();

        var cfg = depixService.GetPixConfig(store, handlers);
        var effectiveConfig = await depixService.GetEffectiveConfigAsync(cfg);
        if (effectiveConfig.Source == DepixService.DepixConfigSource.None)
            return NotFound();

        var webhookSecret = secretProtector.Unprotect(effectiveConfig.EncryptedWebhookSecret);
        if (string.IsNullOrEmpty(webhookSecret))
            return Unauthorized();

        var rawBody = await ReadBodyAsync();
        var signatureHeader = Request.Headers["X-DePix-Signature"].ToString();

        if (!Utils.ValidateHmacSignature(rawBody, signatureHeader, webhookSecret))
            return Unauthorized();

        var payload = JsonSerializer.Deserialize<DepixWebhookPayload>(rawBody);
        if (payload?.Data.Id is null)
            return BadRequest();

        _ = Task.Run(() => depixService.ProcessWebhookAsync(storeId, payload, CancellationToken.None), CancellationToken.None);
        return Ok();
    }

    // Server-scoped webhook: /depix/webhooks/deposit
    [HttpPost("deposit")]
    [AllowAnonymous]
    public async Task<IActionResult> DepositServer()
    {
        var server = await depixService.GetServerConfigAsync();
        var webhookSecret = secretProtector.Unprotect(server.EncryptedWebhookSecret);
        if (string.IsNullOrEmpty(webhookSecret))
            return Unauthorized();

        var rawBody = await ReadBodyAsync();
        var signatureHeader = Request.Headers["X-DePix-Signature"].ToString();

        if (!Utils.ValidateHmacSignature(rawBody, signatureHeader, webhookSecret))
            return Unauthorized();

        var payload = JsonSerializer.Deserialize<DepixWebhookPayload>(rawBody);
        if (payload?.Data.Id is null)
            return BadRequest();

        _ = Task.Run(() => depixService.ProcessWebhookAsync(payload, CancellationToken.None), CancellationToken.None);
        return Ok();
    }

    private async Task<string> ReadBodyAsync()
    {
        Request.EnableBuffering();
        Request.Body.Position = 0;
        using var reader = new StreamReader(Request.Body, Encoding.UTF8, leaveOpen: true);
        var body = await reader.ReadToEndAsync();
        Request.Body.Position = 0;
        return body;
    }
}
