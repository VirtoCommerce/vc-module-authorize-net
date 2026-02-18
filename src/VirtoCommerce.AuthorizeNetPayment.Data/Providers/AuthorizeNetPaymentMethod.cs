using System;
using System.Collections.Specialized;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
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
    public class AuthorizeNetPaymentMethod : PaymentMethod, ISupportCaptureFlow, ISupportRefundFlow
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
                var mode = Settings.GetValue<string>(ModuleConstants.Settings.General.Mode);
                return mode != ModuleConstants.Test;
            }
        }

        private string AcceptJsPath
        {
            get
            {
                var result = IsLiveMode
                    ? Settings.GetValue<string>(ModuleConstants.Settings.General.AcceptJSProdPath)
                    : Settings.GetValue<string>(ModuleConstants.Settings.General.AcceptJSTestPath);

                return result;
            }
        }

        private string PaymentActionType => Settings.GetValue<string>(ModuleConstants.Settings.General.PaymentActionType);

        private string ProcessPaymentAction => Settings.GetValue<string>(ModuleConstants.Settings.General.ProcessPaymentAction);


        public override async Task<ProcessPaymentRequestResult> ProcessPaymentAsync(ProcessPaymentRequest request, CancellationToken cancellationToken = default)
        {
            var tokenRequest = new AuthorizeNetTokenRequest
            {
                IsLiveMode = IsLiveMode,
                ApiLogin = ApiLogin,
                TransactionKey = TransactionKey,
            };

            var clientKeyResult = await _authorizeNetClient.GetPublicClientKeyAsync(tokenRequest);

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

        public override async Task<PostProcessPaymentRequestResult> PostProcessPaymentAsync(PostProcessPaymentRequest request, CancellationToken cancellationToken = default)
        {
            var dataDescriptor = request.Parameters.Get(ModuleConstants.DataDescriptorParamName);
            var dataValue = request.Parameters.Get(ModuleConstants.DataValueParamName);

            if ((dataDescriptor == null || dataValue == null) && request.Parameters["CreditCard"] == null)
            {
                return new PostProcessPaymentRequestResult
                {
                    ErrorMessage = "No valid Authorize.NET response present.",
                };
            }

            var payment = request.GetPayment();
            var order = request.GetOrder();

            AuthorizeNetCreditCard creditCard = null;
            if (request.Parameters["CreditCard"] != null)
            {
                var tokenizedCard = JsonConvert.DeserializeObject<dynamic>(request.Parameters["CreditCard"]);
                creditCard = new AuthorizeNetCreditCard
                {
                    CardCode = tokenizedCard.Cvv,
                    CardNumber = tokenizedCard.CardNumber,
                    CardExpiration = tokenizedCard.CardExpiration,
                    ProxyEndpointUrl = request.Parameters["ProxyEndpointUrl"],
                    ProxyHttpClientName = request.Parameters["ProxyHttpClientName"],
                };
            }

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
                CreditCard = creditCard,
            };

            var transactionResult = await _authorizeNetClient.CreateTransactionAsync(transactionRequest);
            var result = ProcessCreateTransactionResult(transactionResult, payment, order);

            return result;
        }

        public override Task<ValidatePostProcessRequestResult> ValidatePostProcessRequestAsync(NameValueCollection queryString, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new ValidatePostProcessRequestResult
            {
                IsSuccess = true,
            });
        }

        public override async Task<CapturePaymentRequestResult> CaptureProcessPaymentAsync(CapturePaymentRequest request, CancellationToken cancellationToken = default)
        {
            var payment = request.GetPayment();

            var transactionRequest = new AuthorizeNetCaptureTransactionRequest
            {
                IsLiveMode = IsLiveMode,
                ApiLogin = ApiLogin,
                TransactionKey = TransactionKey,
                TransactionAmount = payment.Sum,
                TransactionId = payment.OuterId,
            };

            var captureResult = await _authorizeNetClient.CaptureTransactionAsync(transactionRequest);

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

        public override async Task<RefundPaymentRequestResult> RefundProcessPaymentAsync(RefundPaymentRequest request, CancellationToken cancellationToken = default)
        {
            var payment = request.GetPayment();

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

            var transactionDetails = await _authorizeNetClient.GetTransactionDetailsAsync(transactionDetailsRequest);

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

                var refundTransactionResult = await _authorizeNetClient.RefundTransactionAsync(transactionRequest);

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
                var order = request.GetOrder();
                var voidRequest = new VoidPaymentRequest
                {
                    PaymentId = payment.Id,
                    Payment = payment,
                    OrderId = order.Id,
                    Order = order,
                };

                var voidReult = await VoidProcessPaymentAsync(voidRequest, cancellationToken);

                result.IsSuccess = voidReult.IsSuccess;
                result.NewPaymentStatus = voidReult.NewPaymentStatus;
                result.ErrorMessage = voidReult.ErrorMessage;
            }

            return result;
        }

        public override async Task<VoidPaymentRequestResult> VoidProcessPaymentAsync(VoidPaymentRequest request, CancellationToken cancellationToken = default)
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

            var voidTransactionResult = await _authorizeNetClient.VoidTransactionAsync(transactionRequest);

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
            var transactionMessage = transactionResult.TransactionMessage.Description;

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
            var transactionMessage = transactionResult.TransactionMessage.Description;

            payment.ProcessPaymentResult = new ProcessPaymentRequestResult
            {
                ErrorMessage = $"Your transaction was held for review: {transactionMessage}",
            };
            payment.Comment = $"{payment.ProcessPaymentResult.ErrorMessage}{Environment.NewLine}";

            return new PostProcessPaymentRequestResult { ErrorMessage = payment.ProcessPaymentResult.ErrorMessage };
        }
    }
}
