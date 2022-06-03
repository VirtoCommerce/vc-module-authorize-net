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

        private string ApiLogin => _options.ApiLogin;

        private string TransactionKey => _options.TxnKey;

        private bool IsLiveMode
        {
            get
            {
                var mode = Settings.GetSettingValue(ModuleConstants.Settings.General.Mode.Name,
                    ModuleConstants.Settings.General.Mode.DefaultValue.ToString());
                return mode != ModuleConstants.Test;
            }
        }

        private string AcceptJsPath
        {
            get
            {
                var result = IsLiveMode
                    ? Settings.GetSettingValue(ModuleConstants.Settings.General.AcceptJSProdPath.Name,
                        ModuleConstants.Settings.General.AcceptJSProdPath.DefaultValue.ToString())
                    : Settings.GetSettingValue(ModuleConstants.Settings.General.AcceptJSTestPath.Name,
                        ModuleConstants.Settings.General.AcceptJSTestPath.DefaultValue.ToString());

                return result;
            }
        }

        private string PaymentActionType => Settings.GetSettingValue(ModuleConstants.Settings.General.PaymentActionType.Name,
            ModuleConstants.Settings.General.PaymentActionType.DefaultValue.ToString());

        private string ProcessPaymentAction => Settings.GetSettingValue(ModuleConstants.Settings.General.ProcessPaymentAction.Name,
            ModuleConstants.Settings.General.ProcessPaymentAction.DefaultValue.ToString());


        public override ProcessPaymentRequestResult ProcessPayment(ProcessPaymentRequest request)
        {
            var tokenRequest = new AuthorizeNetTokenRequest
            {
                IsLiveMode = IsLiveMode,
                ApiLogin = ApiLogin,
                TransactionKey = TransactionKey,
            };

            var clientKeyResult = _authorizeNetClient.GetPublicClientKey(tokenRequest);

            var userIp = request.Parameters != null ? request.Parameters["True-Client-IP"] : string.Empty;

            var formContext = new AuthorizeNetCheckoutFormContext
            {
                ClientKey = clientKeyResult.ClientKey,
                ApiLogin = ApiLogin,
                FormAction = ProcessPaymentAction,
                AcceptJsPath = AcceptJsPath,
                OrderId = request.OrderId,
                UserIp = userIp,
            };

            var formContentResult = _authorizeNetCheckoutService.GetCheckoutForm(formContext);

            var result = new ProcessPaymentRequestResult
            {
                IsSuccess = true,
                NewPaymentStatus = PaymentStatus.Pending,
                HtmlForm = formContentResult.FormContent,
                PublicParameters = new()
                {
                    { "acceptJsPath", AcceptJsPath },
                    { "apiLogin", ApiLogin },
                    { "clientKey", clientKeyResult.ClientKey },
                }
            };

            var payment = request.GetPayment();
            payment.PaymentStatus = PaymentStatus.Pending;
            payment.Status = payment.PaymentStatus.ToString();

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

            var transactionRequest = new AuthorizeNetCreateTransactionRequest
            {
                IsLiveMode = IsLiveMode,
                ApiLogin = ApiLogin,
                TransactionKey = TransactionKey,
                PaymentActionType = PaymentActionType,
                DataDescriptor = dataDescriptor,
                DataValue = dataValue,
                Amount = payment.Sum,
                CurrencyCode = payment.Currency,
                OrderId = order.Id,
                OrderNumber = order.Number,
            };

            var transactionResult = _authorizeNetClient.CreateTransaction(transactionRequest);
            var result = ProcessCreateTransactionResult(transactionResult, payment, order);

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
            var payment = context.GetPayment();

            var transactionRequest = new AuthorizeNetCaptureTransactionRequest
            {
                IsLiveMode = IsLiveMode,
                ApiLogin = ApiLogin,
                TransactionKey = TransactionKey,
                TransactionAmount = payment.Sum,
                TransactionId = payment.OuterId,
            };

            var captureResult = _authorizeNetClient.CaptureTransaction(transactionRequest);

            var result = new CapturePaymentRequestResult();

            if (captureResult.TransactionResponse == TransactionResponse.Approved)
            {
                result.IsSuccess = true;
                result.NewPaymentStatus = payment.PaymentStatus = PaymentStatus.Paid;
                payment.Status = payment.PaymentStatus.ToString();
                payment.IsApproved = true;
                payment.CapturedDate = DateTime.UtcNow;
            }
            else
            {
                throw new InvalidOperationException($"{captureResult.TransactionResponse} ({captureResult.TransactionMessage.Code}:{captureResult.TransactionMessage.Description})");
            }

            return result;
        }

        public override RefundPaymentRequestResult RefundProcessPayment(RefundPaymentRequest context)
        {
            var payment = context.GetPayment();

            if (payment.IsApproved && payment.PaymentStatus != PaymentStatus.Paid)
            {
                throw new InvalidOperationException("Only settled payments can be refunded");
            }

            if (string.IsNullOrEmpty(payment.OuterId))
            {
                throw new InvalidOperationException("Transaction ID is empty.");
            }

            var transactionDetailsRequest = new AuthorizeNetTransactionRequest
            {
                IsLiveMode = IsLiveMode,
                ApiLogin = ApiLogin,
                TransactionKey = TransactionKey,
                TransactionId = payment.OuterId,
            };

            var transactionDetails = _authorizeNetClient.GetTransactionDetails(transactionDetailsRequest);

            // test on null or invalid transaction id
            var result = new RefundPaymentRequestResult();

            if (transactionDetails.IsSettled)
            {
                var transactionRequest = new AuthorizeNetRefundTransactionRequest
                {
                    IsLiveMode = IsLiveMode,
                    ApiLogin = ApiLogin,
                    TransactionKey = TransactionKey,
                    TransactionId = payment.OuterId,
                    TransactionAmount = payment.Sum,
                    PaymentData = transactionDetails.PaymentData,
                };

                var refundTransactionResult = _authorizeNetClient.RefundTransaction(transactionRequest);

                if (refundTransactionResult.TransactionResponse == TransactionResponse.Approved)
                {
                    result.NewPaymentStatus = payment.PaymentStatus = PaymentStatus.Refunded;
                    payment.Status = payment.PaymentStatus.ToString();
                    payment.VoidedDate = DateTime.UtcNow;
                }
                else
                {
                    result.ErrorMessage = refundTransactionResult.TransactionMessage?.Description;
                }
            }
            else
            {
                var order = context.GetOrder();
                var voidRequest = new VoidPaymentRequest
                {
                    PaymentId = payment.Id,
                    Payment = payment,
                    OrderId = order.Id,
                    Order = order,
                };

                var voidReult = VoidProcessPayment(voidRequest);

                result.IsSuccess = voidReult.IsSuccess;
                result.NewPaymentStatus = voidReult.NewPaymentStatus;
                result.ErrorMessage = voidReult.ErrorMessage;
            }

            return result;
        }

        public override VoidPaymentRequestResult VoidProcessPayment(VoidPaymentRequest request)
        {
            var payment = request.GetPayment();

            if (payment.PaymentStatus != PaymentStatus.Authorized && payment.PaymentStatus != PaymentStatus.Paid)
            {
                throw new InvalidOperationException("Only authorized payments can be voided");
            }

            var transactionRequest = new AuthorizeNetVoidTransactionRequest
            {
                IsLiveMode = IsLiveMode,
                ApiLogin = ApiLogin,
                TransactionKey = TransactionKey,
                TransactionId = payment.OuterId,
            };

            var voidTransactionResult = _authorizeNetClient.VoidTransaction(transactionRequest);

            var result = new VoidPaymentRequestResult();

            if (voidTransactionResult.TransactionResponse == TransactionResponse.Approved)
            {
                result.IsSuccess = true;
                result.NewPaymentStatus = payment.PaymentStatus = PaymentStatus.Voided;

                payment.IsCancelled = true;
                payment.Status = PaymentStatus.Voided.ToString();
                payment.VoidedDate = payment.CancelledDate = DateTime.UtcNow;
            }
            else
            {
                result.ErrorMessage = voidTransactionResult.TransactionMessage?.Description;
            }

            return result;
        }


        private PostProcessPaymentRequestResult ProcessCreateTransactionResult(AuthorizeNetTransactionResult transactionResult, PaymentIn payment, CustomerOrder order) => transactionResult.TransactionResponse switch
        {
            TransactionResponse.Approved => ProcessApprovedResult(transactionResult, payment, order),
            TransactionResponse.Declined => ProcessDeclinedResult(transactionResult, payment),
            TransactionResponse.Error => ProcessErrorResult(transactionResult, payment),
            TransactionResponse.HeldForReview => ProcessHeldResult(transactionResult, payment),
            _ => new PostProcessPaymentRequestResult { ErrorMessage = transactionResult.TransactionMessage.Description },
        };

        private PostProcessPaymentRequestResult ProcessApprovedResult(AuthorizeNetTransactionResult transactionResult, PaymentIn payment, CustomerOrder order)
        {
            var result = new PostProcessPaymentRequestResult();

            var transactionMessage = transactionResult.TransactionMessage;

            if (PaymentActionType == ModuleConstants.Sale)
            {
                result.NewPaymentStatus = payment.PaymentStatus = PaymentStatus.Paid;
                payment.Status = payment.PaymentStatus.ToString();
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
                    Status = transactionMessage.Description,
                    ResponseCode = transactionMessage.Code,
                    ResponseData = $"Account number {transactionResult.AccountNumber}",
                };

                payment.Transactions.Add(paymentTransaction);
            }

            if (PaymentActionType == ModuleConstants.AuthCapture)
            {
                result.NewPaymentStatus = payment.PaymentStatus = PaymentStatus.Authorized;
                payment.Status = payment.PaymentStatus.ToString();
            }

            result.IsSuccess = true;
            result.OrderId = order.Id;
            result.OuterId = payment.OuterId = transactionResult.TransactionId;

            order.Status = "Processing";
            payment.AuthorizedDate = DateTime.UtcNow;

            return result;
        }

        private static PostProcessPaymentRequestResult ProcessDeclinedResult(AuthorizeNetTransactionResult transactionResult, PaymentIn payment)
        {
            var transactionMessage = transactionResult.TransactionMessage.Description;

            payment.Status = PaymentStatus.Declined.ToString();
            payment.ProcessPaymentResult = new ProcessPaymentRequestResult
            {
                ErrorMessage = $"Your transaction was declined: {transactionMessage}",
            };
            payment.Comment = $"{payment.ProcessPaymentResult.ErrorMessage}{Environment.NewLine}";

            return new PostProcessPaymentRequestResult { ErrorMessage = payment.ProcessPaymentResult.ErrorMessage };
        }

        private static PostProcessPaymentRequestResult ProcessErrorResult(AuthorizeNetTransactionResult transactionResult, PaymentIn payment)
        {
            var transactionMessage = transactionResult.TransactionMessage;

            payment.Status = PaymentStatus.Error.ToString();
            payment.ProcessPaymentResult = new ProcessPaymentRequestResult
            {
                ErrorMessage = $"There was an error processing your transaction: {transactionMessage}",
            };
            payment.Comment = $"{payment.ProcessPaymentResult.ErrorMessage}{Environment.NewLine}";

            return new PostProcessPaymentRequestResult { ErrorMessage = payment.ProcessPaymentResult.ErrorMessage };
        }

        private static PostProcessPaymentRequestResult ProcessHeldResult(AuthorizeNetTransactionResult transactionResult, PaymentIn payment)
        {
            var transactionMessage = transactionResult.TransactionMessage;

            payment.ProcessPaymentResult = new ProcessPaymentRequestResult
            {
                ErrorMessage = $"Your transaction was held for review: {transactionMessage}",
            };
            payment.Comment = $"{payment.ProcessPaymentResult.ErrorMessage}{Environment.NewLine}";

            return new PostProcessPaymentRequestResult { ErrorMessage = payment.ProcessPaymentResult.ErrorMessage };
        }
    }
}
