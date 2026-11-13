using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using VirtoCommerce.AuthorizeNetPayment.Core;
using VirtoCommerce.AuthorizeNetPayment.Core.Models;
using VirtoCommerce.AuthorizeNetPayment.Core.Services;
using VirtoCommerce.AuthorizeNetPayment.Data.Extensions;
using VirtoCommerce.AuthorizeNetPayment.Data.Services;
using VirtoCommerce.OrdersModule.Core.Model;
using VirtoCommerce.PaymentModule.Core.Model;
using VirtoCommerce.PaymentModule.Core.Model.StoredCredentials;
using VirtoCommerce.PaymentModule.Model.Requests;
using VirtoCommerce.Platform.Core.Common;
using VirtoCommerce.Platform.Core.Settings;

namespace VirtoCommerce.AuthorizeNetPayment.Data.Providers
{
    /// <summary>
    /// Authorize.Net payment method. For stored credentials it only talks to Authorize.Net: it saves cards in the Customer
    /// Information Manager and returns them, and charges the saved card it is given. It never reads or writes the
    /// platform's saved cards; IStoredPaymentMethodManager of the Payment module does that for its callers.
    /// </summary>
    public class AuthorizeNetPaymentMethod : PaymentMethod, ISupportCaptureFlow, ISupportRefundFlow, ISupportStoredCredentialFlow
    {
        private readonly IAuthorizeNetClient _authorizeNetClient;
        private readonly IAuthorizeNetCheckoutService _authorizeNetCheckoutService;
        private readonly ILogger<AuthorizeNetPaymentMethod> _logger;
        private readonly AuthorizeNetPaymentMethodOptions _options;

        public AuthorizeNetPaymentMethod(
            IAuthorizeNetClient authorizeNetClient,
            IAuthorizeNetCheckoutService authorizeNetCheckoutService,
            IOptions<AuthorizeNetPaymentMethodOptions> options,
            ILogger<AuthorizeNetPaymentMethod> logger)
            : base(nameof(AuthorizeNetPaymentMethod))
        {
            _options = options?.Value ?? new AuthorizeNetPaymentMethodOptions();
            _authorizeNetClient = authorizeNetClient;
            _authorizeNetCheckoutService = authorizeNetCheckoutService;
            _logger = logger;
        }

        public override PaymentMethodGroupType PaymentMethodGroupType => PaymentMethodGroupType.Alternative;

        public override PaymentMethodType PaymentMethodType => PaymentMethodType.PreparedForm;

        public override bool AllowCartPayment => true;

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

            if (request.GetPayment() is { } payment)
            {
                payment.PaymentStatus = PaymentStatus.Pending;
                payment.Status = payment.PaymentStatus.ToString();
            }

            return result;
        }

        public override async Task<PostProcessPaymentRequestResult> PostProcessPaymentAsync(PostProcessPaymentRequest request, CancellationToken cancellationToken = default)
        {
            if (request.StoredPaymentMethod != null)
            {
                return await PayWithStoredPaymentMethodAsync(request);
            }

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

            var storeForFutureUse = request.StoreForFutureUse && !string.IsNullOrEmpty(order.CustomerId);
            if (storeForFutureUse)
            {
                transactionRequest.ProcessingOptions = new AuthorizeNetProcessingOptions
                {
                    IsFirstRecurringPayment = request.StoredCredentialUsage == StoredCredentialUsage.Recurring,
                    IsFirstSubsequentAuth = request.StoredCredentialUsage == StoredCredentialUsage.Unscheduled,
                };
            }

            var transactionResult = await _authorizeNetClient.CreateTransactionAsync(transactionRequest);
            var result = ProcessCreateTransactionResult(transactionResult, payment, order);

            if (result.IsSuccess && storeForFutureUse)
            {
                result.StoredPaymentMethod = await CreateCardFromTransactionAsync(transactionResult, order, payment, request.StoredCredentialUsage);
            }

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
                TransactionAmount = request.CaptureAmount ?? payment.Sum,
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

            var refundAmount = request.AmountToRefund > 0 ? Math.Min(request.AmountToRefund, payment.Sum) : payment.Sum;
            var isPartialRefund = refundAmount < payment.Sum;

            if (transactionDetails.IsSettled)
            {
                var transactionRequest = new AuthorizeNetRefundTransactionRequest
                {
                    IsLiveMode = IsLiveMode,
                    ApiLogin = ApiLogin,
                    TransactionKey = TransactionKey,
                    TransactionId = payment.OuterId,
                    TransactionAmount = refundAmount,
                    PaymentData = transactionDetails.PaymentData,
                };

                var refundTransactionResult = await _authorizeNetClient.RefundTransactionAsync(transactionRequest);

                if (refundTransactionResult.TransactionResponse == TransactionResponse.Approved)
                {
                    result.IsSuccess = true;
                    result.NewPaymentStatus = payment.PaymentStatus = isPartialRefund ? PaymentStatus.PartiallyRefunded : PaymentStatus.Refunded;
                    payment.Status = payment.PaymentStatus.ToString();
                    payment.VoidedDate = DateTime.UtcNow;
                }
                else
                {
                    result.ErrorMessage = refundTransactionResult.TransactionMessage?.Description;
                }
            }
            else if (isPartialRefund)
            {
                // An unsettled transaction can only be voided as a whole.
                result.ErrorMessage = "A partial refund is possible once the transaction has settled. Void the payment to cancel it in full.";
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

                var voidResult = await VoidProcessPaymentAsync(voidRequest, cancellationToken);

                result.IsSuccess = voidResult.IsSuccess;
                result.NewPaymentStatus = voidResult.NewPaymentStatus;
                result.ErrorMessage = voidResult.ErrorMessage;
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


        #region Stored credentials (card on file)

        /// <summary>
        /// Saves a card without a payment, from an Accept.js nonce in <see cref="PaymentRequestBase.Parameters"/>
        /// (<c>dataDescriptor</c>, <c>dataValue</c>), optionally verified with a $0 authorization. The verification needs the
        /// card's billing address (<c>billingAddress</c> and <c>billingZip</c>, plus optional name, city, state and country).
        /// </summary>
        public override async Task<StorePaymentMethodRequestResult> StorePaymentMethodAsync(StorePaymentMethodRequest request, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);

            var dataDescriptor = request.Parameters?.Get(ModuleConstants.DataDescriptorParamName);
            var dataValue = request.Parameters?.Get(ModuleConstants.DataValueParamName);
            if (string.IsNullOrEmpty(dataDescriptor) || string.IsNullOrEmpty(dataValue) || string.IsNullOrEmpty(request.CustomerId))
            {
                return new StorePaymentMethodRequestResult
                {
                    Outcome = ChargeOutcome.HardDecline,
                    ErrorMessage = "Enter the card details again.",
                    RawMessage = "The Accept.js payment nonce or the customer is missing.",
                };
            }

            var billTo = GetBillingAddress(request.Parameters);
            if (request.Verify && !billTo.HasAvsData)
            {
                return new StorePaymentMethodRequestResult
                {
                    Outcome = ChargeOutcome.HardDecline,
                    ErrorMessage = "Enter the billing address of the card.",
                    RawMessage = "Verifying a card needs its billing street address and ZIP code.",
                };
            }

            // The Accept.js nonce works once, so the card goes straight into the customer's existing profile when there is one,
            // found by the merchant customer ID derived from the user ID (no retry after a "duplicate profile" answer).
            var merchantCustomerId = GetMerchantCustomerId(request.CustomerId);
            var profileRequest = new AuthorizeNetCustomerProfileRequest
            {
                IsLiveMode = IsLiveMode,
                ApiLogin = ApiLogin,
                TransactionKey = TransactionKey,
                CustomerProfileId = await _authorizeNetClient.FindCustomerProfileIdAsync(new AuthorizeNetCustomerLookupRequest
                {
                    IsLiveMode = IsLiveMode,
                    ApiLogin = ApiLogin,
                    TransactionKey = TransactionKey,
                    MerchantCustomerId = merchantCustomerId,
                }),
                MerchantCustomerId = merchantCustomerId,
                Description = GetProfileDescription(request.CustomerId),
                DataDescriptor = dataDescriptor,
                DataValue = dataValue,
                BillTo = billTo,
                ValidationMode = request.Verify ? "liveMode" : "none",
            };

            var profileResult = await _authorizeNetClient.CreateCustomerProfileAsync(profileRequest);

            var reference = new AuthorizeNetProfileReference
            {
                CustomerProfileId = profileResult.CustomerProfileId ?? profileRequest.CustomerProfileId,
                CustomerPaymentProfileId = profileResult.CustomerPaymentProfileId ?? (profileResult.IsDuplicate && !string.IsNullOrEmpty(profileRequest.CustomerProfileId) ? profileResult.DuplicateId : null),
            };

            if (!profileResult.IsSuccess && !(profileResult.IsDuplicate && !string.IsNullOrEmpty(reference.CustomerPaymentProfileId)))
            {
                return GetStoreFailure(profileResult);
            }

            // A card already in the profile comes back with its existing payment profile ID; the caller recognizes it as the same card.
            var card = AbstractTypeFactory<StoredPaymentMethod>.TryCreateInstance();
            card.StoreId = request.StoreId;
            card.CustomerId = request.CustomerId;
            card.OrganizationId = request.OrganizationId;
            card.PaymentMethodCode = Code;
            card.ProviderCustomerRef = reference.CustomerProfileId;
            card.Token = reference.CustomerPaymentProfileId;
            card.Usage = request.Usage;
            card.ConsentId = request.ConsentId;
            await FillCardDetailsAsync(card, null);

            return new StorePaymentMethodRequestResult { IsSuccess = true, Outcome = ChargeOutcome.Succeeded, StoredPaymentMethod = card };
        }

        /// <summary>
        /// Charges a saved card without the customer present, as a merchant-initiated transaction of the stored credential framework.
        /// A repeated call with the same idempotency key returns the original transaction instead of charging again.
        /// </summary>
        public override async Task<ProcessStoredPaymentRequestResult> ProcessStoredPaymentAsync(ProcessStoredPaymentRequest request, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);

            var card = request.StoredPaymentMethod;
            if (card == null || string.IsNullOrEmpty(card.ProviderCustomerRef) || string.IsNullOrEmpty(card.Token))
            {
                return new ProcessStoredPaymentRequestResult
                {
                    Outcome = ChargeOutcome.HardDecline,
                    ErrorMessage = "This saved card can't be charged.",
                    RawMessage = "The stored payment method has no Authorize.Net customer or payment profile.",
                };
            }

            var order = request.Order as CustomerOrder;
            var transactionRequest = new AuthorizeNetCreateTransactionRequest
            {
                IsLiveMode = IsLiveMode,
                ApiLogin = ApiLogin,
                TransactionKey = TransactionKey,
                PaymentActionType = request.CaptureMode == StoredPaymentCaptureMode.AuthorizeAndCapture ? ModuleConstants.Sale : ModuleConstants.AuthCapture,
                Amount = request.Amount,
                CurrencyCode = request.Currency,
                OrderId = request.OrderId,
                OrderNumber = order?.Number,
                InvoiceNumber = GetInvoiceNumber(request.IdempotencyKey, request.OrderId),
                DuplicateWindowSeconds = string.IsNullOrEmpty(request.IdempotencyKey) ? null : ModuleConstants.StoredPaymentDuplicateWindowSeconds,
                Profile = new AuthorizeNetProfileReference { CustomerProfileId = card.ProviderCustomerRef, CustomerPaymentProfileId = card.Token },
                ProcessingOptions = new AuthorizeNetProcessingOptions { IsSubsequentAuth = true, IsStoredCredentials = true },
                SubsequentAuthInformation = new AuthorizeNetSubsequentAuthInformation
                {
                    OriginalNetworkTransactionId = card.NetworkTransactionId,
                    Reason = GetSubsequentAuthReason(request.Reason),
                },
            };

            AuthorizeNetTransactionResult transactionResult;
            try
            {
                transactionResult = await _authorizeNetClient.CreateTransactionAsync(transactionRequest);

                if (transactionResult.IsDuplicate && !string.IsNullOrEmpty(transactionResult.TransactionId))
                {
                    // The same charge was already sent (a replay after a crash or a timeout): report the original transaction.
                    transactionResult = await GetTransactionDetailsAsync(transactionResult.TransactionId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Authorize.Net stored card charge failed for order {OrderId}", request.OrderId);
                return new ProcessStoredPaymentRequestResult
                {
                    Outcome = ChargeOutcome.ProviderError,
                    ErrorMessage = "The payment provider is not available. Please try again later.",
                    RawMessage = ex.Message,
                    RetryAfter = AuthorizeNetChargeOutcomeMapper.ProcessorErrorRetryAfter,
                };
            }

            // Transaction details (the original of a duplicate) carry a status instead of a response of their own.
            var outcome = transactionResult.TransactionStatus != null
                ? AuthorizeNetChargeOutcomeMapper.GetOutcomeByStatus(transactionResult)
                : AuthorizeNetChargeOutcomeMapper.GetOutcome(transactionResult);

            var result = new ProcessStoredPaymentRequestResult
            {
                IsSuccess = outcome == ChargeOutcome.Succeeded,
                Outcome = outcome,
                TransactionId = transactionResult.TransactionId,
                NetworkTransactionId = transactionResult.NetworkTransactionId,
                AuthorizationCode = transactionResult.AuthCode,
                Amount = outcome == ChargeOutcome.Succeeded ? transactionResult.AuthAmount ?? request.Amount : null,
                RawCode = transactionResult.ReasonCode ?? transactionResult.TransactionResponseCode,
                RawMessage = transactionResult.TransactionMessage.Description,
                RetryAfter = AuthorizeNetChargeOutcomeMapper.GetRetryAfter(transactionResult, outcome),
            };

            if (outcome == ChargeOutcome.Succeeded)
            {
                result.NewPaymentStatus = request.CaptureMode == StoredPaymentCaptureMode.AuthorizeAndCapture ? PaymentStatus.Paid : PaymentStatus.Authorized;

                if (request.Reason == MerchantInitiatedReason.Reauthorization)
                {
                    await VoidReplacedAuthorizationAsync(request.Payment as PaymentIn, transactionResult.TransactionId);
                }
            }
            else
            {
                result.ErrorMessage = GetCustomerMessage(outcome);

                if (AuthorizeNetChargeOutcomeMapper.IsMerchantFilterDecline(transactionResult))
                {
                    _logger.LogWarning("Authorize.Net rejected a stored card charge for order {OrderId} with reason {ReasonCode}: the merchant's AVS, card code or fraud filters must allow transactions without a card code (result \"P\").",
                        request.OrderId, transactionResult.ReasonCode);
                }
            }

            return result;
        }

        /// <summary>
        /// Deletes the payment profile in the Customer Information Manager. A card that no longer exists there counts as deleted.
        /// </summary>
        public override async Task<DeleteStoredPaymentMethodRequestResult> DeleteStoredPaymentMethodAsync(DeleteStoredPaymentMethodRequest request, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);

            var card = request.StoredPaymentMethod;
            if (card == null || string.IsNullOrEmpty(card.ProviderCustomerRef) || string.IsNullOrEmpty(card.Token))
            {
                return new DeleteStoredPaymentMethodRequestResult { IsSuccess = true };
            }

            var deleteResult = await _authorizeNetClient.DeleteCustomerPaymentProfileAsync(new AuthorizeNetPaymentProfileRequest
            {
                IsLiveMode = IsLiveMode,
                ApiLogin = ApiLogin,
                TransactionKey = TransactionKey,
                CustomerProfileId = card.ProviderCustomerRef,
                CustomerPaymentProfileId = card.Token,
            });

            return new DeleteStoredPaymentMethodRequestResult
            {
                IsSuccess = deleteResult.IsSuccess,
                ErrorMessage = deleteResult.IsSuccess ? null : "The card could not be removed at the payment provider.",
            };
        }

        /// <summary>
        /// Pays a checkout order with a saved card the customer chose: a customer-initiated charge of a stored credential.
        /// </summary>
        protected virtual async Task<PostProcessPaymentRequestResult> PayWithStoredPaymentMethodAsync(PostProcessPaymentRequest request)
        {
            var payment = request.GetPayment();
            var order = request.GetOrder();

            // The caller checked that the card is the customer's and active; only an unusable reference is refused here.
            var card = request.StoredPaymentMethod;
            if (order == null || payment == null || string.IsNullOrEmpty(card.ProviderCustomerRef) || string.IsNullOrEmpty(card.Token))
            {
                return new PostProcessPaymentRequestResult { ErrorMessage = "This saved card can't be used. Choose another card." };
            }

            var transactionRequest = new AuthorizeNetCreateTransactionRequest
            {
                IsLiveMode = IsLiveMode,
                ApiLogin = ApiLogin,
                TransactionKey = TransactionKey,
                PaymentActionType = PaymentActionType,
                Amount = payment.Sum,
                CurrencyCode = payment.Currency,
                OrderId = order.Id,
                OrderNumber = order.Number,
                Profile = new AuthorizeNetProfileReference { CustomerProfileId = card.ProviderCustomerRef, CustomerPaymentProfileId = card.Token },
                ProcessingOptions = new AuthorizeNetProcessingOptions { IsStoredCredentials = true },
            };

            var transactionResult = await _authorizeNetClient.CreateTransactionAsync(transactionRequest);
            return ProcessCreateTransactionResult(transactionResult, payment, order);
        }

        /// <summary>
        /// Saves the card of an approved checkout payment in the Customer Information Manager and returns it, not persisted;
        /// the caller records it. A failure is logged and returns null: the payment itself stays approved.
        /// </summary>
        protected virtual async Task<StoredPaymentMethod> CreateCardFromTransactionAsync(AuthorizeNetTransactionResult transactionResult, CustomerOrder order, PaymentIn payment, StoredCredentialUsage usage)
        {
            try
            {
                var profileRequest = new AuthorizeNetProfileFromTransactionRequest
                {
                    IsLiveMode = IsLiveMode,
                    ApiLogin = ApiLogin,
                    TransactionKey = TransactionKey,
                    TransactionId = transactionResult.TransactionId,
                    MerchantCustomerId = GetMerchantCustomerId(order.CustomerId),
                    Email = GetEmail(order, payment),
                    Description = GetProfileDescription(order.CustomerId),
                };

                var profileResult = await _authorizeNetClient.CreateCustomerProfileFromTransactionAsync(profileRequest);

                if (!profileResult.IsSuccess && string.IsNullOrEmpty(profileRequest.CustomerProfileId) && !string.IsNullOrEmpty(profileResult.DuplicateId))
                {
                    profileRequest.CustomerProfileId = profileResult.DuplicateId;
                    profileResult = await _authorizeNetClient.CreateCustomerProfileFromTransactionAsync(profileRequest);
                }

                var customerProfileId = profileResult.CustomerProfileId ?? profileRequest.CustomerProfileId;
                var paymentProfileId = profileResult.CustomerPaymentProfileId
                    ?? (profileResult.IsDuplicate && !string.IsNullOrEmpty(profileRequest.CustomerProfileId) ? profileResult.DuplicateId : null);

                if (string.IsNullOrEmpty(customerProfileId) || string.IsNullOrEmpty(paymentProfileId))
                {
                    _logger.LogWarning("Authorize.Net did not save the card of order {OrderId}: {Errors}",
                        order.Id, string.Join("; ", profileResult.Errors.Select(x => $"{x.Code} {x.Description}")));
                    return null;
                }

                var card = AbstractTypeFactory<StoredPaymentMethod>.TryCreateInstance();
                card.PaymentMethodCode = Code;
                card.OrganizationId = order.OrganizationId;
                card.ProviderCustomerRef = customerProfileId;
                card.Token = paymentProfileId;
                card.Brand = transactionResult.AccountType;
                card.Last4 = GetLast4(transactionResult.AccountNumber);
                card.NetworkTransactionId = transactionResult.NetworkTransactionId;
                card.InitialTransactionRef = transactionResult.TransactionId;
                card.InitialOrderId = order.Id;
                card.InitialPaymentId = payment?.Id;
                card.Usage = usage;
                await FillCardDetailsAsync(card, transactionResult.NetworkTransactionId);

                return card;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Saving the Authorize.Net card of order {OrderId} failed", order.Id);
                return null;
            }
        }

        /// <summary>
        /// Reads the brand, last 4 digits, expiry date and original network transaction ID of a stored card.
        /// </summary>
        protected virtual async Task FillCardDetailsAsync(StoredPaymentMethod card, string networkTransactionId)
        {
            var profile = await _authorizeNetClient.GetCustomerPaymentProfileAsync(new AuthorizeNetPaymentProfileRequest
            {
                IsLiveMode = IsLiveMode,
                ApiLogin = ApiLogin,
                TransactionKey = TransactionKey,
                CustomerProfileId = card.ProviderCustomerRef,
                CustomerPaymentProfileId = card.Token,
            });

            if (!profile.IsSuccess)
            {
                return;
            }

            card.Brand ??= profile.CardType;
            card.Last4 ??= GetLast4(profile.CardNumber);
            card.NetworkTransactionId = networkTransactionId ?? profile.OriginalNetworkTransactionId ?? card.NetworkTransactionId;

            if (DateTime.TryParseExact(profile.ExpirationDate, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var expiration))
            {
                card.ExpirationYear = expiration.Year;
                card.ExpirationMonth = expiration.Month;
            }
        }

        private async Task<AuthorizeNetTransactionResult> GetTransactionDetailsAsync(string transactionId)
        {
            return await _authorizeNetClient.GetTransactionDetailsAsync(new AuthorizeNetTransactionRequest
            {
                IsLiveMode = IsLiveMode,
                ApiLogin = ApiLogin,
                TransactionKey = TransactionKey,
                TransactionId = transactionId,
            });
        }

        private async Task VoidReplacedAuthorizationAsync(PaymentIn payment, string newTransactionId)
        {
            var replacedTransactionId = payment?.OuterId;
            if (string.IsNullOrEmpty(replacedTransactionId) || replacedTransactionId == newTransactionId)
            {
                return;
            }

            try
            {
                // Release the hold of the authorization the new one replaces; an expired authorization simply can't be voided.
                await _authorizeNetClient.VoidTransactionAsync(new AuthorizeNetVoidTransactionRequest
                {
                    IsLiveMode = IsLiveMode,
                    ApiLogin = ApiLogin,
                    TransactionKey = TransactionKey,
                    TransactionId = replacedTransactionId,
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Voiding the replaced Authorize.Net authorization {TransactionId} failed", replacedTransactionId);
            }
        }

        private static StorePaymentMethodRequestResult GetStoreFailure(AuthorizeNetCustomerProfileResult profileResult)
        {
            var validation = new AuthorizeNetTransactionResult
            {
                TransactionResponseCode = profileResult.ValidationResponseCode ?? "3",
                TransactionErrors = string.IsNullOrEmpty(profileResult.ValidationReasonCode)
                    ? []
                    : [new AuthorizeNetTransactionMessage { Code = profileResult.ValidationReasonCode, Description = profileResult.ValidationReasonText }],
                Errors = profileResult.Errors,
            };

            var outcome = AuthorizeNetChargeOutcomeMapper.GetOutcome(validation);
            if (outcome == ChargeOutcome.Succeeded)
            {
                outcome = ChargeOutcome.ProviderError;
            }

            return new StorePaymentMethodRequestResult
            {
                Outcome = outcome,
                ErrorMessage = outcome is ChargeOutcome.HardDecline or ChargeOutcome.SoftDecline
                    ? "The card was declined. Check the details or try another card."
                    : "The card could not be saved. Please try again.",
                RawCode = validation.ReasonCode,
                RawMessage = validation.TransactionMessage.Description,
            };
        }

        private static string GetCustomerMessage(ChargeOutcome outcome) => outcome switch
        {
            ChargeOutcome.HardDecline => "Your card was declined. Please update your payment method.",
            ChargeOutcome.SoftDecline => "Your card was declined. We will try again later.",
            ChargeOutcome.RequiresAction => "Your bank needs you to confirm this payment.",
            _ => "The payment could not be processed. We will try again later.",
        };

        private static string GetSubsequentAuthReason(MerchantInitiatedReason reason) => reason switch
        {
            MerchantInitiatedReason.Resubmission => "resubmission",
            MerchantInitiatedReason.DelayedCharge => "delayedCharge",
            MerchantInitiatedReason.Reauthorization => "reauthorization",
            _ => null,
        };

        /// <summary>
        /// A deterministic invoice number for an idempotency key, so a repeated charge matches the original inside the duplicate window.
        /// </summary>
        public static string GetInvoiceNumber(string idempotencyKey, string orderId)
        {
            if (string.IsNullOrEmpty(idempotencyKey))
            {
                return orderId?.Length > ModuleConstants.MaxReferenceLength ? orderId[^ModuleConstants.MaxReferenceLength..] : orderId;
            }

            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(idempotencyKey));
            return Convert.ToHexString(hash)[..ModuleConstants.MaxReferenceLength];
        }

        /// <summary>
        /// Authorize.Net limits merchant customer IDs to 20 characters; platform user IDs are usually 36-character GUIDs.
        /// </summary>
        public static string GetMerchantCustomerId(string customerId)
        {
            if (string.IsNullOrEmpty(customerId))
            {
                return customerId;
            }

            var compact = customerId.Replace("-", string.Empty);
            return compact.Length > ModuleConstants.MaxReferenceLength ? compact[..ModuleConstants.MaxReferenceLength] : compact;
        }

        private static string GetProfileDescription(string customerId) => $"Virto Commerce customer {customerId}";

        private static string GetLast4(string accountNumber)
        {
            var digits = new string((accountNumber ?? string.Empty).Where(char.IsDigit).ToArray());
            return digits.Length >= 4 ? digits[^4..] : null;
        }

        private static string GetEmail(CustomerOrder order, PaymentIn payment)
        {
            return payment?.BillingAddress?.Email
                ?? order.Addresses?.Select(x => x.Email).FirstOrDefault(x => !string.IsNullOrEmpty(x));
        }

        private static AuthorizeNetBillingAddress GetBillingAddress(NameValueCollection parameters)
        {
            return new AuthorizeNetBillingAddress
            {
                FirstName = parameters?[ModuleConstants.BillingFirstNameParamName],
                LastName = parameters?[ModuleConstants.BillingLastNameParamName],
                Address = parameters?[ModuleConstants.BillingAddressParamName],
                City = parameters?[ModuleConstants.BillingCityParamName],
                State = parameters?[ModuleConstants.BillingStateParamName],
                Zip = parameters?[ModuleConstants.BillingZipParamName],
                Country = parameters?[ModuleConstants.BillingCountryParamName],
            };
        }


        #endregion

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
                    // Status (64) and ResponseCode (64) are short columns. The gateway can return
                    // several combined messages/warnings even on approval, so keep concise values
                    // here and put the full text in Note (2048).
                    Note = $"Transaction ID: {transactionResult.TransactionId}. {transactionMessage.Description}".Trim(),
                    Status = transactionResult.TransactionResponse.ToString(),
                    ResponseCode = transactionResult.TransactionResponseCode,
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
