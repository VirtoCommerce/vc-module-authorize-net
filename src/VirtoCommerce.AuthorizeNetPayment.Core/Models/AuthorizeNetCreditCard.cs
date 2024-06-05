namespace VirtoCommerce.AuthorizeNetPayment.Core.Models
{
    public class AuthorizeNetCreditCard
    {
        public string CardCode;
        public string CardNumber;
        public string ExpirationDate;

        public string ProxyEndpointUrl;
        public string ProxyHttpClientName;
    }
}
