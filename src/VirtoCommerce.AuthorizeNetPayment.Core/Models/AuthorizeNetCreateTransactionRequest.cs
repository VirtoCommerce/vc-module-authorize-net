namespace VirtoCommerce.AuthorizeNetPayment.Core.Models
{
    public class AuthorizeNetCreateTransactionRequest : AuthorizeNetBaseRequest
    {
        public decimal Amount { get; set; }

        public string CurrencyCode { get; set; }

        public string OrderId { get; set; }

        public string OrderNumber { get; set; }

        /// <summary>
        /// Must be "COMMON.ACCEPT.INAPP.PAYMENT" in case of using Payment Nonce
        /// </summary>
        public string DataDescriptor { get; set; }

        /// <summary>
        /// Payment Nonce value
        /// </summary>
        public string DataValue { get; set; }

        /// <summary>
        /// Tokenized credit card data
        /// </summary>
        public AuthorizeNetCreditCard CreditCard { get; set; }

        public string PaymentActionType { get; set; }
    }
}
