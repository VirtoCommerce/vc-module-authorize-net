using System.Collections.Generic;

namespace VirtoCommerce.AuthorizeNetPayment.Core.Models
{
    public class AuthorizeNetCustomerProfileResult : AuthorizeNetBaseResult
    {
        public string CustomerProfileId { get; set; }

        public string CustomerPaymentProfileId { get; set; }

        /// <summary>
        /// For a duplicate: the ID of the existing customer profile or payment profile the gateway matched.
        /// </summary>
        public string DuplicateId { get; set; }

        /// <summary>Response code of the validation authorization (1 approved, 2 declined, 3 error, 4 held).</summary>
        public string ValidationResponseCode { get; set; }

        /// <summary>Response reason code of the validation authorization.</summary>
        public string ValidationReasonCode { get; set; }

        public string ValidationReasonText { get; set; }

        public IList<AuthorizeNetTransactionMessage> Errors { get; set; } = new List<AuthorizeNetTransactionMessage>();

        public bool IsDuplicate => DuplicateId != null;
    }
}
