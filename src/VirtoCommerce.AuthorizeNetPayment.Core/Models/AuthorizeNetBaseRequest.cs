namespace VirtoCommerce.AuthorizeNetPayment.Core.Models
{
    public abstract class AuthorizeNetBaseRequest
    {
        public bool IsLiveMode { get; set; }

        public string ApiLogin { get; set; }

        public string TransactionKey { get; set; }
    }
}
