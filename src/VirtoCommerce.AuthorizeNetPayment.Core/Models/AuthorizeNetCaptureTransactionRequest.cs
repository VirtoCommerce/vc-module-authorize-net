namespace VirtoCommerce.AuthorizeNetPayment.Core.Models
{
    public class AuthorizeNetCaptureTransactionRequest : AuthorizeNetBaseRequest
    {
        public string TransactionId { get; set; }

        public decimal TransactionAmount { get; set; }
    }
}
