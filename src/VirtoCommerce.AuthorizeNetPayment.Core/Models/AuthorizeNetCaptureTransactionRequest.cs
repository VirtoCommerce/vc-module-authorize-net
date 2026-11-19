namespace VirtoCommerce.AuthorizeNetPayment.Core.Models
{
    public class AuthorizeNetCaptureTransactionRequest : AuthorizeNetBaseRequest
    {
        public string TransactionId { get; set; }

        /// <summary>
        /// The amount to capture: the authorized amount, or less for a partial capture.
        /// </summary>
        public decimal TransactionAmount { get; set; }
    }
}
