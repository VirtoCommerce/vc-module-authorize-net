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

        /// <summary>
        /// Charge a stored card instead of a nonce or card data.
        /// </summary>
        public AuthorizeNetProfileReference Profile { get; set; }

        /// <summary>
        /// Invoice number to send instead of the one derived from <see cref="OrderId"/> (at most 20 characters).
        /// With <see cref="DuplicateWindowSeconds"/>, it makes a repeated charge return the original transaction.
        /// </summary>
        public string InvoiceNumber { get; set; }

        /// <summary>
        /// How long, in seconds, the gateway treats an identical transaction as a duplicate (at most 28800).
        /// Null keeps the gateway default of two minutes.
        /// </summary>
        public int? DuplicateWindowSeconds { get; set; }

        /// <summary>
        /// Merchant-assigned customer ID (at most 20 characters) and e-mail, sent with the transaction.
        /// </summary>
        public string CustomerId { get; set; }

        public string CustomerEmail { get; set; }

        public AuthorizeNetProcessingOptions ProcessingOptions { get; set; }

        public AuthorizeNetSubsequentAuthInformation SubsequentAuthInformation { get; set; }
    }
}
