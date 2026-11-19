using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VirtoCommerce.AuthorizeNetPayment.Core;
using VirtoCommerce.OrdersModule.Core.Model;
using VirtoCommerce.PaymentModule.Core.Model;
using VirtoCommerce.PaymentModule.Model.Requests;
using VirtoCommerce.ProductSubscription.ConnectorTestKit;

namespace VirtoCommerce.AuthorizeNetPayment.Tests.Sandbox
{
    /// <summary>
    /// The stored credential contract tests against the Authorize.Net sandbox.
    /// Skipped unless <c>AUTHORIZENET_API_LOGIN_ID</c> and <c>AUTHORIZENET_TRANSACTION_KEY</c> are set.
    /// </summary>
    public class AuthorizeNetConnectorContractTests : StoredCredentialConnectorContractTests
    {
        public AuthorizeNetConnectorContractTests()
            : base(() => AuthorizeNetSandbox.CreateConnector(), new AuthorizeNetSandboxScenarios())
        {
        }

        /// <summary>
        /// An Accept payment nonce is single-use, so every store request gets a new one for the scenario's test card.
        /// </summary>
        protected override StorePaymentMethodRequest CreateStoreRequest(CardScenario card)
        {
            var request = base.CreateStoreRequest(card);
            var testCard = card.StoreParameters;

            request.Parameters = string.IsNullOrEmpty(testCard?[AuthorizeNetSandboxScenarios.CardNumberKey]) || !AuthorizeNetSandbox.IsConfigured
                ? new NameValueCollection()
                : AuthorizeNetSandbox.GetCardParametersAsync(
                    testCard[AuthorizeNetSandboxScenarios.CardNumberKey],
                    testCard[AuthorizeNetSandboxScenarios.ExpirationKey],
                    testCard[AuthorizeNetSandboxScenarios.CardCodeKey],
                    CancellationToken).GetAwaiter().GetResult();

            // The billing address the storefront collects with the card; the $0 validation needs it for AVS.
            request.Parameters[ModuleConstants.BillingFirstNameParamName] = "Contract";
            request.Parameters[ModuleConstants.BillingLastNameParamName] = "Test";
            request.Parameters[ModuleConstants.BillingAddressParamName] = "1 Main St";
            request.Parameters[ModuleConstants.BillingZipParamName] = "98004";

            return request;
        }
    }

    /// <summary>
    /// Sandbox inputs. The sandbox answers card 4222222222222 with the response reason code equal to the amount in dollars,
    /// e.g. $8.00 for reason 8 (expired card). https://developer.authorize.net/hello_world/testing_guide.html
    /// </summary>
    public class AuthorizeNetSandboxScenarios : ConnectorTestScenarios
    {
        public const string CardNumberKey = "testCardNumber";
        public const string ExpirationKey = "testCardExpiration";
        public const string CardCodeKey = "testCardCode";

        // The sandbox's card code filter matches "900" only.
        private static readonly CardScenario _visa = TestCard("Visa 1111 (approved)", "4111111111111111", "Visa", "1111");
        private static readonly CardScenario _responseByAmount = TestCard("Visa 2222 (response reason code = amount)", "4222222222222", "Visa", "2222");

        public override string SkipReason => AuthorizeNetSandbox.SkipReason;

        public override ChargeScenario Success => new() { Description = "Visa 1111 approved", Card = _visa, Amount = 10.17m };

        public override ChargeScenario SoftDecline => new()
        {
            Description = "reason 2: declined (generic, e.g. insufficient funds)",
            Card = _responseByAmount,
            Amount = 2m,
            ExpectedRawCode = "2",
        };

        public override ChargeScenario HardDecline => new()
        {
            Description = "reason 8: the card has expired",
            Card = _responseByAmount,
            Amount = 8m,
            ExpectedRawCode = "8",
        };

        public override ProviderErrorScenario ProviderError => new()
        {
            Description = "reason 19: processing error, try again later",
            Card = _responseByAmount,
            Amount = 19m,
            ExpectedRawCode = "19",
            ExpectRetryAfter = true,
        };

        /// <summary>
        /// Merchant-initiated charges send no card code. An account whose card code filter rejects "P" (not processed)
        /// declines every one of them with reason 65, so those tests are reported as known issues with the fix until the
        /// filter allows it. They run again as soon as the account setting is changed.
        /// </summary>
        public override IReadOnlyDictionary<string, string> KnownIssues =>
            AuthorizeNetSandbox.IsConfigured && AuthorizeNetSandbox.RejectsChargesWithoutCardCode.Value
                ? _chargeTests.ToDictionary(x => x, _ => AuthorizeNetSandbox.CardCodeFilterIssue)
                : new Dictionary<string, string>();

        private static readonly string[] _chargeTests =
        [
            "Charge_Subsequent_ReferencesOriginalNetworkTransactionId",
            "Charge_AuthorizeOnly_IsAuthorizedWithTransactionAndAmount",
            "Charge_AuthorizeAndCapture_IsPaid",
            "Charge_EveryMerchantInitiatedReason_IsAccepted",
            "Charge_SameIdempotencyKey_NeverChargesTwice",
            "Charge_DifferentIdempotencyKeys_ChargeIndependently",
            "Charge_SameIdempotencyKey_ReplaysDecline",
            "Charge_SoftDecline_IsClassifiedWithCodesAndRetryTiming",
            "Charge_HardDecline_IsClassifiedWithRawCodeAndNoRetry",
            "Charge_ProviderError_IsClassifiedAndRetryableWithSameKey",
            "AuthorizeOnly_ThenCapture_IsPaid",
        ];

        public override Task<string> GetReferencedNetworkTransactionIdAsync(ProcessStoredPaymentRequest request, ProcessStoredPaymentRequestResult result, CancellationToken cancellationToken)
        {
            return string.IsNullOrEmpty(result.TransactionId)
                ? Task.FromResult<string>(null)
                : AuthorizeNetSandbox.GetOriginalNetworkTransactionIdAsync(result.TransactionId);
        }

        /// <summary>
        /// The Orders module passes the order payment; Authorize.Net captures its <c>OuterId</c> transaction.
        /// </summary>
        public override CapturePaymentRequest CreateCaptureRequest(ProcessStoredPaymentRequest authorizationRequest, ProcessStoredPaymentRequestResult authorization)
        {
            var request = base.CreateCaptureRequest(authorizationRequest, authorization);
            request.Payment = new PaymentIn
            {
                Id = authorizationRequest.PaymentId,
                OuterId = authorization.TransactionId,
                Sum = authorization.Amount ?? authorizationRequest.Amount,
                Currency = authorizationRequest.Currency,
                PaymentStatus = PaymentStatus.Authorized,
            };

            return request;
        }

        private static CardScenario TestCard(string description, string number, string brand, string last4)
        {
            return new CardScenario
            {
                Description = description,
                CardNumber = number,
                ExpectedBrand = brand,
                ExpectedLast4 = last4,
                StoreParameters = new NameValueCollection
                {
                    [CardNumberKey] = number,
                    [ExpirationKey] = "122030",
                    [CardCodeKey] = "900",
                },
            };
        }
    }
}
