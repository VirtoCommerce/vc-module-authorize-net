namespace VirtoCommerce.AuthorizeNetPayment.Core.Models
{
    public class AuthorizeNetPaymentMethodOptions
    {
        /// <summary>
        /// AuthorizeNET API Login ID
        /// </summary>
        public string ApiLogin { get; set; }

        /// <summary>
        /// AuthorizeNET API Transaction Key
        /// </summary>
        public string TxnKey { get; set; }
    }
}
