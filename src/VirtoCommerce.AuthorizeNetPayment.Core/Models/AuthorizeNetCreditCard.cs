namespace VirtoCommerce.AuthorizeNetPayment.Core.Models
{
    public class AuthorizeNetCreditCard
    {
        public string CardCode;
        public string CardNumber;
        public string CardExpiration;

        public string ProxyEndpointUrl;
        public string ProxyHttpClientName;
        public string BearerToken;
    }
}
