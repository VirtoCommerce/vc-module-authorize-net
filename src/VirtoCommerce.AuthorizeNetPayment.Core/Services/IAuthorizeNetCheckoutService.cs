using VirtoCommerce.AuthorizeNetPayment.Core.Models;

namespace VirtoCommerce.AuthorizeNetPayment.Core.Services
{
    public interface IAuthorizeNetCheckoutService
    {
        AuthorizeNetCheckoutFormResult GetCheckoutForm(AuthorizeNetCheckoutFormContext context);
    }
}
