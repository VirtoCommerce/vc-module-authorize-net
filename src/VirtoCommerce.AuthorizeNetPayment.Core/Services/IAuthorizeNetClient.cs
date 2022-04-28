using System.Threading.Tasks;
using VirtoCommerce.AuthorizeNetPayment.Core.Models;

namespace VirtoCommerce.AuthorizeNetPayment.Core.Services
{
    public interface IAuthorizeNetClient
    {
        AuthorizeNetAccessTokenResult GetAccessToken(AuthorizeNetAccessTokenRequest request);

        Task<AuthorizeNetAccessTokenResult> GetAccessTokenAsync(AuthorizeNetAccessTokenRequest request);

        AuthorizeNetAccessTransactionResult CreateTransactionRequest(AuthorizeNetAccessTransactionRequest request);

        Task<AuthorizeNetAccessTransactionResult> CreateTransactionRequestAsync(AuthorizeNetAccessTransactionRequest request);
    }
}
