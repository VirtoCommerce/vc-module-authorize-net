using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Mime;
using System.Text;
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
        private IHttpClientFactory _httpClientFactory;
        public AuthorizeNetClient(IHttpClientFactory httpClientFactory)
        {
            _httpClientFactory = httpClientFactory;
        }
        public AuthorizeNetTokenResult GetPublicClientKey(AuthorizeNetTokenRequest request)
        {
            SetApiMode(request.IsLiveMode);

            var authoriseRequest = new getMerchantDetailsRequest
            {
                merchantAuthentication = new merchantAuthenticationType
                {
                    name = request.ApiLogin,
                    Item = request.TransactionKey,
                    ItemElementName = ItemChoiceType.transactionKey,
                }
            };

            var controller = new getMerchantDetailsController(authoriseRequest);
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

            var transactionDetailsRequest = new getTransactionDetailsRequest { merchantAuthentication = merchantAuthentication };
            transactionDetailsRequest.transId = request.TransactionId;

            // instantiate the controller that will call the service
            var controller = new getTransactionDetailsController(transactionDetailsRequest);
            controller.Execute();

            // get the response from the service (errors contained if any)
            var response = controller.GetApiResponse();

            var paymentData = response.transaction.payment.Item is creditCardMaskedType creditCardMaskedType
                ? creditCardMaskedType.cardNumber[^4..]
                : string.Empty;

            var transactionResult = new AuthorizeNetTransactionResult
            {
                IsSuccess = response.messages.resultCode.IsSuccessfulApiResponse(),
                TransactionId = response.transaction.transId,
                TransactionResponseCode = response.transaction?.responseCode.ToString(),
                TransactionStatus = response.transaction.transactionStatus,
                TransactionType = response.transaction.transactionType,
                PaymentData = paymentData,
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
                    expirationDate = request.CreditCard.ExpirationDate
                };
            }
            var order = new orderType
            {
                invoiceNumber = request.OrderId.Substring(20),
            };

            var transactionType = request.PaymentActionType == ModuleConstants.Sale
                ? transactionTypeEnum.authCaptureTransaction
                : transactionTypeEnum.authOnlyTransaction;

            var transactionRequest = new transactionRequestType
            {
                transactionType = transactionType.ToString(), // charge the card
                amount = request.Amount,
                currencyCode = request.CurrencyCode,
                poNumber = request.OrderNumber,
                payment = paymentType,
                order = order,
            };

            if(request.CreditCard != null)
            {
                using var stream = new MemoryStream();
                var proxyHttpClient = _httpClientFactory.CreateClient(request.CreditCard.ProxyHttpClientName);
                var xmlSerializer = new XmlSerializer(typeof(AuthorizeNetCreateTransactionRequest));
                using var xmlWriter = XmlWriter.Create(stream, new XmlWriterSettings
                {
                    Encoding = new UTF8Encoding(false, true), //Exclude BOM
                    Indent = true,
                });
                xmlSerializer.Serialize(xmlWriter, this);
                using var content = new StreamContent(stream);
                content.Headers.ContentType = new MediaTypeHeaderValue(MediaTypeNames.Application.Xml);
                var proxyRequest = new HttpRequestMessage(HttpMethod.Post, new Uri(request.CreditCard.ProxyEndpointUrl))
                {
                    Content = content
                };
                var response = proxyHttpClient.Send(proxyRequest);
                response.EnsureSuccessStatusCode();
                throw new NotImplementedException();
                //TODO: Deserialize response

            }
            else
            {
                return ProcessTransactionRequest(merchantAuthentication, transactionRequest);
            }
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


        private static void SetApiMode(bool isLiveMode)
        {
            ApiOperationBase<ANetApiRequest, ANetApiResponse>.RunEnvironment = isLiveMode
                ? AuthorizeNet.Environment.PRODUCTION
                : AuthorizeNet.Environment.SANDBOX;
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
            else
            {
                var errorResponse = controller.GetErrorResponse();
                return GetResponse(errorResponse);
            }


        }

        private static AuthorizeNetTransactionResult GetTransactionResponse(createTransactionResponse response)
        {
            var result = GetResponse(response);

            if (response.transactionResponse != null)
            {
                result.AccountNumber = response.transactionResponse.accountNumber;
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
