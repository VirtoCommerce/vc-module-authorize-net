using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AuthorizeNet.Api.Contracts.V1;
using AuthorizeNet.Api.Controllers;
using AuthorizeNet.Api.Controllers.Bases;
using VirtoCommerce.AuthorizeNetPayment.Core.Models;
using VirtoCommerce.AuthorizeNetPayment.Core.Services;

namespace VirtoCommerce.AuthorizeNetPayment.Data.Services
{
    public class AuthorizeNetClient : IAuthorizeNetClient
    {
        private static IDictionary<string, TransactionResponse> _transactionResponseMap = new Dictionary<string, TransactionResponse>
        {
            { "1", TransactionResponse.Approved },
            { "2", TransactionResponse.Declined },
            { "3", TransactionResponse.Error },
            { "4", TransactionResponse.HeldForReview },
        };

        public AuthorizeNetAccessTokenResult GetAccessToken(AuthorizeNetAccessTokenRequest request)
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

            var tokenResult = new AuthorizeNetAccessTokenResult
            {
                IsSuccess = response.messages.resultCode == messageTypeEnum.Ok,
                ClientKey = response.publicClientKey,
            };

            return tokenResult;
        }

        public Task<AuthorizeNetAccessTokenResult> GetAccessTokenAsync(AuthorizeNetAccessTokenRequest request)
        {
            var result = GetAccessToken(request);
            return Task.FromResult(result);
        }

        public AuthorizeNetAccessTransactionResult CreateTransactionRequest(AuthorizeNetAccessTransactionRequest request)
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

            var transactionRequest = new transactionRequestType
            {
                transactionType = transactionTypeEnum.authCaptureTransaction.ToString(), // charge the card
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

            return new AuthorizeNetAccessTransactionResult
            {
                IsSuccess = response.messages.resultCode == messageTypeEnum.Ok,

                TransactionResponse = GetTransactionResponse(response.transactionResponse.responseCode),
                TransactionId = response.transactionResponse?.transId,
                TransactionMessages = response.transactionResponse?.messages?.Select(x => new AuthorizeNetAccessTransactionMessage
                {
                    Code = x.code,
                    Description = x.description,
                }).ToList(),
                TransactionErrors = response.transactionResponse?.errors?.Select(x => new AuthorizeNetAccessTransactionMessage
                {
                    Code = x.errorCode,
                    Description = x.errorText,
                }).ToList(),
            };
        }

        public Task<AuthorizeNetAccessTransactionResult> CreateTransactionRequestAsync(AuthorizeNetAccessTransactionRequest request)
        {
            var result = CreateTransactionRequest(request);
            return Task.FromResult(result);
        }

        private static void SetApiMode(bool isLiveMode)
        {
            ApiOperationBase<ANetApiRequest, ANetApiResponse>.RunEnvironment = isLiveMode
                ? AuthorizeNet.Environment.PRODUCTION
                : AuthorizeNet.Environment.SANDBOX;
        }

        private static TransactionResponse GetTransactionResponse(string responseCode)
        {
            var contains = _transactionResponseMap.TryGetValue(responseCode, out var transactionResponse);
            return contains ? transactionResponse : TransactionResponse.UnknownResponse;
        }
    }
}
