namespace VirtoCommerce.AuthorizeNetPayment.Core.Models
{
    /// <summary>
    /// A stored card in the Authorize.Net Customer Information Manager: the customer profile and its payment profile.
    /// </summary>
    public class AuthorizeNetProfileReference
    {
        public string CustomerProfileId { get; set; }

        public string CustomerPaymentProfileId { get; set; }
    }
}
