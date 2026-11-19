namespace VirtoCommerce.AuthorizeNetPayment.Core.Models
{
    public class AuthorizeNetPaymentProfileRequest : AuthorizeNetBaseRequest
    {
        public string CustomerProfileId { get; set; }

        public string CustomerPaymentProfileId { get; set; }
    }
}
