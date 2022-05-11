namespace VirtoCommerce.AuthorizeNetPayment.Core.Models
{
    public class AuthorizeNetTransactionRequest : AuthorizeNetBaseRequest
    {
        public string TransactionId { get; set; }
    }
}
