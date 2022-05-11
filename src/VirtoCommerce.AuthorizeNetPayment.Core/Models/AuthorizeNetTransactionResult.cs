using System.Collections.Generic;
using System.Linq;

namespace VirtoCommerce.AuthorizeNetPayment.Core.Models
{
    public class AuthorizeNetTransactionResult : AuthorizeNetBaseResult
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

        public IList<AuthorizeNetTransactionMessage> TransactionMessages { get; set; } = new List<AuthorizeNetTransactionMessage>();

        public AuthorizeNetTransactionMessage TransactionMessage
        {
            get
            {
                return TransactionMessages.FirstOrDefault() ?? new AuthorizeNetTransactionMessage();
            }
        }

        public string TransactionStatus { get; set; }

        public string TransactionType { get; set; }

        public bool IsSettled
        {
            get
            {
                return TransactionStatus == "settledSuccessfully";
            }
        }

        public string PaymentData { get; set; }

        public string AccountNumber { get; set; }

        public IList<AuthorizeNetTransactionMessage> TransactionErrors { get; set; } = new List<AuthorizeNetTransactionMessage>();
    }
}
