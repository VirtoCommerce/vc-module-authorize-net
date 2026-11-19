namespace VirtoCommerce.AuthorizeNetPayment.Core.Models
{
    /// <summary>
    /// Saves the card of an approved transaction in the Customer Information Manager: in a new customer profile,
    /// or as another payment profile of an existing one.
    /// </summary>
    public class AuthorizeNetProfileFromTransactionRequest : AuthorizeNetBaseRequest
    {
        public string TransactionId { get; set; }

        /// <summary>The existing customer profile to add the card to; empty to create a customer profile.</summary>
        public string CustomerProfileId { get; set; }

        /// <summary>Merchant-assigned customer ID (at most 20 characters).</summary>
        public string MerchantCustomerId { get; set; }

        public string Email { get; set; }

        public string Description { get; set; }
    }
}
