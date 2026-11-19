using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using VirtoCommerce.AuthorizeNetPayment.Core;
using VirtoCommerce.AuthorizeNetPayment.Core.Models;
using VirtoCommerce.AuthorizeNetPayment.Core.Services;
using VirtoCommerce.AuthorizeNetPayment.Data.Providers;
using VirtoCommerce.PaymentModule.Core.Model.StoredCredentials;
using VirtoCommerce.Platform.Core.Settings;

namespace VirtoCommerce.AuthorizeNetPayment.Tests
{
    /// <summary>
    /// The Authorize.Net payment method over a mocked API client.
    /// </summary>
    internal sealed class AuthorizeNetPaymentMethodTestContext
    {
        public Mock<IAuthorizeNetClient> Client { get; } = new();

        public AuthorizeNetPaymentMethod Method { get; }

        public AuthorizeNetPaymentMethodTestContext(string paymentActionType = ModuleConstants.AuthCapture)
        {
            Client
                .Setup(x => x.GetCustomerPaymentProfileAsync(It.IsAny<AuthorizeNetPaymentProfileRequest>()))
                .ReturnsAsync(new AuthorizeNetPaymentProfileResult
                {
                    IsSuccess = true,
                    CardNumber = "XXXX1111",
                    CardType = "Visa",
                    ExpirationDate = "2030-12",
                    OriginalNetworkTransactionId = "profile-ntid",
                });

            Method = new AuthorizeNetPaymentMethod(
                Client.Object,
                new Mock<IAuthorizeNetCheckoutService>().Object,
                Options.Create(new AuthorizeNetPaymentMethodOptions { ApiLogin = "login", TxnKey = "key" }),
                NullLogger<AuthorizeNetPaymentMethod>.Instance)
            {
                Settings =
                [
                    new ObjectSettingEntry(ModuleConstants.Settings.General.Mode) { Value = ModuleConstants.Test },
                    new ObjectSettingEntry(ModuleConstants.Settings.General.PaymentActionType) { Value = paymentActionType },
                ],
            };
        }

        public static StoredPaymentMethod SavedCard(string id = "card-1", string customerId = "customer-1")
        {
            return new StoredPaymentMethod
            {
                Id = id,
                StoreId = "store-1",
                CustomerId = customerId,
                PaymentMethodCode = nameof(AuthorizeNetPaymentMethod),
                ProviderCustomerRef = "profile-1",
                Token = "payment-profile-1",
                Brand = "Visa",
                Last4 = "1111",
                NetworkTransactionId = "original-ntid",
                Status = StoredPaymentMethodStatus.Active,
            };
        }

        public static AuthorizeNetTransactionResult Approved(string transactionId = "txn-1")
        {
            return new AuthorizeNetTransactionResult
            {
                IsSuccess = true,
                TransactionResponseCode = "1",
                TransactionId = transactionId,
                NetworkTransactionId = "new-ntid",
                AuthCode = "AUTH01",
                AccountNumber = "XXXX1111",
                AccountType = "Visa",
            };
        }
    }
}
