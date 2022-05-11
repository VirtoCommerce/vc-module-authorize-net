namespace VirtoCommerce.AuthorizeNetPayment.Core.Models
{
    public enum TransactionResponse
    {
        Approved,
        Declined,
        Error,
        HeldForReview,

        UnknownResponse
    }
}
