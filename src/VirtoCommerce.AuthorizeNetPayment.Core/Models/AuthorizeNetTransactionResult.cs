using System.Collections.Generic;
using System.Linq;
using VirtoCommerce.Platform.Core.Common;

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

        public IList<AuthorizeNetTransactionMessage> TransactionErrors { get; set; } = new List<AuthorizeNetTransactionMessage>();

        public IList<AuthorizeNetTransactionMessage> Errors { get; set; } = new List<AuthorizeNetTransactionMessage>();

        public AuthorizeNetTransactionMessage TransactionMessage
        {
            get
            {
                var result = new AuthorizeNetTransactionMessage()
                {
                    Code = string.Empty,
                    Description = string.Empty,
                };

                CombineMessages(Errors, result);
                CombineMessages(TransactionErrors, result);
                CombineMessages(TransactionMessages, result);

                return result;
            }
        }

        private void CombineMessages(IList<AuthorizeNetTransactionMessage> messages, AuthorizeNetTransactionMessage message)
        {
            if (messages.IsNullOrEmpty())
            {
                return;
            }

            var codes = string.Join(";", messages.Select(x => x.Code));
            message.Code = $"{codes};{message.Code}";

            var descriptions = string.Join(";", messages.Select(x => $"{x.Description} ({x.Code})"));
            message.Description = $"{descriptions};{message.Description}";
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
    }
}
