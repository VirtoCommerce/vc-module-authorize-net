namespace VirtoCommerce.AuthorizeNetPayment.Core.Models
{
    public class AuthorizeNetCheckoutFormContext
    {
        public string ClientKey { get; set; }

        public string ApiLogin { get; set; }

        public string AcceptJsPath { get; set; }

        public string FormAction { get; set; }

        public string OrderId { get; set; }

        public string UserIp { get; set; }
    }
}
