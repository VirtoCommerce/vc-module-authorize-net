namespace VirtoCommerce.AuthorizeNetPayment.Core.Models
{
    /// <summary>
    /// The billing address of a card. A $0 validation authorization needs at least the street address and ZIP code for AVS.
    /// </summary>
    public class AuthorizeNetBillingAddress
    {
        public string FirstName { get; set; }

        public string LastName { get; set; }

        public string Address { get; set; }

        public string City { get; set; }

        public string State { get; set; }

        public string Zip { get; set; }

        public string Country { get; set; }

        public bool HasAvsData => !string.IsNullOrWhiteSpace(Address) && !string.IsNullOrWhiteSpace(Zip);
    }
}
