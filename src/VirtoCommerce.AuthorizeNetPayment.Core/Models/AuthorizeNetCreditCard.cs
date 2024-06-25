namespace VirtoCommerce.AuthorizeNetPayment.Core.Models
{
    public class AuthorizeNetCreditCard
    {
        public string CardCode { get; set; }
        public string CardNumber { get; set; }
        public string CardExpiration { get; set; }

        public string ProxyEndpointUrl { get; set; }
        public string ProxyHttpClientName { get; set; }
    }
}
