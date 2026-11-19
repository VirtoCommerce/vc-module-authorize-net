namespace VirtoCommerce.AuthorizeNetPayment.Core.Models
{
    /// <summary>
    /// Stored credential framework flags sent with a transaction (Authorize.Net <c>processingOptions</c>).
    /// </summary>
    public class AuthorizeNetProcessingOptions
    {
        /// <summary>The first charge of a recurring series, with the card being stored for it.</summary>
        public bool IsFirstRecurringPayment { get; set; }

        /// <summary>The first charge of an unscheduled card-on-file series, with the card being stored for it.</summary>
        public bool IsFirstSubsequentAuth { get; set; }

        /// <summary>A merchant-initiated charge of a stored card.</summary>
        public bool IsSubsequentAuth { get; set; }

        /// <summary>The charge uses a stored card (customer- or merchant-initiated).</summary>
        public bool IsStoredCredentials { get; set; }
    }
}
