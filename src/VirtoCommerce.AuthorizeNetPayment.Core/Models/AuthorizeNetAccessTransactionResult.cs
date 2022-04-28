using System.Collections.Generic;
using System.Linq;

namespace VirtoCommerce.AuthorizeNetPayment.Core.Models
{
    public class AuthorizeNetAccessTransactionResult : AuthorizeNetBaseResult
    {
        private static IDictionary<string, TransactionResponse> _transactionResponseMap = new Dictionary<string, TransactionResponse>
        {
            { "1", TransactionResponse.Approved },
            { "2", TransactionResponse.Declined },
            { "3", TransactionResponse.Error },
            { "4", TransactionResponse.HeldForReview },
        };

        public string TransactionId { get; set; }

        public string TransactionResponseCode { get; set; }

        public TransactionResponse TransactionResponse
        {
            get
            {
                return _transactionResponseMap[TransactionResponseCode];
            }
        }

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
