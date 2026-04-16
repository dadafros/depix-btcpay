using System;
using System.Collections.Generic;
using BTCPayServer.Plugins.DepixApp.Data.Enums;

namespace BTCPayServer.Plugins.DepixApp.Data.Models.ViewModels
{
    public class PixTransactionsViewModel
    {
        public List<PixTxResponse> Transactions { get; set; } = [];
        public string StoreId { get; set; }
        public string WalletId { get; set; }
        public PixConfigStatus ConfigStatus { get; set; } = new(false, false, false);
        public PixTxQueryRequest QueryRequest { get; set; }
    }

    public class PixTxResponse
    {
        public string InvoiceId { get; set; } = "";
        public DateTimeOffset Created { get; set; }
        public string QrId { get; set; } = "";
        public string DepixAddress { get; set; }   
        public int? ValueInCents { get; set; }
        public string AmountBrl { get; set; }
        public string DepixStatusRaw { get; set; } = "pending";
        public DepixStatus? DepixStatus { get; set; } 
    }
}