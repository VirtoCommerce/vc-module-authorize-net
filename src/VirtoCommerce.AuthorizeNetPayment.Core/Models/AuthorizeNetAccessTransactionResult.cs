using System.Collections.Generic;

namespace VirtoCommerce.AuthorizeNetPayment.Core.Models
{
    public class AuthorizeNetAccessTransactionResult : AuthorizeNetBaseResult
    {
        public TransactionResponse TransactionResponse { get; set; }

        public string TransactionId { get; set; }

        public IList<AuthorizeNetAccessTransactionMessage> TransactionMessages { get; set; } = new List<AuthorizeNetAccessTransactionMessage>();

        public IList<AuthorizeNetAccessTransactionMessage> TransactionErrors { get; set; } = new List<AuthorizeNetAccessTransactionMessage>();
    }
}
