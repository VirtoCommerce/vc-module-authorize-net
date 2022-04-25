using VirtoCommerce.AuthorizeNetPayment.Core.Models;

namespace VirtoCommerce.AuthorizeNetPayment.Core.Services
{
    public interface IAuthorizeNetCheckoutService
    {
        string GetCheckoutFormContent(AuthorizeNetCheckoutContext context);
    }
}
