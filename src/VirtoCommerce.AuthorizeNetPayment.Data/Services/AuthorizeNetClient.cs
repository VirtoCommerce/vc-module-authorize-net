using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Mime;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Serialization;
using AuthorizeNet.Api.Contracts.V1;
using AuthorizeNet.Api.Controllers;
using AuthorizeNet.Api.Controllers.Bases;
using VirtoCommerce.AuthorizeNetPayment.Core;
using VirtoCommerce.AuthorizeNetPayment.Core.Models;
using VirtoCommerce.AuthorizeNetPayment.Core.Services;
using VirtoCommerce.AuthorizeNetPayment.Data.Extensions;

namespace VirtoCommerce.AuthorizeNetPayment.Data.Services
{
    public class AuthorizeNetClient : IAuthorizeNetClient
    {
        private readonly IHttpClientFactory _httpClientFactory;
        public AuthorizeNetClient(IHttpClientFactory httpClientFactory)
        {
            _httpClientFactory = httpClientFactory;
        }

        public AuthorizeNetTokenResult GetPublicClientKey(AuthorizeNetTokenRequest request)
        {
            SetApiMode(request.IsLiveMode);

            var authorizeRequest = new getMerchantDetailsRequest
            {
                merchantAuthentication = new merchantAuthenticationType
                {
                    name = request.ApiLogin,
                    Item = request.TransactionKey,
                    ItemElementName = ItemChoiceType.transactionKey,
                }
            };

            var controller = new getMerchantDetailsController(authorizeRequest);
            controller.Execute();

            var response = controller.GetApiResponse();

            var tokenResult = new AuthorizeNetTokenResult
            {
                IsSuccess = response.messages.resultCode.IsSuccessfulApiResponse(),
                ClientKey = response.publicClientKey,
            };

            return tokenResult;
        }

        public Task<AuthorizeNetTokenResult> GetPublicClientKeyAsync(AuthorizeNetTokenRequest request)
        {
            var result = GetPublicClientKey(request);
            return Task.FromResult(result);
        }

        public AuthorizeNetTransactionResult GetTransactionDetails(AuthorizeNetTransactionRequest request)
        {
            SetApiMode(request.IsLiveMode);

            var merchantAuthentication = new merchantAuthenticationType
            {
                name = request.ApiLogin,
                Item = request.TransactionKey,
                ItemElementName = ItemChoiceType.transactionKey,
            };

            var transactionDetailsRequest = new getTransactionDetailsRequest
            {
                merchantAuthentication = merchantAuthentication,
                transId = request.TransactionId
            };

            // instantiate the controller that will call the service
            var controller = new getTransactionDetailsController(transactionDetailsRequest);
            controller.Execute();

            // get the response from the service (errors contained if any)
            var response = controller.GetApiResponse();

            if (response == null)
            {
                return GetResponse(controller.GetErrorResponse());
            }

            var transaction = response.transaction;
            if (transaction == null)
            {
                return GetResponse(response);
            }

            var card = transaction.payment?.Item as creditCardMaskedType;

            var transactionResult = new AuthorizeNetTransactionResult
            {
                IsSuccess = response.messages.resultCode.IsSuccessfulApiResponse(),
                TransactionId = transaction.transId,
                TransactionResponseCode = transaction.responseCode.ToString(CultureInfo.InvariantCulture),
                TransactionStatus = transaction.transactionStatus,
                TransactionType = transaction.transactionType,
                PaymentData = card != null ? card.cardNumber[^4..] : string.Empty,
                AccountType = card?.cardType,
                AuthCode = transaction.authCode,
                AuthAmount = transaction.authAmount,
                ResponseReasonCode = transaction.responseReasonCode.ToString(CultureInfo.InvariantCulture),
                NetworkTransactionId = transaction.networkTransId,
                OriginalNetworkTransactionId = transaction.originalNetworkTransId,
            };

            return transactionResult;
        }

        public Task<AuthorizeNetTransactionResult> GetTransactionDetailsAsync(AuthorizeNetTransactionRequest request)
        {
            var result = GetTransactionDetails(request);
            return Task.FromResult(result);
        }

        public AuthorizeNetTransactionResult CreateTransaction(AuthorizeNetCreateTransactionRequest request)
        {
            SetApiMode(request.IsLiveMode);

            var merchantAuthentication = new merchantAuthenticationType
            {
                name = request.ApiLogin,
                Item = request.TransactionKey,
                ItemElementName = ItemChoiceType.transactionKey,
            };

            var opaqueData = new opaqueDataType
            {
                dataDescriptor = request.DataDescriptor,
                dataValue = request.DataValue,
            };

            //standard api call to retrieve response
            var paymentType = new paymentType
            {
                Item = opaqueData
            };
            if (request.CreditCard != null)
            {
                paymentType.Item = new creditCardType
                {
                    cardCode = request.CreditCard.CardCode,
                    cardNumber = request.CreditCard.CardNumber,
                    expirationDate = request.CreditCard.CardExpiration
                };
            }
            if (request.Profile != null)
            {
                paymentType = null;
            }

            var order = new orderType
            {
                invoiceNumber = request.InvoiceNumber ?? request.OrderId?.Substring(20),
            };

            var transactionType = request.PaymentActionType == ModuleConstants.Sale
                ? transactionTypeEnum.authCaptureTransaction
                : transactionTypeEnum.authOnlyTransaction;

            var transactionRequest = new transactionRequestType
            {
                transactionType = transactionType.ToString(), // charge the card
                amount = request.Amount,
                amountSpecified = true,
                currencyCode = request.CurrencyCode,
                poNumber = request.OrderNumber,
                payment = paymentType,
                order = order,
            };

            if (request.Profile != null)
            {
                transactionRequest.profile = new customerProfilePaymentType
                {
                    customerProfileId = request.Profile.CustomerProfileId,
                    paymentProfile = new paymentProfile { paymentProfileId = request.Profile.CustomerPaymentProfileId },
                };
            }

            if (!string.IsNullOrEmpty(request.CustomerId) || !string.IsNullOrEmpty(request.CustomerEmail))
            {
                transactionRequest.customer = new customerDataType
                {
                    id = request.CustomerId,
                    email = request.CustomerEmail,
                };
            }

            if (request.DuplicateWindowSeconds != null)
            {
                transactionRequest.transactionSettings =
                [
                    new settingType
                    {
                        settingName = settingNameEnum.duplicateWindow.ToString(),
                        settingValue = request.DuplicateWindowSeconds.Value.ToString(CultureInfo.InvariantCulture),
                    },
                ];
            }

            transactionRequest.processingOptions = GetProcessingOptions(request.ProcessingOptions);
            transactionRequest.subsequentAuthInformation = GetSubsequentAuthInformation(request.SubsequentAuthInformation);

            if (request.CreditCard != null)
            {
                return ProcessTransactionProxyRequest(merchantAuthentication, transactionRequest, request.CreditCard);
            }

            return ProcessTransactionRequest(merchantAuthentication, transactionRequest);
        }

        public Task<AuthorizeNetTransactionResult> CreateTransactionAsync(AuthorizeNetCreateTransactionRequest request)
        {
            var result = CreateTransaction(request);
            return Task.FromResult(result);
        }

        public AuthorizeNetTransactionResult CaptureTransaction(AuthorizeNetCaptureTransactionRequest request)
        {
            SetApiMode(request.IsLiveMode);

            var merchantAuthentication = new merchantAuthenticationType
            {
                name = request.ApiLogin,
                Item = request.TransactionKey,
                ItemElementName = ItemChoiceType.transactionKey,
            };

            var transactionRequest = new transactionRequestType
            {
                transactionType = transactionTypeEnum.priorAuthCaptureTransaction.ToString(), // capture prior only
                amount = request.TransactionAmount,
                refTransId = request.TransactionId,
            };

            return ProcessTransactionRequest(merchantAuthentication, transactionRequest);
        }

        public Task<AuthorizeNetTransactionResult> CaptureTransactionAsync(AuthorizeNetCaptureTransactionRequest request)
        {
            var result = CaptureTransaction(request);
            return Task.FromResult(result);
        }

        public AuthorizeNetTransactionResult RefundTransaction(AuthorizeNetRefundTransactionRequest request)
        {
            SetApiMode(request.IsLiveMode);

            var merchantAuthentication = new merchantAuthenticationType
            {
                name = request.ApiLogin,
                Item = request.TransactionKey,
                ItemElementName = ItemChoiceType.transactionKey,
            };

            var creditCard = new creditCardType
            {
                cardNumber = request.PaymentData,
                expirationDate = "XXXX"
            };

            //standard api call to retrieve response
            var paymentType = new paymentType { Item = creditCard };

            var transactionRequest = new transactionRequestType
            {
                transactionType = transactionTypeEnum.refundTransaction.ToString(),
                payment = paymentType,
                amount = request.TransactionAmount,
                refTransId = request.TransactionId,
            };

            return ProcessTransactionRequest(merchantAuthentication, transactionRequest);
        }

        public Task<AuthorizeNetTransactionResult> RefundTransactionAsync(AuthorizeNetRefundTransactionRequest request)
        {
            var result = RefundTransaction(request);
            return Task.FromResult(result);
        }

        public AuthorizeNetTransactionResult VoidTransaction(AuthorizeNetVoidTransactionRequest request)
        {
            SetApiMode(request.IsLiveMode);

            var merchantAuthentication = new merchantAuthenticationType
            {
                name = request.ApiLogin,
                Item = request.TransactionKey,
                ItemElementName = ItemChoiceType.transactionKey,
            };

            var transactionRequest = new transactionRequestType
            {
                transactionType = transactionTypeEnum.voidTransaction.ToString(),
                refTransId = request.TransactionId,
            };

            return ProcessTransactionRequest(merchantAuthentication, transactionRequest);
        }

        public Task<AuthorizeNetTransactionResult> VoidTransactionAsync(AuthorizeNetVoidTransactionRequest request)
        {
            var result = VoidTransaction(request);
            return Task.FromResult(result);
        }


        public Task<string> FindCustomerProfileIdAsync(AuthorizeNetCustomerLookupRequest request)
        {
            SetApiMode(request.IsLiveMode);

            var controller = new getCustomerProfileController(new getCustomerProfileRequest
            {
                merchantAuthentication = GetMerchantAuthentication(request),
                merchantCustomerId = request.MerchantCustomerId,
            });
            controller.Execute();

            // Record not found (E00040) leaves no response: the customer has no profile yet.
            var response = controller.GetApiResponse();
            return Task.FromResult(response?.profile?.customerProfileId);
        }

        public Task<AuthorizeNetCustomerProfileResult> CreateCustomerProfileAsync(AuthorizeNetCustomerProfileRequest request)
        {
            SetApiMode(request.IsLiveMode);

            var paymentProfile = new customerPaymentProfileType
            {
                payment = new paymentType
                {
                    Item = new opaqueDataType
                    {
                        dataDescriptor = request.DataDescriptor,
                        dataValue = request.DataValue,
                    },
                },
                billTo = request.BillTo == null ? null : new customerAddressType
                {
                    firstName = request.BillTo.FirstName,
                    lastName = request.BillTo.LastName,
                    address = request.BillTo.Address,
                    city = request.BillTo.City,
                    state = request.BillTo.State,
                    zip = request.BillTo.Zip,
                    country = request.BillTo.Country,
                },
            };

            var validationMode = ParseValidationMode(request.ValidationMode);
            AuthorizeNetCustomerProfileResult result;

            if (string.IsNullOrEmpty(request.CustomerProfileId))
            {
                var controller = new createCustomerProfileController(new createCustomerProfileRequest
                {
                    merchantAuthentication = GetMerchantAuthentication(request),
                    profile = new customerProfileType
                    {
                        merchantCustomerId = request.MerchantCustomerId,
                        email = request.Email,
                        description = request.Description,
                        paymentProfiles = [paymentProfile],
                    },
                    validationMode = validationMode,
                    validationModeSpecified = true,
                });
                controller.Execute();

                // A duplicate is an error that still carries the existing profile's IDs.
                var response = controller.GetApiResponse() ?? controller.GetErrorResponse() as createCustomerProfileResponse;
                if (response == null)
                {
                    return Task.FromResult(GetProfileResponse(controller.GetErrorResponse()));
                }

                result = GetProfileResponse(response);
                result.CustomerProfileId = response.customerProfileId;
                result.CustomerPaymentProfileId = response.customerPaymentProfileIdList?.FirstOrDefault();
                SetValidationResult(result, response.validationDirectResponseList?.FirstOrDefault());
            }
            else
            {
                var controller = new createCustomerPaymentProfileController(new createCustomerPaymentProfileRequest
                {
                    merchantAuthentication = GetMerchantAuthentication(request),
                    customerProfileId = request.CustomerProfileId,
                    paymentProfile = paymentProfile,
                    validationMode = validationMode,
                    validationModeSpecified = true,
                });
                controller.Execute();

                // The same card again is an error that still carries the existing payment profile's ID.
                var response = controller.GetApiResponse() ?? controller.GetErrorResponse() as createCustomerPaymentProfileResponse;
                if (response == null)
                {
                    return Task.FromResult(GetProfileResponse(controller.GetErrorResponse()));
                }

                result = GetProfileResponse(response);
                result.CustomerProfileId = response.customerProfileId ?? request.CustomerProfileId;
                result.CustomerPaymentProfileId = response.customerPaymentProfileId;
                SetValidationResult(result, response.validationDirectResponse);
            }

            return Task.FromResult(result);
        }

        public Task<AuthorizeNetCustomerProfileResult> CreateCustomerProfileFromTransactionAsync(AuthorizeNetProfileFromTransactionRequest request)
        {
            SetApiMode(request.IsLiveMode);

            var profileRequest = new createCustomerProfileFromTransactionRequest
            {
                merchantAuthentication = GetMerchantAuthentication(request),
                transId = request.TransactionId,
            };

            if (string.IsNullOrEmpty(request.CustomerProfileId))
            {
                profileRequest.customer = new customerProfileBaseType
                {
                    merchantCustomerId = request.MerchantCustomerId,
                    email = request.Email,
                    description = request.Description,
                };
            }
            else
            {
                profileRequest.customerProfileId = request.CustomerProfileId;
            }

            var controller = new createCustomerProfileFromTransactionController(profileRequest);
            controller.Execute();

            var response = controller.GetApiResponse() ?? controller.GetErrorResponse() as createCustomerProfileResponse;
            if (response == null)
            {
                return Task.FromResult(GetProfileResponse(controller.GetErrorResponse()));
            }

            var result = GetProfileResponse(response);
            result.CustomerProfileId = response.customerProfileId ?? request.CustomerProfileId;
            result.CustomerPaymentProfileId = response.customerPaymentProfileIdList?.FirstOrDefault();

            return Task.FromResult(result);
        }

        public Task<AuthorizeNetPaymentProfileResult> GetCustomerPaymentProfileAsync(AuthorizeNetPaymentProfileRequest request)
        {
            SetApiMode(request.IsLiveMode);

            var controller = new getCustomerPaymentProfileController(new getCustomerPaymentProfileRequest
            {
                merchantAuthentication = GetMerchantAuthentication(request),
                customerProfileId = request.CustomerProfileId,
                customerPaymentProfileId = request.CustomerPaymentProfileId,
                unmaskExpirationDate = true,
                unmaskExpirationDateSpecified = true,
            });
            controller.Execute();

            ANetApiResponse apiResponse = controller.GetApiResponse();
            apiResponse ??= controller.GetErrorResponse();

            var errors = GetMessages(apiResponse);
            var result = new AuthorizeNetPaymentProfileResult
            {
                IsSuccess = apiResponse.messages.resultCode.IsSuccessfulApiResponse(),
                IsNotFound = errors.Exists(x => x.Code == RecordNotFoundCode),
                Errors = errors,
            };

            if (apiResponse is getCustomerPaymentProfileResponse { paymentProfile: { } paymentProfile })
            {
                var card = paymentProfile.payment?.Item as creditCardMaskedType;
                result.CardNumber = card?.cardNumber;
                result.CardType = card?.cardType;
                result.ExpirationDate = card?.expirationDate;
                result.OriginalNetworkTransactionId = paymentProfile.originalNetworkTransId;
            }

            return Task.FromResult(result);
        }

        public Task<AuthorizeNetBaseResult> DeleteCustomerPaymentProfileAsync(AuthorizeNetPaymentProfileRequest request)
        {
            SetApiMode(request.IsLiveMode);

            var controller = new deleteCustomerPaymentProfileController(new deleteCustomerPaymentProfileRequest
            {
                merchantAuthentication = GetMerchantAuthentication(request),
                customerProfileId = request.CustomerProfileId,
                customerPaymentProfileId = request.CustomerPaymentProfileId,
            });
            controller.Execute();

            ANetApiResponse apiResponse = controller.GetApiResponse();
            apiResponse ??= controller.GetErrorResponse();

            var isSuccess = apiResponse.messages.resultCode.IsSuccessfulApiResponse()
                || GetMessages(apiResponse).Exists(x => x.Code == RecordNotFoundCode);

            return Task.FromResult(new AuthorizeNetBaseResult { IsSuccess = isSuccess });
        }


        private static void SetApiMode(bool isLiveMode)
        {
            ApiOperationBase<ANetApiRequest, ANetApiResponse>.RunEnvironment = isLiveMode
                ? AuthorizeNet.Environment.PRODUCTION
                : AuthorizeNet.Environment.SANDBOX;
        }

        private const string RecordNotFoundCode = "E00040";
        private const string DuplicateRecordCode = "E00039";

        private static readonly Regex _duplicateIdRegex = new(@"ID\s+(?<id>\d+)", RegexOptions.Compiled, TimeSpan.FromSeconds(1));

        private static merchantAuthenticationType GetMerchantAuthentication(AuthorizeNetBaseRequest request)
        {
            return new merchantAuthenticationType
            {
                name = request.ApiLogin,
                Item = request.TransactionKey,
                ItemElementName = ItemChoiceType.transactionKey,
            };
        }

        private static validationModeEnum ParseValidationMode(string validationMode)
        {
            return Enum.TryParse<validationModeEnum>(validationMode, ignoreCase: true, out var result) ? result : validationModeEnum.none;
        }

        private static processingOptions GetProcessingOptions(AuthorizeNetProcessingOptions options)
        {
            if (options == null)
            {
                return null;
            }

            return new processingOptions
            {
                isFirstRecurringPayment = options.IsFirstRecurringPayment,
                isFirstRecurringPaymentSpecified = options.IsFirstRecurringPayment,
                isFirstSubsequentAuth = options.IsFirstSubsequentAuth,
                isFirstSubsequentAuthSpecified = options.IsFirstSubsequentAuth,
                isSubsequentAuth = options.IsSubsequentAuth,
                isSubsequentAuthSpecified = options.IsSubsequentAuth,
                isStoredCredentials = options.IsStoredCredentials,
                isStoredCredentialsSpecified = options.IsStoredCredentials,
            };
        }

        private static subsequentAuthInformation GetSubsequentAuthInformation(AuthorizeNetSubsequentAuthInformation information)
        {
            if (information == null)
            {
                return null;
            }

            var result = new subsequentAuthInformation
            {
                originalNetworkTransId = string.IsNullOrEmpty(information.OriginalNetworkTransactionId) ? null : information.OriginalNetworkTransactionId,
            };

            if (Enum.TryParse<merchantInitTransReasonEnum>(information.Reason, ignoreCase: false, out var reason))
            {
                result.reason = reason;
                result.reasonSpecified = true;
            }

            return result.originalNetworkTransId == null && !result.reasonSpecified ? null : result;
        }

        private static AuthorizeNetCustomerProfileResult GetProfileResponse(ANetApiResponse response)
        {
            var isSuccess = response.messages.resultCode.IsSuccessfulApiResponse();
            var messages = GetMessages(response);
            var result = new AuthorizeNetCustomerProfileResult
            {
                IsSuccess = isSuccess,
                Errors = isSuccess ? new List<AuthorizeNetTransactionMessage>() : messages,
            };

            var duplicate = messages.Find(x => x.Code == DuplicateRecordCode);
            if (duplicate != null)
            {
                var match = _duplicateIdRegex.Match(duplicate.Description ?? string.Empty);
                result.DuplicateId = match.Success ? match.Groups["id"].Value : string.Empty;
            }

            return result;
        }

        /// <summary>
        /// Reads the validation authorization from a comma-delimited direct response:
        /// field 1 is the response code, field 3 the reason code and field 4 the reason text.
        /// </summary>
        private static void SetValidationResult(AuthorizeNetCustomerProfileResult result, string directResponse)
        {
            if (string.IsNullOrEmpty(directResponse))
            {
                return;
            }

            var fields = directResponse.Split(',');
            result.ValidationResponseCode = fields.ElementAtOrDefault(0);
            result.ValidationReasonCode = fields.ElementAtOrDefault(2);
            result.ValidationReasonText = fields.ElementAtOrDefault(3);
        }

        private static List<AuthorizeNetTransactionMessage> GetMessages(ANetApiResponse response)
        {
            return response?.messages?.message?.Select(x => new AuthorizeNetTransactionMessage
            {
                Code = x.code,
                Description = x.text,
            }).ToList() ?? new List<AuthorizeNetTransactionMessage>();
        }

        private static AuthorizeNetTransactionResult ProcessTransactionRequest(merchantAuthenticationType merchantAuthentication, transactionRequestType transactionRequest)
        {
            var request = new createTransactionRequest
            {
                merchantAuthentication = merchantAuthentication,
                transactionRequest = transactionRequest
            };

            var controller = new createTransactionController(request);
            controller.Execute();
            var response = controller.GetApiResponse();
            if (response != null)
            {
                return GetTransactionResponse(response);
            }

            var errorResponse = controller.GetErrorResponse();
            return GetResponse(errorResponse);
        }

        private AuthorizeNetTransactionResult ProcessTransactionProxyRequest(merchantAuthenticationType merchantAuthentication, transactionRequestType transactionRequest, AuthorizeNetCreditCard creditCard)
        {
            var createTxRequest = new createTransactionRequest
            {
                merchantAuthentication = merchantAuthentication,
                transactionRequest = transactionRequest
            };
            using var stream = new MemoryStream();
            using var xmlWriter = XmlWriter.Create(stream, new XmlWriterSettings
            {
                Encoding = new UTF8Encoding(false, true), //Exclude BOM
                Indent = true,
            });

            var xmlSerializer = new XmlSerializer(typeof(createTransactionRequest));
            xmlSerializer.Serialize(xmlWriter, createTxRequest);

            stream.Position = 0;
            using var content = new StreamContent(stream);

            content.Headers.ContentType = new MediaTypeHeaderValue(MediaTypeNames.Application.Xml);
            var proxyRequest = new HttpRequestMessage(HttpMethod.Post, new Uri(creditCard.ProxyEndpointUrl))
            {
                Content = content
            };

            var proxyHttpClient = _httpClientFactory.CreateClient(creditCard.ProxyHttpClientName);
            var response = proxyHttpClient.Send(proxyRequest);
            response.EnsureSuccessStatusCode();
            using var resultStream = response.Content.ReadAsStream();
            using var xmlReader = XmlReader.Create(resultStream);
            try
            {
                var responseXmlSerializer = new XmlSerializer(typeof(createTransactionResponse));
                var txResponse = responseXmlSerializer.Deserialize(xmlReader) as createTransactionResponse;
                var result = GetTransactionResponse(txResponse);
                return result;
            }
            catch (Exception)
            {
                var responseXmlSerializer = new XmlSerializer(typeof(ANetApiResponse));
                var txResponse = responseXmlSerializer.Deserialize(xmlReader) as ANetApiResponse;
                var result = GetResponse(txResponse);
                return result;
            }
        }

        private static AuthorizeNetTransactionResult GetTransactionResponse(createTransactionResponse response)
        {
            var result = GetResponse(response);

            if (response.transactionResponse != null)
            {
                result.AccountNumber = response.transactionResponse.accountNumber;
                result.AccountType = response.transactionResponse.accountType;
                result.AuthCode = response.transactionResponse.authCode;
                result.NetworkTransactionId = response.transactionResponse.networkTransId;
                result.AvsResultCode = response.transactionResponse.avsResultCode;
                result.CvvResultCode = response.transactionResponse.cvvResultCode;
                result.TransactionResponseCode = response.transactionResponse.responseCode;
                result.TransactionId = response.transactionResponse.transId;
                result.TransactionMessages = response.transactionResponse.messages?.Select(x => new AuthorizeNetTransactionMessage
                {
                    Code = x.code,
                    Description = x.description,
                }).ToList() ?? new List<AuthorizeNetTransactionMessage>();
                result.TransactionErrors = response.transactionResponse.errors?.Select(x => new AuthorizeNetTransactionMessage
                {
                    Code = x.errorCode,
                    Description = x.errorText,
                }).ToList() ?? new List<AuthorizeNetTransactionMessage>();
            }

            return result;
        }

        private static AuthorizeNetTransactionResult GetResponse(ANetApiResponse response)
        {
            var result = new AuthorizeNetTransactionResult
            {
                IsSuccess = response.messages.resultCode.IsSuccessfulApiResponse(),
                Errors = response.messages.message?.Select(x => new AuthorizeNetTransactionMessage
                {
                    Code = x.code,
                    Description = x.text
                }).ToList() ?? new List<AuthorizeNetTransactionMessage>(),
            };

            return result;
        }
    }
}
