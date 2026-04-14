# DePix Plugin for BTCPay Server

Accept **Pix** payments in your BTCPay Server store and receive funds in **DePix** — a Liquid Network BRL stablecoin (1 DEPIX = 1 BRL). Powered by the [DePix App](https://depixapp.com).

## How it works

1. Customer creates an invoice (or uses your Point of Sale).
2. The plugin calls the DePix API to generate a Pix checkout.
3. Customer pays via Pix using any Brazilian bank app.
4. DePix sends a webhook to BTCPay confirming payment.
5. Funds settle to your **DePix (Liquid) wallet** — no chargebacks, no MED.

The plugin adds two payment methods to your store:
- **Pix** — Brazilian instant payments via QR code
- **DePix** — Liquid Network stablecoin for on-chain settlement

## Requirements

- BTCPay Server >= 2.3.7
- A **DePix API key** (`sk_live_...` or `sk_test_...`) — get one at [depixapp.com/btcpay](https://depixapp.com/btcpay)
- A **webhook secret** (`whsec_...`) from the DePix App Merchant Area

## Setup

### 1. Install the plugin

Install in BTCPay via **Plugins > Manage Plugins**. Restart when prompted.

When installed, the DePix (Liquid) asset is automatically registered for your store.

### 2. Create your DePix wallet

Choose one:

**Option A — Aqua wallet (recommended)**

1. Install the **SamRock** plugin in BTCPay and pair it with the [Aqua](https://aquawallet.io) app.
2. Go to **Wallets > Liquid Bitcoin > Settings > Derivation Scheme** and copy the LBTC xpub.
3. Go to **Wallets > DePix > Connect an existing wallet > Enter extended public key** and paste it.

Deposits go directly to your Aqua wallet.

**Option B — BTCPay hot wallet**

1. Go to **Wallets > DePix > Create new wallet > Hot wallet**.
2. To spend later, use the **Liquid+** plugin with `elements-cli`.

### 3. Configure Pix (store level)

1. Go to **Wallets > Pix > Settings**
2. Paste your **DePix API key** (`sk_live_...`)
3. Paste your **webhook secret** (`whsec_...`)
4. Copy the **Webhook URL** shown on the page and paste it into the DePix App Merchant Area
5. Check **Enable Pix** and click **Save**

### 4. Configure Pix (server level — optional)

Server admins can set a default configuration for all stores at **Server Settings > Pix**. Stores without their own config will use the server config. Stores with their own API key + webhook secret take precedence.

## Usage

- **Invoices**: create an invoice as usual — Pix appears as a payment option.
- **Point of Sale**: Pix is available on POS charges.
- **Transactions**: go to **Wallets > Pix** to monitor Pix deposits and statuses.
- **DePix Balance**: go to **Wallets > DePix** to see received DePix tokens.

## Sandbox testing

1. Create a DePix account at [depixapp.com](https://depixapp.com)
2. Configure the plugin with a **test API key** (`sk_test_...`)
3. Create an invoice in BTCPay
4. On the checkout page, click **"Simular pagamento"** to simulate the Pix payment
5. The plugin receives the webhook and marks the invoice as Settled

## Spending DePix

- **Aqua wallet**: funds arrive directly — spend them normally.
- **BTCPay hot wallet**: use the **Liquid+** plugin + `elements-cli` to import keys and send transactions.

Liquid transaction fees are paid in **L-BTC**. Keep a small L-BTC balance to cover fees.

## FAQ

**Where do I get the API key?**
Create an account at [depixapp.com/btcpay](https://depixapp.com/btcpay). API keys are self-service.

**Where do I get the webhook secret?**
In the DePix App Merchant Area, under your merchant settings.

**Pix doesn't appear on invoices. What's wrong?**
Check that both API key and webhook secret are configured (at store or server level), and that Pix is enabled in the settings.

## Support

For questions or issues with the DePix plugin, join the [Telegram group](https://t.me/+xFiXWiZPAQ05O).

## License

MIT
