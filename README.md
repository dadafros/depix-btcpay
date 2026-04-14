# DePix Plugin for BTCPay Server

Accept **Pix** payments in your BTCPay Server store and receive funds in **DePix** (a Liquid-based BRL stablecoin). Powered by the [DePix App](https://depixapp.com).

---

## What it does

* Adds **Pix** as a payment method to your BTCPay store via the DePix API.
* Adds Liquid Network stablecoin **DePix** as a payment method.
* After you save a valid configuration (API key + webhook secret), Pix appears on invoices and **Point of Sale (POS)** apps.
* Provides a **Pix Transactions** page to monitor deposits and statuses.
* Funds settle to your **DePix (Liquid) wallet** — irreversible on the blockchain. No chargebacks, no MED.

> Community plugin. Not affiliated with BTCPay Server. Experimental — feedback is welcome!

---

## Requirements

* BTCPay Server >= 2.3.7
* A **DePix API key** (`sk_live_...` or `sk_test_...`) — create one at [depixapp.com/btcpay](https://depixapp.com/btcpay)
* Your **webhook secret** (`whsec_...`) from the DePix App Merchant Area

---

## Installation

1. Install the plugin in BTCPay (**Plugins > Manage Plugins > search for DePix**).
2. Restart BTCPay when prompted.

> When installed, the DePix (Liquid) asset is registered for your store. You only need to create your DePix wallet and configure the plugin to enable Pix payments.

---

## Create your DePix wallet (choose one)

### Option A — External (Aqua via SamRock Plugin, xpub import)

1. In BTCPay: **Plugins > Manage Plugins > install SamRock**; open SamRock and **scan** the pairing QR with the **Aqua** app.
2. **Wallets > Liquid Bitcoin > Settings > Derivation Scheme** — copy the LBTC xpub.
3. **Wallets > DePix > Connect an existing wallet > Enter extended public key** — paste the LBTC xpub.

Result: BTCPay derives DePix receiving addresses from this xpub. Deposits go directly to your Aqua wallet.

### Option B — BTCPay Hot Wallet

1. **Wallets > DePix > Create new wallet > Hot wallet**.
2. To spend later, import the generated keys using **Liquid+** and `elements-cli` (see **Spending DePix** below).

---

## Configuration

### 1. Store setup (Wallets > Pix > Settings)

1. Go to **Wallets > Pix > Settings**
2. Paste your **DePix API key** (`sk_live_...`)
3. Paste your **Webhook Secret** (`whsec_...`) from the DePix App Merchant Area
4. Copy the **Webhook URL** shown on the page and paste it into the DePix App
5. Check **Enable Pix** and click **Save**

### 2. Server setup (optional, for server admins)

Path: **Server Settings > Pix**

Use this when you run a BTCPay Server instance and want a default configuration for multiple stores:

1. Go to **Server Settings > Pix**
2. Paste the **server DePix API key** and **webhook secret**
3. Copy the **webhook URL** and paste it into the DePix App
4. Click **Save**

### Precedence

- If the store has a complete configuration (API key + webhook secret), **store config is used**.
- Otherwise, if the server has a complete configuration, **server config is used**.
- If neither exists, Pix cannot be enabled.

---

## Using it

* **Invoices**: create an invoice as usual; customers will see **Pix** as a payment method.
* **POS**: generate charges from your Point of Sale; Pix is available.
* **Transactions**: go to **Wallets > Pix** to track Pix deposits (status, ID, amount, time, etc.).
* **DePix Balance**: go to **Wallets > DePix** to track the received DePix tokens.

---

## Testing with sandbox

1. Create a DePix account at [depixapp.com](https://depixapp.com)
2. Configure the plugin with your **test API key** (`sk_test_...`)
3. Create an invoice in BTCPay
4. On the DePix checkout page, click **"Simular pagamento"** (simulate payment)
5. The plugin receives a `checkout.completed` webhook and marks the invoice as Settled

---

## Spending DePix

After a Pix payment, funds settle to your **DePix (Liquid) wallet**.

- **SamRock + Aqua**: funds go to your Aqua wallet. Spend them normally.
- **BTCPay Hot Wallet**: use the **Liquid+** plugin + `elements-cli` to import keys and send transactions.

**Note**: Liquid transaction fees are paid in **L-BTC**. Keep a small L-BTC balance to cover fees.

---

## FAQ

**Where do I get the DePix API key?**
Create an account at [depixapp.com/btcpay](https://depixapp.com/btcpay). API keys are self-service.

**Where do I get the webhook secret?**
In the DePix App Merchant Area, under your merchant settings.

**Is the webhook mandatory?**
Yes. The webhook is required so BTCPay receives real-time payment status updates from DePix.

**Pix doesn't appear as a payment method. What should I check?**
Make sure DePix is configured (API key + webhook secret) either at the store level or by the server admin. Once configured, enable Pix in the settings.

---

## Support & feedback

Open an **issue** on the repository. Pull requests are welcome.
For anything related to the DePix plugin, join the [Telegram group](https://t.me/+xFiXWiZPAQ05O).

## License

MIT.
