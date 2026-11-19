using System.Collections.Generic;

namespace VirtoCommerce.AuthorizeNetPayment.Core.Models
{
    public class AuthorizeNetPaymentProfileResult : AuthorizeNetBaseResult
    {
        /// <summary>The masked card number, e.g. <c>XXXX1111</c>.</summary>
        public string CardNumber { get; set; }

        /// <summary>The card brand, e.g. <c>Visa</c>.</summary>
        public string CardType { get; set; }

        /// <summary>The unmasked expiry date, <c>YYYY-MM</c>.</summary>
        public string ExpirationDate { get; set; }

        /// <summary>The network transaction ID of the charge or validation the card was stored with.</summary>
        public string OriginalNetworkTransactionId { get; set; }

        /// <summary>True when the payment profile no longer exists (record not found).</summary>
        public bool IsNotFound { get; set; }

        public IList<AuthorizeNetTransactionMessage> Errors { get; set; } = new List<AuthorizeNetTransactionMessage>();
    }
}
