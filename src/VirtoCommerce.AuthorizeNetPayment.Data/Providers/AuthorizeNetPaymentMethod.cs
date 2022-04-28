using System;
using System.Collections.Specialized;
using Microsoft.Extensions.Options;
using VirtoCommerce.AuthorizeNetPayment.Core;
using VirtoCommerce.AuthorizeNetPayment.Core.Models;
using VirtoCommerce.AuthorizeNetPayment.Core.Services;
using VirtoCommerce.AuthorizeNetPayment.Data.Extensions;
using VirtoCommerce.OrdersModule.Core.Model;
using VirtoCommerce.PaymentModule.Core.Model;
using VirtoCommerce.PaymentModule.Model.Requests;
using VirtoCommerce.Platform.Core.Settings;

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

        public string ApiLogin => _options.ApiLogin;

        public string TransactionKey => _options.TxnKey;

        private bool IsLiveMode
        {
            get
            {
                var mode = Settings.GetSettingValue(ModuleConstants.Settings.General.Mode.Name, ModuleConstants.Settings.General.Mode.DefaultValue.ToString());
                return mode != ModuleConstants.Test;
            }
        }

        private string AcceptJsPath
        {
            get
            {
                var result = IsLiveMode
                    ? Settings.GetSettingValue(ModuleConstants.Settings.General.AcceptJSProdPath.Name, ModuleConstants.Settings.General.AcceptJSProdPath.DefaultValue.ToString())
                    : Settings.GetSettingValue(ModuleConstants.Settings.General.AcceptJSTestPath.Name, ModuleConstants.Settings.General.AcceptJSTestPath.DefaultValue.ToString());

                return result;
            }
        }

        private string ProcessPaymentAction => Settings.GetSettingValue(ModuleConstants.Settings.General.ProcessPaymentAction.Name,
            ModuleConstants.Settings.General.ProcessPaymentAction.DefaultValue.ToString());


        public override ProcessPaymentRequestResult ProcessPayment(ProcessPaymentRequest request)
        {
            var tokenRequest = new AuthorizeNetAccessTokenRequest
            {
                IsLiveMode = IsLiveMode,
                ApiLogin = ApiLogin,
                TransactionKey = TransactionKey,
            };

            var clientKeyResult = _authorizeNetClient.GetAccessToken(tokenRequest);

            var formContext = new AuthorizeNetCheckoutFormContext
            {
                ClientKey = clientKeyResult.ClientKey,
                ApiLogin = ApiLogin,
                FormAction = ProcessPaymentAction,
                AcceptJsPath = AcceptJsPath,
                OrderId = request.OrderId,
            };

            var formContentResult = _authorizeNetCheckoutService.GetCheckoutForm(formContext);

            var result = new ProcessPaymentRequestResult
            {
                IsSuccess = true,
                NewPaymentStatus = PaymentStatus.Pending,
                HtmlForm = formContentResult.FormContent,
            };

            var payment = request.GetPayment();
            payment.PaymentStatus = PaymentStatus.Pending;

            return result;
        }

        public override PostProcessPaymentRequestResult PostProcessPayment(PostProcessPaymentRequest request)
        {
            var dataDescriptor = request.Parameters.Get(ModuleConstants.DataDescriptorParamName);
            var dataValue = request.Parameters.Get(ModuleConstants.DataValueParamName);

            if (dataDescriptor == null || dataValue == null)
            {
                return new PostProcessPaymentRequestResult
                {
                    ErrorMessage = "No valid Authorize.NET response present.",
                };
            }

            var payment = request.GetPayment();
            var order = request.GetOrder();

            var transactionRequest = new AuthorizeNetAccessTransactionRequest
            {
                IsLiveMode = IsLiveMode,
                ApiLogin = ApiLogin,
                TransactionKey = TransactionKey,
                DataDescriptor = dataDescriptor,
                DataValue = dataValue,
                Amount = payment.Sum,
                CurrencyCode = payment.Currency,
                OrderId = order.Id,
                OrderNumber = order.Number,
            };

            var transactionResult = _authorizeNetClient.CreateTransactionRequest(transactionRequest);
            var result = ProcessResult(transactionResult, payment, order);

            return result;
        }

        public override ValidatePostProcessRequestResult ValidatePostProcessRequest(NameValueCollection queryString)
        {
            return new ValidatePostProcessRequestResult
            {
                IsSuccess = true,
            };
        }

        public override CapturePaymentRequestResult CaptureProcessPayment(CapturePaymentRequest context)
        {
            throw new NotImplementedException();
        }

        public override RefundPaymentRequestResult RefundProcessPayment(RefundPaymentRequest context)
        {
            throw new NotImplementedException();
        }

        public override VoidPaymentRequestResult VoidProcessPayment(VoidPaymentRequest request)
        {
            throw new NotImplementedException();
        }

        private static PostProcessPaymentRequestResult ProcessResult(AuthorizeNetAccessTransactionResult transactionResult, PaymentIn payment, CustomerOrder order)
        {
            return transactionResult.TransactionResponse switch
            {
                TransactionResponse.Approved => ProcessApprovedResult(transactionResult, payment, order),
                TransactionResponse.Declined => ProcessDeclinedResult(transactionResult, payment),
                TransactionResponse.Error => ProcessErrorResult(transactionResult, payment),
                TransactionResponse.HeldForReview => ProcessHeldResult(transactionResult, payment),
                _ => new PostProcessPaymentRequestResult { ErrorMessage = "Unknown transaction status." },
            };
        }

        private static PostProcessPaymentRequestResult ProcessApprovedResult(AuthorizeNetAccessTransactionResult transactionResult, PaymentIn payment, CustomerOrder order)
        {
            var result = new PostProcessPaymentRequestResult();

            var transactionMessage = transactionResult.TransactionMessage;

            result.NewPaymentStatus = payment.PaymentStatus = PaymentStatus.Paid;
            payment.IsApproved = true;
            payment.CapturedDate = DateTime.UtcNow;
            payment.Comment = $"Paid successfully. Transaction Info {transactionResult.TransactionId}{Environment.NewLine}";

            var paymentTransaction = new PaymentGatewayTransaction
            {
                IsProcessed = true,
                ProcessedDate = DateTime.UtcNow,
                CurrencyCode = payment.Currency,
                Amount = payment.Sum,
                Note = $"Transaction ID: {transactionResult.TransactionId}",
                ResponseCode = transactionMessage.Code,
                ResponseData = transactionMessage.Description,
            };

            payment.Transactions.Add(paymentTransaction);

            result.IsSuccess = true;
            result.OrderId = order.Id;
            result.OuterId = payment.OuterId = transactionResult.TransactionId;
            payment.AuthorizedDate = DateTime.UtcNow;

            return result;
        }

        private static PostProcessPaymentRequestResult ProcessDeclinedResult(AuthorizeNetAccessTransactionResult transactionResult, PaymentIn payment)
        {
            var transactionMessage = transactionResult.TransactionMessage;

            payment.Status = PaymentStatus.Declined.ToString();
            payment.ProcessPaymentResult = new ProcessPaymentRequestResult
            {
                ErrorMessage = $"Your transaction was declined: {transactionMessage.Description} ({transactionMessage.Code})",
            };
            payment.Comment = $"{payment.ProcessPaymentResult.ErrorMessage}{Environment.NewLine}";

            return new PostProcessPaymentRequestResult();
        }

        private static PostProcessPaymentRequestResult ProcessErrorResult(AuthorizeNetAccessTransactionResult transactionResult, PaymentIn payment)
        {
            var transactionMessage = transactionResult.TransactionMessage;

            payment.Status = PaymentStatus.Error.ToString();
            payment.ProcessPaymentResult = new ProcessPaymentRequestResult
            {
                ErrorMessage = $"There was an error processing your transaction: {transactionMessage.Description} ({transactionMessage.Code})",
            };
            payment.Comment = $"{payment.ProcessPaymentResult.ErrorMessage}{Environment.NewLine}";

            return new PostProcessPaymentRequestResult();
        }

        private static PostProcessPaymentRequestResult ProcessHeldResult(AuthorizeNetAccessTransactionResult transactionResult, PaymentIn payment)
        {
            var transactionMessage = transactionResult.TransactionMessage;

            payment.ProcessPaymentResult = new ProcessPaymentRequestResult
            {
                ErrorMessage = $"Your transaction was held for review: {transactionMessage.Description} ({transactionMessage.Code})",
            };
            payment.Comment = $"{payment.ProcessPaymentResult.ErrorMessage}{Environment.NewLine}";

            return new PostProcessPaymentRequestResult();
        }
    }
}
