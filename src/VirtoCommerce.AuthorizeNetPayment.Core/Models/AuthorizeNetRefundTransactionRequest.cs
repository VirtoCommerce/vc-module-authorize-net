namespace VirtoCommerce.AuthorizeNetPayment.Core.Models
{
    public class AuthorizeNetRefundTransactionRequest : AuthorizeNetBaseRequest
    {
        public string TransactionId { get; set; }

        public decimal TransactionAmount { get; set; }

        /// <summary>
        /// Last 4 CC digits
        /// </summary>
        public string PaymentData { get; set; }
    }
}
