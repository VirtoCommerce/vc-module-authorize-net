using System.Collections.Specialized;
using Microsoft.Extensions.Options;
using VirtoCommerce.AuthorizeNetPayment.Core.Models;
using VirtoCommerce.AuthorizeNetPayment.Core.Services;
using VirtoCommerce.PaymentModule.Core.Model;
using VirtoCommerce.PaymentModule.Model.Requests;

namespace VirtoCommerce.AuthorizeNetPayment.Data.Providers
{
    public class AuthorizeNetPaymentMethod : PaymentMethod
    {
        private readonly IAuthorizeNetClient _authorizeNetClient;
        private readonly IAuthorizeNetCheckoutService _authorizeNetCheckoutService;
        private readonly AuthorizeNetPaymentMethodOptions _options;

        public AuthorizeNetPaymentMethod(
            IAuthorizeNetClient authorizeNetClient,
            IAuthorizeNetCheckoutService authorizeNetCheckoutService,
            IOptions<AuthorizeNetPaymentMethodOptions> options)
            : base(nameof(AuthorizeNetPaymentMethod))
        {
            _options = options?.Value ?? new AuthorizeNetPaymentMethodOptions();
            _authorizeNetClient = authorizeNetClient;
            _authorizeNetCheckoutService = authorizeNetCheckoutService;
        }

        public override PaymentMethodGroupType PaymentMethodGroupType => PaymentMethodGroupType.Alternative;

        public override PaymentMethodType PaymentMethodType => PaymentMethodType.PreparedForm;

        public override ProcessPaymentRequestResult ProcessPayment(ProcessPaymentRequest request)
        {
            throw new System.NotImplementedException();
        }

        public override PostProcessPaymentRequestResult PostProcessPayment(PostProcessPaymentRequest request)
        {
            throw new System.NotImplementedException();
        }

        public override ValidatePostProcessRequestResult ValidatePostProcessRequest(NameValueCollection queryString)
        {
            throw new System.NotImplementedException();
        }

        public override CapturePaymentRequestResult CaptureProcessPayment(CapturePaymentRequest context)
        {
            throw new System.NotImplementedException();
        }

        public override RefundPaymentRequestResult RefundProcessPayment(RefundPaymentRequest context)
        {
            throw new System.NotImplementedException();
        }

        public override VoidPaymentRequestResult VoidProcessPayment(VoidPaymentRequest request)
        {
            throw new System.NotImplementedException();
        }
    }
}
