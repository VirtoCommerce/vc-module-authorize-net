using System;
using System.Collections.Specialized;
using System.Linq;
using Microsoft.Extensions.Options;
using VirtoCommerce.AuthorizeNetPayment.Core;
using VirtoCommerce.AuthorizeNetPayment.Core.Models;
using VirtoCommerce.AuthorizeNetPayment.Core.Services;
using VirtoCommerce.OrdersModule.Core.Model;
using VirtoCommerce.PaymentModule.Core.Model;
using VirtoCommerce.PaymentModule.Model.Requests;
using VirtoCommerce.Platform.Core.Settings;

namespace VirtoCommerce.AuthorizeNetPayment.Data.Providers
{
    public class AuthorizeNetPaymentMethod : PaymentMethod
    {
        private readonly string _dataDescriptorParamName = "dataDescriptor";
        private readonly string _dataValueParamName = "dataValue";

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

            var payment = (PaymentIn)request.Payment;
            payment.PaymentStatus = PaymentStatus.Pending;

            return result;
        }

        public override PostProcessPaymentRequestResult PostProcessPayment(PostProcessPaymentRequest request)
        {
            var result = new PostProcessPaymentRequestResult();

            var dataDescriptor = request.Parameters?.Get(_dataDescriptorParamName);
            if (dataDescriptor == null)
            {
                result.ErrorMessage = "No Authorize.NET data descripor present";
                return result;
            }

            var dataValue = request.Parameters?.Get(_dataValueParamName);
            if (dataValue == null)
            {
                result.ErrorMessage = "No Authorize.NET Payment Nonce present";
                return result;
            }

            var payment = (PaymentIn)request.Payment;
            var order = (CustomerOrder)request.Order;

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

            var transactionMessage = transactionResult.TransactionMessages.FirstOrDefault();

            switch (transactionResult.TransactionResponse)
            {
                case TransactionResponse.Approved:
                    result.NewPaymentStatus = payment.PaymentStatus = PaymentStatus.Paid;
                    payment.IsApproved = true;
                    payment.CapturedDate = DateTime.UtcNow;
                    payment.Comment = $"Paid successfully. Transaction Info {transactionResult.TransactionId}{Environment.NewLine}";
                    payment.Transactions.Add(new PaymentGatewayTransaction
                    {
                        IsProcessed = true,
                        ProcessedDate = DateTime.UtcNow,
                        Note = $"Transaction ID: {transactionResult.TransactionId}",
                        ResponseCode = transactionMessage?.Code,
                        ResponseData = transactionMessage?.Description,
                        CurrencyCode = payment.Currency,
                        Amount = payment.Sum,
                    });

                    result.IsSuccess = true;
                    result.OrderId = order.Id;
                    result.OuterId = payment.OuterId = transactionResult.TransactionId;
                    payment.AuthorizedDate = DateTime.UtcNow;

                    break;
                case TransactionResponse.Declined:
                    if (payment.PaymentStatus != PaymentStatus.Paid)
                    {
                        result.IsSuccess = false;

                        payment.Status = PaymentStatus.Declined.ToString();

                        var message = $"Your transaction was declined - {transactionMessage?.Description} ({transactionMessage?.Code})";
                        payment.ProcessPaymentResult = new ProcessPaymentRequestResult
                        {
                            ErrorMessage = message,
                        };
                        payment.Comment = $"{message}{Environment.NewLine}";
                    }

                    break;
                default:
                    if (payment.PaymentStatus != PaymentStatus.Paid)
                    {
                        result.IsSuccess = false;

                        payment.Status = PaymentStatus.Error.ToString();

                        var message = $"There was an error processing your transaction - {transactionMessage?.Description} ({transactionMessage?.Code})";
                        payment.ProcessPaymentResult = new ProcessPaymentRequestResult
                        {
                            ErrorMessage = message,
                        };
                        payment.Comment = $"{message}{Environment.NewLine}";
                    }

                    break;
            }

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
