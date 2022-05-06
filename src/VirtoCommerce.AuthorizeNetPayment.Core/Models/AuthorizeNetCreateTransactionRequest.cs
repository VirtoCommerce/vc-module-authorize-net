namespace VirtoCommerce.AuthorizeNetPayment.Core.Models
{
    public class AuthorizeNetCreateTransactionRequest : AuthorizeNetBaseRequest
    {
        public decimal Amount { get; set; }

        public string CurrencyCode { get; set; }

        public string OrderId { get; set; }

        public string OrderNumber { get; set; }

        public string DataDescriptor { get; set; }

        public string DataValue { get; set; }

        public string PaymentActionType { get; set; }
    }
}
