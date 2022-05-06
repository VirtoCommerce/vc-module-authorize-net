using System.Threading.Tasks;
using VirtoCommerce.AuthorizeNetPayment.Core.Models;

namespace VirtoCommerce.AuthorizeNetPayment.Core.Services
{
    public interface IAuthorizeNetClient
    {
        AuthorizeNetTokenResult GetAccessToken(AuthorizeNetTokenRequest request);

        Task<AuthorizeNetTokenResult> GetAccessTokenAsync(AuthorizeNetTokenRequest request);

        AuthorizeNetTransactionResult GetTransactionDetails(AuthorizeNetTransactionRequest request);

        Task<AuthorizeNetTransactionResult> GetTransactionDetailsAsync(AuthorizeNetTransactionRequest request);

        AuthorizeNetTransactionResult CreateTransaction(AuthorizeNetCreateTransactionRequest request);

        Task<AuthorizeNetTransactionResult> CreateTransactionAsync(AuthorizeNetCreateTransactionRequest request);

        AuthorizeNetTransactionResult CaptureTransaction(AuthorizeNetCaptureTransactionRequest request);

        Task<AuthorizeNetTransactionResult> CaptureTransactionAsync(AuthorizeNetCaptureTransactionRequest request);

        AuthorizeNetTransactionResult RefundTransaction(AuthorizeNetRefundTransactionRequest request);

        Task<AuthorizeNetTransactionResult> RefundTransactionAsync(AuthorizeNetRefundTransactionRequest request);

        AuthorizeNetTransactionResult VoidTransaction(AuthorizeNetVoidTransactionRequest request);

        Task<AuthorizeNetTransactionResult> VoidTransactionAsync(AuthorizeNetVoidTransactionRequest request);
    }
}
