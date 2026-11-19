namespace VirtoCommerce.AuthorizeNetPayment.Core.Models
{
    /// <summary>
    /// Saves a card in the Customer Information Manager from an Accept.js payment nonce: in a new customer profile,
    /// or as another payment profile of an existing one.
    /// </summary>
    public class AuthorizeNetCustomerProfileRequest : AuthorizeNetBaseRequest
    {
        /// <summary>The existing customer profile to add the card to; empty to create a customer profile.</summary>
        public string CustomerProfileId { get; set; }

        /// <summary>Merchant-assigned customer ID (at most 20 characters).</summary>
        public string MerchantCustomerId { get; set; }

        public string Email { get; set; }

        public string Description { get; set; }

        public string DataDescriptor { get; set; }

        public string DataValue { get; set; }

        /// <summary>The card's billing address; a $0 validation authorization needs the street address and ZIP code.</summary>
        public AuthorizeNetBillingAddress BillTo { get; set; }

        /// <summary>
        /// <c>none</c>, <c>testMode</c> (format check only) or <c>liveMode</c> (a $0 or $0.01 authorization that is voided).
        /// </summary>
        public string ValidationMode { get; set; }
    }
}
