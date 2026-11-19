namespace VirtoCommerce.AuthorizeNetPayment.Core.Models
{
    /// <summary>
    /// Data for a merchant-initiated charge of a stored card (Authorize.Net <c>subsequentAuthInformation</c>).
    /// </summary>
    public class AuthorizeNetSubsequentAuthInformation
    {
        /// <summary>The network transaction ID of the charge the card was stored with.</summary>
        public string OriginalNetworkTransactionId { get; set; }

        /// <summary>
        /// One of <c>resubmission</c>, <c>delayedCharge</c>, <c>reauthorization</c> or <c>noShow</c>;
        /// empty for a regular recurring or unscheduled charge.
        /// </summary>
        public string Reason { get; set; }
    }
}
