namespace VirtoCommerce.AuthorizeNetPayment.Core.Models
{
    /// <summary>
    /// Finds a customer profile in the Customer Information Manager by the merchant-assigned customer ID.
    /// </summary>
    public class AuthorizeNetCustomerLookupRequest : AuthorizeNetBaseRequest
    {
        public string MerchantCustomerId { get; set; }
    }
}
