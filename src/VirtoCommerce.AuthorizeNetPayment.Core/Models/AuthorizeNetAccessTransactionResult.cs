using System.Collections.Generic;
using System.Linq;

namespace VirtoCommerce.AuthorizeNetPayment.Core.Models
{
    public class AuthorizeNetAccessTransactionResult : AuthorizeNetBaseResult
    {
        public string TransactionId { get; set; }

        public string TransactionResponseCode { get; set; }

        public TransactionResponse TransactionResponse => TransactionResponseCode switch
        {
            "1" => TransactionResponse.Approved,
            "2" => TransactionResponse.Declined,
            "3" => TransactionResponse.Error,
            "4" => TransactionResponse.HeldForReview,
            _ => TransactionResponse.UnknownResponse
        };

        public IList<AuthorizeNetAccessTransactionMessage> TransactionMessages { get; set; } = new List<AuthorizeNetAccessTransactionMessage>();

        public IList<AuthorizeNetAccessTransactionMessage> TransactionErrors { get; set; } = new List<AuthorizeNetAccessTransactionMessage>();

        public AuthorizeNetAccessTransactionMessage TransactionMessage
        {
            get
            {
                return TransactionMessages.FirstOrDefault() ?? new AuthorizeNetAccessTransactionMessage();
            }
        }
    }
}
