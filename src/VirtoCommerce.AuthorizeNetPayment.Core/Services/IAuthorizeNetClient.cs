using System.Threading.Tasks;
using VirtoCommerce.AuthorizeNetPayment.Core.Models;

namespace VirtoCommerce.AuthorizeNetPayment.Core.Services
{
    public interface IAuthorizeNetClient
    {
        /// <summary>
        /// Generates Public Client Key for the merchant to be used in AcceptJS form. 
        /// </summary>
        AuthorizeNetTokenResult GetPublicClientKey(AuthorizeNetTokenRequest request);

        Task<AuthorizeNetTokenResult> GetPublicClientKeyAsync(AuthorizeNetTokenRequest request);

        /// <summary>
        /// Gets information about a specific transaction by Transaction ID.
        /// </summary>
        AuthorizeNetTransactionResult GetTransactionDetails(AuthorizeNetTransactionRequest request);

        Task<AuthorizeNetTransactionResult> GetTransactionDetailsAsync(AuthorizeNetTransactionRequest request);

        /// <summary>
        /// Create an Authorize.net payment transaction request, using the Accept Payment nonce in place of card data.
        /// When using "Sale" PaymentActionType the transaction is automatically submitted for settlement.
        /// When using "Authorize/Capture" PaymentActionType the transaction amount is sent for authorization only.
        /// The transaction is not settled until captured by CaptureTransaction.
        /// </summary>
        AuthorizeNetTransactionResult CreateTransaction(AuthorizeNetCreateTransactionRequest request);

        Task<AuthorizeNetTransactionResult> CreateTransactionAsync(AuthorizeNetCreateTransactionRequest request);

        /// <summary>
        /// Captures the previously authorized transaction and queues it for settlement.
        /// </summary>
        AuthorizeNetTransactionResult CaptureTransaction(AuthorizeNetCaptureTransactionRequest request);

        Task<AuthorizeNetTransactionResult> CaptureTransactionAsync(AuthorizeNetCaptureTransactionRequest request);

        /// <summary>
        /// Refunds the specified transaction after settlement. Creates a new and distinct transaction from the original charge with its own unique transaction ID.
        /// </summary>
        AuthorizeNetTransactionResult RefundTransaction(AuthorizeNetRefundTransactionRequest request);

        Task<AuthorizeNetTransactionResult> RefundTransactionAsync(AuthorizeNetRefundTransactionRequest request);

        /// <summary>
        /// Cancells the specified transaction before settlement. Once a transaction settles, it cannot be voided.
        /// </summary>
        AuthorizeNetTransactionResult VoidTransaction(AuthorizeNetVoidTransactionRequest request);

        Task<AuthorizeNetTransactionResult> VoidTransactionAsync(AuthorizeNetVoidTransactionRequest request);

        /// <summary>
        /// Finds the customer profile with this merchant customer ID; null when there is none.
        /// </summary>
        Task<string> FindCustomerProfileIdAsync(AuthorizeNetCustomerLookupRequest request);

        /// <summary>
        /// Saves a card from an Accept.js nonce in the Customer Information Manager, optionally validated with an authorization.
        /// </summary>
        Task<AuthorizeNetCustomerProfileResult> CreateCustomerProfileAsync(AuthorizeNetCustomerProfileRequest request);

        /// <summary>
        /// Saves the card of an approved transaction in the Customer Information Manager.
        /// </summary>
        Task<AuthorizeNetCustomerProfileResult> CreateCustomerProfileFromTransactionAsync(AuthorizeNetProfileFromTransactionRequest request);

        /// <summary>
        /// Gets a stored card with its unmasked expiry date.
        /// </summary>
        Task<AuthorizeNetPaymentProfileResult> GetCustomerPaymentProfileAsync(AuthorizeNetPaymentProfileRequest request);

        /// <summary>
        /// Deletes a stored card. A card that no longer exists counts as deleted.
        /// </summary>
        Task<AuthorizeNetBaseResult> DeleteCustomerPaymentProfileAsync(AuthorizeNetPaymentProfileRequest request);
    }
}
