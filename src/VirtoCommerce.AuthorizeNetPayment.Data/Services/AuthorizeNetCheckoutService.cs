using System.IO;
using System.Reflection;
using DotLiquid;
using VirtoCommerce.AuthorizeNetPayment.Core.Models;
using VirtoCommerce.AuthorizeNetPayment.Core.Services;

namespace VirtoCommerce.AuthorizeNetPayment.Data.Services
{
    public class AuthorizeNetCheckoutService : IAuthorizeNetCheckoutService
    {
        public AuthorizeNetCheckoutFormResult GetCheckoutForm(AuthorizeNetCheckoutFormContext context)
        {
            var assembly = Assembly.GetExecutingAssembly();

            string formTemplate;

            using var stream = assembly.GetManifestResourceStream("VirtoCommerce.AuthorizeNetPayment.Data.Form.paymentForm.html");
            using (var reader = new StreamReader(stream))
            {
                formTemplate = reader.ReadToEnd();
            }

            Template template = Template.Parse(formTemplate);
            var formContent = template.Render(Hash.FromAnonymousObject(new
            {
                acceptJsPath = context.AcceptJsPath,
                formAction = context.FormAction,
                clientKey = context.ClientKey,
                apiLogin = context.ApiLogin,
                orderId = context.OrderId,
            }));

            return new AuthorizeNetCheckoutFormResult
            {
                FormContent = formContent,
            };
        }
    }
}
