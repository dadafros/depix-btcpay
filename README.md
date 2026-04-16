# DePix App Plugin for BTCPay Server

Accept **Pix** payments in your BTCPay Server store and receive funds in **DePix** -- a BRL stablecoin on the Liquid Network (1 DEPIX = 1 BRL). Customers pay with any Brazilian bank app; you receive DePix in your Liquid wallet with no chargebacks.

Pix payment amounts range from **R$ 5.00 to R$ 3,000.00** per transaction.

## How it works

1. Customer creates an invoice (or uses your Point of Sale).
2. The plugin calls the DePix API to generate a Pix checkout with a QR code.
3. Customer scans the QR code and pays via Pix using any Brazilian bank app.
4. DePix sends a webhook to BTCPay confirming payment.
5. Funds settle to your **DePix (Liquid) wallet**.

## Requirements

- BTCPay Server >= 2.3.7
- A **DePix App API key** -- get one at [depixapp.com/btcpay](https://depixapp.com/btcpay)

## Installation

1. Go to [**GitHub Releases**](https://github.com/dadafros/depix-btcpay/releases) and download the latest `.btcpay` file.
2. In BTCPay Server: go to **Server Settings > Plugins > Upload Plugin**.
3. Select the downloaded `.btcpay` file and confirm.
4. Restart BTCPay Server when prompted.

After installation the DePix (Liquid) asset is automatically registered for your store.

## Configuration

### Store-level settings (recommended)

1. Go to **Wallets > Pix > Settings**.
2. Paste your **DePix API key** and click **Save**.
3. The plugin validates the key against the DePix API. On success you will see the **Webhook URL** and a one-time **webhook secret**.
4. Copy the webhook secret and register it with DePix (see Webhook setup below).
5. Optionally enable **Pass fee to customer** and **Whitelist mode**.
6. Check **Enable Pix** and click **Save** again.

### Server-level settings (optional, for server admins)

Server admins can set a default configuration at **Server Settings > Pix** so that all stores inherit it. Stores that have their own API key and webhook secret take precedence over the server config.

### Webhook setup

After saving a valid API key (at either store or server level), the settings page shows:

- **Webhook URL** -- the endpoint DePix will call when a payment completes.
- **One-time secret** -- displayed only once. Copy it immediately.

Register the webhook URL and secret in the DePix App Merchant Area (API e Webhooks section). If you lose the secret, click **Regenerate secret** and save again.

## Wallet setup

Choose one:

**Option A -- Aqua wallet (recommended)**

1. Install the **SamRock** plugin in BTCPay and pair it with the [Aqua](https://aquawallet.io) app.
2. Copy the LBTC xpub from **Wallets > Liquid Bitcoin > Settings > Derivation Scheme**.
3. Paste it in **Wallets > DePix > Connect an existing wallet > Enter extended public key**.

Deposits go directly to your Aqua wallet.

**Option B -- BTCPay hot wallet**

1. Go to **Wallets > DePix > Create new wallet > Hot wallet**.
2. To spend later, use the **Liquid+** plugin with `elements-cli` to import keys and send transactions.

Liquid transaction fees are paid in **L-BTC**. Keep a small L-BTC balance to cover fees.

## Usage

- **Invoices**: create an invoice as usual -- Pix appears as a payment option.
- **Point of Sale**: Pix is available on POS charges.
- **Transactions**: go to **Wallets > Pix** to monitor deposits and statuses.
- **DePix Balance**: go to **Wallets > DePix** to see received DePix tokens.

## Support

Open an [issue](https://github.com/dadafros/depix-btcpay/issues) or join the [WhatsApp group](https://chat.whatsapp.com/KWuysHsJhXwCQOpYHJZUP5?mode=gi_t).

## License

All rights reserved.
