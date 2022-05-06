using System.Linq;
using System.Threading.Tasks;
using AuthorizeNet.Api.Contracts.V1;
using AuthorizeNet.Api.Controllers;
using AuthorizeNet.Api.Controllers.Bases;
using VirtoCommerce.AuthorizeNetPayment.Core;
using VirtoCommerce.AuthorizeNetPayment.Core.Models;
using VirtoCommerce.AuthorizeNetPayment.Core.Services;

namespace VirtoCommerce.AuthorizeNetPayment.Data.Services
{
    public class AuthorizeNetClient : IAuthorizeNetClient
    {
        public AuthorizeNetTokenResult GetAccessToken(AuthorizeNetTokenRequest request)
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
                IsSuccess = IsSuccessfulApiResponse(response.messages.resultCode),
                ClientKey = response.publicClientKey,
            };

            return tokenResult;
        }

        public Task<AuthorizeNetTokenResult> GetAccessTokenAsync(AuthorizeNetTokenRequest request)
        {
            var result = GetAccessToken(request);
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
                IsSuccess = IsSuccessfulApiResponse(response.messages.resultCode),
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
            };

            var createTransactionRequest = new createTransactionRequest
            {
                merchantAuthentication = merchantAuthentication,
                transactionRequest = transactionRequest
            };

            // instantiate the controller that will call the service
            var controller = new createTransactionController(createTransactionRequest);
            controller.Execute();

            var response = controller.GetApiResponse();

            // move to private
            return new AuthorizeNetTransactionResult
            {
                IsSuccess = IsSuccessfulApiResponse(response.messages.resultCode),
                TransactionResponseCode = response.transactionResponse?.responseCode,
                TransactionId = response.transactionResponse?.transId,
                TransactionMessages = response.transactionResponse?.messages?.Select(x => new AuthorizeNetTransactionMessage
                {
                    Code = x.code,
                    Description = x.description,
                }).ToList(),
                TransactionErrors = response.transactionResponse?.errors?.Select(x => new AuthorizeNetTransactionMessage
                {
                    Code = x.errorCode,
                    Description = x.errorText,
                }).ToList(),
            };
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

            var createTransactionRequest = new createTransactionRequest
            {
                merchantAuthentication = merchantAuthentication,
                transactionRequest = transactionRequest
            };

            // instantiate the controller that will call the service
            var controller = new createTransactionController(createTransactionRequest);
            controller.Execute();

            // get the response from the service (errors contained if any)
            var response = controller.GetApiResponse();

            // move to private
            return new AuthorizeNetTransactionResult
            {
                IsSuccess = IsSuccessfulApiResponse(response.messages.resultCode),
                TransactionResponseCode = response.transactionResponse?.responseCode,
                TransactionId = response.transactionResponse?.transId,
                TransactionMessages = response.transactionResponse?.messages?.Select(x => new AuthorizeNetTransactionMessage
                {
                    Code = x.code,
                    Description = x.description,
                }).ToList(),
                TransactionErrors = response.transactionResponse?.errors?.Select(x => new AuthorizeNetTransactionMessage
                {
                    Code = x.errorCode,
                    Description = x.errorText,
                }).ToList(),
            };
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

            var refundTransactionRequest = new createTransactionRequest
            {
                merchantAuthentication = merchantAuthentication,
                transactionRequest = transactionRequest
            };

            // instantiate the controller that will call the service
            var controller = new createTransactionController(refundTransactionRequest);
            controller.Execute();

            // get the response from the service (errors contained if any)
            var response = controller.GetApiResponse();

            // move to private
            return new AuthorizeNetTransactionResult
            {
                IsSuccess = IsSuccessfulApiResponse(response.messages.resultCode),
                TransactionResponseCode = response.transactionResponse?.responseCode,
                TransactionId = response.transactionResponse?.transId,
                TransactionMessages = response.transactionResponse?.messages?.Select(x => new AuthorizeNetTransactionMessage
                {
                    Code = x.code,
                    Description = x.description,
                }).ToList(),
                TransactionErrors = response.transactionResponse?.errors?.Select(x => new AuthorizeNetTransactionMessage
                {
                    Code = x.errorCode,
                    Description = x.errorText,
                }).ToList(),
            };
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

            var voidTransactionRequest = new createTransactionRequest
            {
                merchantAuthentication = merchantAuthentication,
                transactionRequest = transactionRequest
            };

            // instantiate the controller that will call the service
            var controller = new createTransactionController(voidTransactionRequest);
            controller.Execute();

            // get the response from the service (errors contained if any)
            var response = controller.GetApiResponse();

            return new AuthorizeNetTransactionResult
            {
                IsSuccess = IsSuccessfulApiResponse(response.messages.resultCode),
                TransactionResponseCode = response.transactionResponse?.responseCode,
                TransactionId = response.transactionResponse?.transId,
                TransactionMessages = response.transactionResponse?.messages?.Select(x => new AuthorizeNetTransactionMessage
                {
                    Code = x.code,
                    Description = x.description,
                }).ToList(),
                TransactionErrors = response.transactionResponse?.errors?.Select(x => new AuthorizeNetTransactionMessage
                {
                    Code = x.errorCode,
                    Description = x.errorText,
                }).ToList(),
            };
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

        private static bool IsSuccessfulApiResponse(messageTypeEnum messageType)
        {
            return messageType == messageTypeEnum.Ok;
        }
    }
}
