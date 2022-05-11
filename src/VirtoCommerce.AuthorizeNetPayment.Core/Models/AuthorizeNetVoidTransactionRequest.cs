namespace VirtoCommerce.AuthorizeNetPayment.Core.Models
{
    public class AuthorizeNetVoidTransactionRequest : AuthorizeNetBaseRequest
    {
        public string TransactionId { get; set; }
    }
}
