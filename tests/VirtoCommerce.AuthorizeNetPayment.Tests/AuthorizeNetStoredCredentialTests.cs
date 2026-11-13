using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using VirtoCommerce.AuthorizeNetPayment.Core;
using VirtoCommerce.AuthorizeNetPayment.Core.Models;
using VirtoCommerce.AuthorizeNetPayment.Data.Providers;
using VirtoCommerce.OrdersModule.Core.Model;
using VirtoCommerce.PaymentModule.Core.Model;
using VirtoCommerce.PaymentModule.Core.Model.StoredCredentials;
using VirtoCommerce.PaymentModule.Model.Requests;
using Xunit;
using static VirtoCommerce.AuthorizeNetPayment.Tests.AuthorizeNetPaymentMethodTestContext;

namespace VirtoCommerce.AuthorizeNetPayment.Tests
{
    public class AuthorizeNetStoredCredentialTests
    {
        private const string OrderId = "0123456789abcdef0123456789abcdef0123";
        private const string StoreCustomerId = "2f5bd7a0-0f8f-4c64-9f0e-6d2a5c1f9b11";

        // Merchant-initiated charges

        [Fact]
        public async Task ProcessStoredPayment_SendsStoredCardAsSubsequentMerchantInitiatedCharge()
        {
            var context = new AuthorizeNetPaymentMethodTestContext();
            AuthorizeNetCreateTransactionRequest sent = null;
            context.Client.Setup(x => x.CreateTransactionAsync(It.IsAny<AuthorizeNetCreateTransactionRequest>()))
                .Callback<AuthorizeNetCreateTransactionRequest>(x => sent = x)
                .ReturnsAsync(Approved());

            var result = await context.Method.ProcessStoredPaymentAsync(ChargeRequest(MerchantInitiatedReason.Resubmission), TestContext.Current.CancellationToken);

            result.Outcome.Should().Be(ChargeOutcome.Succeeded);
            result.IsSuccess.Should().BeTrue();
            result.TransactionId.Should().Be("txn-1");
            result.NetworkTransactionId.Should().Be("new-ntid");
            result.AuthorizationCode.Should().Be("AUTH01");
            result.Amount.Should().Be(25m);
            result.NewPaymentStatus.Should().Be(PaymentStatus.Authorized);

            sent.Profile.CustomerProfileId.Should().Be("profile-1");
            sent.Profile.CustomerPaymentProfileId.Should().Be("payment-profile-1");
            sent.DataValue.Should().BeNull("a stored card is charged by its profile");
            sent.PaymentActionType.Should().Be(ModuleConstants.AuthCapture, "renewals authorize now and capture when the order ships");
            sent.ProcessingOptions.IsSubsequentAuth.Should().BeTrue();
            sent.ProcessingOptions.IsStoredCredentials.Should().BeTrue();
            sent.SubsequentAuthInformation.OriginalNetworkTransactionId.Should().Be("original-ntid");
            sent.SubsequentAuthInformation.Reason.Should().Be("resubmission");
            sent.InvoiceNumber.Should().Be(AuthorizeNetPaymentMethod.GetInvoiceNumber("renewal-1-attempt-1", OrderId));
            sent.DuplicateWindowSeconds.Should().Be(ModuleConstants.StoredPaymentDuplicateWindowSeconds);
        }

        [Theory]
        [InlineData(MerchantInitiatedReason.Recurring, null)]
        [InlineData(MerchantInitiatedReason.Unscheduled, null)]
        [InlineData(MerchantInitiatedReason.DelayedCharge, "delayedCharge")]
        [InlineData(MerchantInitiatedReason.Reauthorization, "reauthorization")]
        public async Task ProcessStoredPayment_MapsReasonToSubsequentAuthReason(MerchantInitiatedReason reason, string expected)
        {
            var context = new AuthorizeNetPaymentMethodTestContext();
            AuthorizeNetCreateTransactionRequest sent = null;
            context.Client.Setup(x => x.CreateTransactionAsync(It.IsAny<AuthorizeNetCreateTransactionRequest>()))
                .Callback<AuthorizeNetCreateTransactionRequest>(x => sent = x)
                .ReturnsAsync(Approved());

            await context.Method.ProcessStoredPaymentAsync(ChargeRequest(reason), TestContext.Current.CancellationToken);

            sent.SubsequentAuthInformation.Reason.Should().Be(expected);
        }

        [Fact]
        public async Task ProcessStoredPayment_AuthorizeAndCapture_IsPaid()
        {
            var context = new AuthorizeNetPaymentMethodTestContext();
            AuthorizeNetCreateTransactionRequest sent = null;
            context.Client.Setup(x => x.CreateTransactionAsync(It.IsAny<AuthorizeNetCreateTransactionRequest>()))
                .Callback<AuthorizeNetCreateTransactionRequest>(x => sent = x)
                .ReturnsAsync(Approved());

            var request = ChargeRequest(MerchantInitiatedReason.Recurring);
            request.CaptureMode = StoredPaymentCaptureMode.AuthorizeAndCapture;
            var result = await context.Method.ProcessStoredPaymentAsync(request, TestContext.Current.CancellationToken);

            sent.PaymentActionType.Should().Be(ModuleConstants.Sale);
            result.NewPaymentStatus.Should().Be(PaymentStatus.Paid);
        }

        [Fact]
        public async Task ProcessStoredPayment_Duplicate_ReportsTheOriginalTransactionInsteadOfChargingAgain()
        {
            var context = new AuthorizeNetPaymentMethodTestContext();
            var duplicate = new AuthorizeNetTransactionResult { TransactionResponseCode = "3", TransactionId = "original-txn" };
            duplicate.TransactionErrors.Add(new AuthorizeNetTransactionMessage { Code = "11", Description = "A duplicate transaction has been submitted." });
            context.Client.Setup(x => x.CreateTransactionAsync(It.IsAny<AuthorizeNetCreateTransactionRequest>())).ReturnsAsync(duplicate);
            context.Client.Setup(x => x.GetTransactionDetailsAsync(It.Is<AuthorizeNetTransactionRequest>(r => r.TransactionId == "original-txn")))
                .ReturnsAsync(new AuthorizeNetTransactionResult
                {
                    IsSuccess = true,
                    TransactionId = "original-txn",
                    TransactionResponseCode = "1",
                    TransactionStatus = "authorizedPendingCapture",
                    AuthAmount = 25m,
                    AuthCode = "AUTH00",
                    NetworkTransactionId = "original-txn-ntid",
                });

            var result = await context.Method.ProcessStoredPaymentAsync(ChargeRequest(MerchantInitiatedReason.Recurring), TestContext.Current.CancellationToken);

            result.Outcome.Should().Be(ChargeOutcome.Succeeded);
            result.TransactionId.Should().Be("original-txn");
            result.Amount.Should().Be(25m);
            context.Client.Verify(x => x.CreateTransactionAsync(It.IsAny<AuthorizeNetCreateTransactionRequest>()), Times.Once);
        }

        [Fact]
        public async Task ProcessStoredPayment_DuplicateOfDecline_ReplaysTheDecline()
        {
            var context = new AuthorizeNetPaymentMethodTestContext();
            var duplicate = new AuthorizeNetTransactionResult { TransactionResponseCode = "3", TransactionId = "declined-txn" };
            duplicate.TransactionErrors.Add(new AuthorizeNetTransactionMessage { Code = "11" });
            context.Client.Setup(x => x.CreateTransactionAsync(It.IsAny<AuthorizeNetCreateTransactionRequest>())).ReturnsAsync(duplicate);
            context.Client.Setup(x => x.GetTransactionDetailsAsync(It.IsAny<AuthorizeNetTransactionRequest>()))
                .ReturnsAsync(new AuthorizeNetTransactionResult { TransactionId = "declined-txn", TransactionResponseCode = "2", TransactionStatus = "declined", ResponseReasonCode = "2" });

            var result = await context.Method.ProcessStoredPaymentAsync(ChargeRequest(MerchantInitiatedReason.Recurring), TestContext.Current.CancellationToken);

            result.Outcome.Should().Be(ChargeOutcome.SoftDecline);
            result.IsSuccess.Should().BeFalse();
        }

        [Fact]
        public async Task ProcessStoredPayment_Decline_KeepsRawCodeAndCustomerMessage()
        {
            var context = new AuthorizeNetPaymentMethodTestContext();
            var declined = new AuthorizeNetTransactionResult { TransactionResponseCode = "2", TransactionId = "txn-2" };
            declined.TransactionErrors.Add(new AuthorizeNetTransactionMessage { Code = "8", Description = "The credit card has expired." });
            context.Client.Setup(x => x.CreateTransactionAsync(It.IsAny<AuthorizeNetCreateTransactionRequest>())).ReturnsAsync(declined);

            var result = await context.Method.ProcessStoredPaymentAsync(ChargeRequest(MerchantInitiatedReason.Recurring), TestContext.Current.CancellationToken);

            result.Outcome.Should().Be(ChargeOutcome.HardDecline);
            result.RawCode.Should().Be("8");
            result.RawMessage.Should().Contain("expired");
            result.ErrorMessage.Should().NotContain("8", "raw provider codes are not shown to customers");
            result.Amount.Should().BeNull();
            result.RetryAfter.Should().BeNull();
        }

        [Fact]
        public async Task ProcessStoredPayment_ClientThrows_IsRetryableProviderError()
        {
            var context = new AuthorizeNetPaymentMethodTestContext();
            context.Client.Setup(x => x.CreateTransactionAsync(It.IsAny<AuthorizeNetCreateTransactionRequest>())).ThrowsAsync(new TimeoutException("gateway timeout"));

            var result = await context.Method.ProcessStoredPaymentAsync(ChargeRequest(MerchantInitiatedReason.Recurring), TestContext.Current.CancellationToken);

            result.Outcome.Should().Be(ChargeOutcome.ProviderError);
            result.RetryAfter.Should().NotBeNull();
            result.RawMessage.Should().Be("gateway timeout");
        }

        [Fact]
        public async Task ProcessStoredPayment_WithoutProfile_NeverCallsTheGateway()
        {
            var context = new AuthorizeNetPaymentMethodTestContext();
            var request = ChargeRequest(MerchantInitiatedReason.Recurring);
            request.StoredPaymentMethod.Token = null;

            var result = await context.Method.ProcessStoredPaymentAsync(request, TestContext.Current.CancellationToken);

            result.Outcome.Should().Be(ChargeOutcome.HardDecline);
            context.Client.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task ProcessStoredPayment_Reauthorization_VoidsTheAuthorizationItReplaces()
        {
            var context = new AuthorizeNetPaymentMethodTestContext();
            context.Client.Setup(x => x.CreateTransactionAsync(It.IsAny<AuthorizeNetCreateTransactionRequest>())).ReturnsAsync(Approved("new-auth"));
            context.Client.Setup(x => x.VoidTransactionAsync(It.IsAny<AuthorizeNetVoidTransactionRequest>())).ReturnsAsync(Approved("void"));

            var request = ChargeRequest(MerchantInitiatedReason.Reauthorization);
            request.Payment = new PaymentIn { Id = "payment-1", OuterId = "expired-auth", Sum = 25m };
            await context.Method.ProcessStoredPaymentAsync(request, TestContext.Current.CancellationToken);

            context.Client.Verify(x => x.VoidTransactionAsync(It.Is<AuthorizeNetVoidTransactionRequest>(r => r.TransactionId == "expired-auth")), Times.Once);
        }

        // Saving a card without a payment

        [Fact]
        public async Task StorePaymentMethod_SavesVerifiedCardWithDetailsFromTheProfile()
        {
            var context = new AuthorizeNetPaymentMethodTestContext();
            AuthorizeNetCustomerProfileRequest sent = null;
            context.Client.Setup(x => x.CreateCustomerProfileAsync(It.IsAny<AuthorizeNetCustomerProfileRequest>()))
                .Callback<AuthorizeNetCustomerProfileRequest>(x => sent = x)
                .ReturnsAsync(new AuthorizeNetCustomerProfileResult { IsSuccess = true, CustomerProfileId = "profile-9", CustomerPaymentProfileId = "payment-profile-9", ValidationResponseCode = "1" });

            var result = await context.Method.StorePaymentMethodAsync(StoreRequest(), TestContext.Current.CancellationToken);

            result.IsSuccess.Should().BeTrue();
            result.Outcome.Should().Be(ChargeOutcome.Succeeded);
            var card = result.StoredPaymentMethod;
            card.Id.Should().BeNull("the caller persists the new card");
            card.ProviderCustomerRef.Should().Be("profile-9");
            card.Token.Should().Be("payment-profile-9");
            card.Brand.Should().Be("Visa");
            card.Last4.Should().Be("1111");
            card.ExpirationYear.Should().Be(2030);
            card.ExpirationMonth.Should().Be(12);
            card.NetworkTransactionId.Should().Be("profile-ntid", "the $0 validation is the first transaction of the stored credential");
            card.ConsentId.Should().Be("consent-1");
            card.PaymentMethodCode.Should().Be(nameof(AuthorizeNetPaymentMethod));

            sent.ValidationMode.Should().Be("liveMode");
            sent.MerchantCustomerId.Should().HaveLength(ModuleConstants.MaxReferenceLength);
            sent.BillTo.Address.Should().Be("1 Main St");
            sent.BillTo.Zip.Should().Be("98004");
            sent.CustomerProfileId.Should().BeNull("the customer has no profile yet");
        }

        [Fact]
        public async Task StorePaymentMethod_CustomerWithAProfile_AddsTheCardToItUsingTheNonceOnce()
        {
            var context = new AuthorizeNetPaymentMethodTestContext();
            AuthorizeNetCustomerLookupRequest lookup = null;
            context.Client.Setup(x => x.FindCustomerProfileIdAsync(It.IsAny<AuthorizeNetCustomerLookupRequest>()))
                .Callback<AuthorizeNetCustomerLookupRequest>(x => lookup = x)
                .ReturnsAsync("profile-existing");
            context.Client.Setup(x => x.CreateCustomerProfileAsync(It.IsAny<AuthorizeNetCustomerProfileRequest>()))
                .ReturnsAsync(new AuthorizeNetCustomerProfileResult { IsSuccess = true, CustomerProfileId = "profile-existing", CustomerPaymentProfileId = "payment-profile-3" });

            var result = await context.Method.StorePaymentMethodAsync(StoreRequest(), TestContext.Current.CancellationToken);

            result.IsSuccess.Should().BeTrue();
            result.StoredPaymentMethod.ProviderCustomerRef.Should().Be("profile-existing");
            lookup.MerchantCustomerId.Should().Be(AuthorizeNetPaymentMethod.GetMerchantCustomerId(StoreCustomerId));
            context.Client.Verify(x => x.CreateCustomerProfileAsync(It.Is<AuthorizeNetCustomerProfileRequest>(r => r.CustomerProfileId == "profile-existing")), Times.Once);
            context.Client.Verify(x => x.CreateCustomerProfileAsync(It.IsAny<AuthorizeNetCustomerProfileRequest>()), Times.Once, "an Accept.js nonce can be used once");
        }

        [Fact]
        public async Task StorePaymentMethod_CardAlreadyInTheProfile_ReturnsItsExistingPaymentProfile()
        {
            var context = new AuthorizeNetPaymentMethodTestContext();
            context.Client.Setup(x => x.FindCustomerProfileIdAsync(It.IsAny<AuthorizeNetCustomerLookupRequest>())).ReturnsAsync("profile-1");
            // the same card again: an error that carries the existing payment profile's ID
            context.Client.Setup(x => x.CreateCustomerProfileAsync(It.IsAny<AuthorizeNetCustomerProfileRequest>()))
                .ReturnsAsync(new AuthorizeNetCustomerProfileResult { IsSuccess = false, CustomerProfileId = "profile-1", CustomerPaymentProfileId = "payment-profile-1", DuplicateId = string.Empty });

            var result = await context.Method.StorePaymentMethodAsync(StoreRequest(), TestContext.Current.CancellationToken);

            result.IsSuccess.Should().BeTrue();
            result.StoredPaymentMethod.Id.Should().BeNull("the caller decides whether it is a card it already has");
            result.StoredPaymentMethod.ProviderCustomerRef.Should().Be("profile-1");
            result.StoredPaymentMethod.Token.Should().Be("payment-profile-1");
        }

        [Fact]
        public async Task StorePaymentMethod_DeclinedValidation_IsNotSaved()
        {
            var context = new AuthorizeNetPaymentMethodTestContext();
            var failed = new AuthorizeNetCustomerProfileResult { IsSuccess = false, ValidationResponseCode = "2", ValidationReasonCode = "2", ValidationReasonText = "This transaction has been declined." };
            failed.Errors.Add(new AuthorizeNetTransactionMessage { Code = "E00027", Description = "The transaction was unsuccessful." });
            context.Client.Setup(x => x.CreateCustomerProfileAsync(It.IsAny<AuthorizeNetCustomerProfileRequest>())).ReturnsAsync(failed);

            var result = await context.Method.StorePaymentMethodAsync(StoreRequest(), TestContext.Current.CancellationToken);

            result.IsSuccess.Should().BeFalse();
            result.Outcome.Should().Be(ChargeOutcome.SoftDecline);
            result.StoredPaymentMethod.Should().BeNull();
            result.RawCode.Should().Be("2");
        }

        [Fact]
        public async Task StorePaymentMethod_VerifyWithoutBillingAddress_AsksForItWithoutCallingTheGateway()
        {
            var context = new AuthorizeNetPaymentMethodTestContext();
            var request = StoreRequest();
            request.Parameters.Remove(ModuleConstants.BillingAddressParamName);

            var result = await context.Method.StorePaymentMethodAsync(request, TestContext.Current.CancellationToken);

            result.IsSuccess.Should().BeFalse();
            result.ErrorMessage.Should().Contain("billing address");
            context.Client.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task StorePaymentMethod_WithoutNonce_Fails()
        {
            var context = new AuthorizeNetPaymentMethodTestContext();
            var request = StoreRequest();
            request.Parameters.Remove(ModuleConstants.DataValueParamName);

            var result = await context.Method.StorePaymentMethodAsync(request, TestContext.Current.CancellationToken);

            result.IsSuccess.Should().BeFalse();
            context.Client.VerifyNoOtherCalls();
        }

        // Deleting a card

        [Fact]
        public async Task DeleteStoredPaymentMethod_DeletesThePaymentProfile()
        {
            var context = new AuthorizeNetPaymentMethodTestContext();
            context.Client.Setup(x => x.DeleteCustomerPaymentProfileAsync(It.IsAny<AuthorizeNetPaymentProfileRequest>())).ReturnsAsync(new AuthorizeNetBaseResult { IsSuccess = true });

            var result = await context.Method.DeleteStoredPaymentMethodAsync(new DeleteStoredPaymentMethodRequest { StoredPaymentMethod = SavedCard() }, TestContext.Current.CancellationToken);

            result.IsSuccess.Should().BeTrue();
            context.Client.Verify(x => x.DeleteCustomerPaymentProfileAsync(It.Is<AuthorizeNetPaymentProfileRequest>(r => r.CustomerProfileId == "profile-1" && r.CustomerPaymentProfileId == "payment-profile-1")), Times.Once);
        }

        // Checkout

        [Fact]
        public async Task PostProcessPayment_StoreForFutureUse_SavesTheCardLinkedToTheOrder()
        {
            var context = new AuthorizeNetPaymentMethodTestContext();
            AuthorizeNetCreateTransactionRequest sent = null;
            context.Client.Setup(x => x.CreateTransactionAsync(It.IsAny<AuthorizeNetCreateTransactionRequest>()))
                .Callback<AuthorizeNetCreateTransactionRequest>(x => sent = x)
                .ReturnsAsync(Approved("checkout-txn"));
            context.Client.Setup(x => x.CreateCustomerProfileFromTransactionAsync(It.IsAny<AuthorizeNetProfileFromTransactionRequest>()))
                .ReturnsAsync(new AuthorizeNetCustomerProfileResult { IsSuccess = true, CustomerProfileId = "profile-5", CustomerPaymentProfileId = "payment-profile-5" });

            var request = CheckoutRequest();
            request.StoreForFutureUse = true;
            request.StoredCredentialUsage = StoredCredentialUsage.Recurring;
            var result = await context.Method.PostProcessPaymentAsync(request, TestContext.Current.CancellationToken);

            result.IsSuccess.Should().BeTrue();
            sent.ProcessingOptions.IsFirstRecurringPayment.Should().BeTrue("a card saved for a subscription pays the following renewals");

            var card = result.StoredPaymentMethod;
            card.Should().NotBeNull("the connector returns the card it saved; the caller records it");
            card.Id.Should().BeNull();
            card.ProviderCustomerRef.Should().Be("profile-5");
            card.Token.Should().Be("payment-profile-5");
            card.PaymentMethodCode.Should().Be(nameof(AuthorizeNetPaymentMethod));
            card.InitialOrderId.Should().Be(OrderId);
            card.InitialPaymentId.Should().Be("payment-1");
            card.InitialTransactionRef.Should().Be("checkout-txn");
            card.NetworkTransactionId.Should().Be("new-ntid");
            card.Usage.Should().Be(StoredCredentialUsage.Recurring);
            card.ExpirationYear.Should().Be(2030);
        }

        [Fact]
        public async Task PostProcessPayment_WithoutStoreForFutureUse_SavesNothing()
        {
            var context = new AuthorizeNetPaymentMethodTestContext();
            context.Client.Setup(x => x.CreateTransactionAsync(It.IsAny<AuthorizeNetCreateTransactionRequest>())).ReturnsAsync(Approved());

            var result = await context.Method.PostProcessPaymentAsync(CheckoutRequest(), TestContext.Current.CancellationToken);

            result.StoredPaymentMethod.Should().BeNull();
            context.Client.Verify(x => x.CreateCustomerProfileFromTransactionAsync(It.IsAny<AuthorizeNetProfileFromTransactionRequest>()), Times.Never);
        }

        [Fact]
        public async Task PostProcessPayment_ProfileNotCreated_StillPaysTheOrder()
        {
            var context = new AuthorizeNetPaymentMethodTestContext();
            context.Client.Setup(x => x.CreateTransactionAsync(It.IsAny<AuthorizeNetCreateTransactionRequest>())).ReturnsAsync(Approved());
            context.Client.Setup(x => x.CreateCustomerProfileFromTransactionAsync(It.IsAny<AuthorizeNetProfileFromTransactionRequest>())).ThrowsAsync(new InvalidOperationException("CIM down"));

            var request = CheckoutRequest();
            request.StoreForFutureUse = true;
            var result = await context.Method.PostProcessPaymentAsync(request, TestContext.Current.CancellationToken);

            result.IsSuccess.Should().BeTrue("saving the card is best effort; the payment itself succeeded");
            result.StoredPaymentMethod.Should().BeNull();
        }

        [Fact]
        public async Task PostProcessPayment_WithSavedCard_ChargesItAsStoredCredential()
        {
            var context = new AuthorizeNetPaymentMethodTestContext();
            AuthorizeNetCreateTransactionRequest sent = null;
            context.Client.Setup(x => x.CreateTransactionAsync(It.IsAny<AuthorizeNetCreateTransactionRequest>()))
                .Callback<AuthorizeNetCreateTransactionRequest>(x => sent = x)
                .ReturnsAsync(Approved());

            var request = CheckoutRequest(withNonce: false);
            request.StoredPaymentMethod = SavedCard();
            var result = await context.Method.PostProcessPaymentAsync(request, TestContext.Current.CancellationToken);

            result.IsSuccess.Should().BeTrue();
            result.StoredPaymentMethod.Should().BeNull("nothing new was saved");
            sent.Profile.CustomerPaymentProfileId.Should().Be("payment-profile-1");
            sent.DataValue.Should().BeNull();
            sent.ProcessingOptions.IsStoredCredentials.Should().BeTrue();
            sent.ProcessingOptions.IsSubsequentAuth.Should().BeFalse("the customer is present");
        }

        [Fact]
        public async Task PostProcessPayment_WithSavedCardWithoutProfile_IsRefusedWithoutCharging()
        {
            var context = new AuthorizeNetPaymentMethodTestContext();
            var card = SavedCard();
            card.Token = null;

            var request = CheckoutRequest(withNonce: false);
            request.StoredPaymentMethod = card;
            var result = await context.Method.PostProcessPaymentAsync(request, TestContext.Current.CancellationToken);

            result.IsSuccess.Should().BeFalse();
            context.Client.Verify(x => x.CreateTransactionAsync(It.IsAny<AuthorizeNetCreateTransactionRequest>()), Times.Never);
        }

        // Partial capture and refund

        [Fact]
        public async Task Capture_PartialAmount_CapturesOnlyThatAmount()
        {
            var context = new AuthorizeNetPaymentMethodTestContext();
            AuthorizeNetCaptureTransactionRequest sent = null;
            context.Client.Setup(x => x.CaptureTransactionAsync(It.IsAny<AuthorizeNetCaptureTransactionRequest>()))
                .Callback<AuthorizeNetCaptureTransactionRequest>(x => sent = x)
                .ReturnsAsync(Approved());

            var result = await context.Method.CaptureProcessPaymentAsync(new CapturePaymentRequest
            {
                Payment = new PaymentIn { OuterId = "auth-1", Sum = 25m, PaymentStatus = PaymentStatus.Authorized },
                CaptureAmount = 18.5m,
            }, TestContext.Current.CancellationToken);

            result.IsSuccess.Should().BeTrue();
            sent.TransactionAmount.Should().Be(18.5m);
        }

        [Fact]
        public async Task Refund_PartialAmountOfSettledPayment_IsPartiallyRefunded()
        {
            var context = new AuthorizeNetPaymentMethodTestContext();
            context.Client.Setup(x => x.GetTransactionDetailsAsync(It.IsAny<AuthorizeNetTransactionRequest>()))
                .ReturnsAsync(new AuthorizeNetTransactionResult { TransactionStatus = "settledSuccessfully", PaymentData = "1111" });
            AuthorizeNetRefundTransactionRequest sent = null;
            context.Client.Setup(x => x.RefundTransactionAsync(It.IsAny<AuthorizeNetRefundTransactionRequest>()))
                .Callback<AuthorizeNetRefundTransactionRequest>(x => sent = x)
                .ReturnsAsync(Approved("refund-1"));

            var payment = new PaymentIn { OuterId = "txn-1", Sum = 25m, PaymentStatus = PaymentStatus.Paid, IsApproved = true };
            var result = await context.Method.RefundProcessPaymentAsync(new RefundPaymentRequest { Payment = payment, AmountToRefund = 10m, IsPartialRefund = true }, TestContext.Current.CancellationToken);

            result.IsSuccess.Should().BeTrue();
            result.NewPaymentStatus.Should().Be(PaymentStatus.PartiallyRefunded);
            sent.TransactionAmount.Should().Be(10m);
        }

        [Fact]
        public async Task Refund_FullAmountOfSettledPayment_IsRefunded()
        {
            var context = new AuthorizeNetPaymentMethodTestContext();
            context.Client.Setup(x => x.GetTransactionDetailsAsync(It.IsAny<AuthorizeNetTransactionRequest>()))
                .ReturnsAsync(new AuthorizeNetTransactionResult { TransactionStatus = "settledSuccessfully", PaymentData = "1111" });
            context.Client.Setup(x => x.RefundTransactionAsync(It.IsAny<AuthorizeNetRefundTransactionRequest>())).ReturnsAsync(Approved("refund-1"));

            var payment = new PaymentIn { OuterId = "txn-1", Sum = 25m, PaymentStatus = PaymentStatus.Paid, IsApproved = true };
            var result = await context.Method.RefundProcessPaymentAsync(new RefundPaymentRequest { Payment = payment }, TestContext.Current.CancellationToken);

            result.IsSuccess.Should().BeTrue();
            result.NewPaymentStatus.Should().Be(PaymentStatus.Refunded);
        }

        [Fact]
        public async Task Refund_PartialAmountBeforeSettlement_IsRefusedInsteadOfVoidingInFull()
        {
            var context = new AuthorizeNetPaymentMethodTestContext();
            context.Client.Setup(x => x.GetTransactionDetailsAsync(It.IsAny<AuthorizeNetTransactionRequest>()))
                .ReturnsAsync(new AuthorizeNetTransactionResult { TransactionStatus = "capturedPendingSettlement" });

            var payment = new PaymentIn { OuterId = "txn-1", Sum = 25m, PaymentStatus = PaymentStatus.Paid, IsApproved = true };
            var result = await context.Method.RefundProcessPaymentAsync(new RefundPaymentRequest { Payment = payment, AmountToRefund = 10m, IsPartialRefund = true }, TestContext.Current.CancellationToken);

            result.IsSuccess.Should().BeFalse();
            result.ErrorMessage.Should().Contain("settled");
            context.Client.Verify(x => x.VoidTransactionAsync(It.IsAny<AuthorizeNetVoidTransactionRequest>()), Times.Never);
        }

        // References

        [Fact]
        public void GetInvoiceNumber_IsStablePerKeyAndFitsTheGatewayLimit()
        {
            var first = AuthorizeNetPaymentMethod.GetInvoiceNumber("renewal-1-attempt-1", OrderId);

            first.Should().HaveLength(ModuleConstants.MaxReferenceLength);
            AuthorizeNetPaymentMethod.GetInvoiceNumber("renewal-1-attempt-1", OrderId).Should().Be(first);
            AuthorizeNetPaymentMethod.GetInvoiceNumber("renewal-1-attempt-2", OrderId).Should().NotBe(first);
            AuthorizeNetPaymentMethod.GetInvoiceNumber(null, OrderId).Should().Be(OrderId[^ModuleConstants.MaxReferenceLength..]);
        }

        [Theory]
        [InlineData("2f5bd7a0-0f8f-4c64-9f0e-6d2a5c1f9b11", "2f5bd7a00f8f4c649f0e")]
        [InlineData("short-id", "shortid")]
        [InlineData(null, null)]
        public void GetMerchantCustomerId_FitsTheGatewayLimit(string customerId, string expected)
        {
            AuthorizeNetPaymentMethod.GetMerchantCustomerId(customerId).Should().Be(expected);
        }

        private static ProcessStoredPaymentRequest ChargeRequest(MerchantInitiatedReason reason)
        {
            return new ProcessStoredPaymentRequest
            {
                OrderId = OrderId,
                Order = new CustomerOrder { Id = OrderId, Number = "CO-1" },
                PaymentId = "payment-1",
                StoredPaymentMethod = SavedCard(),
                Amount = 25m,
                Currency = "USD",
                IdempotencyKey = "renewal-1-attempt-1",
                Reason = reason,
                CaptureMode = StoredPaymentCaptureMode.AuthorizeOnly,
            };
        }

        private static StorePaymentMethodRequest StoreRequest()
        {
            return new StorePaymentMethodRequest
            {
                StoreId = "store-1",
                CustomerId = StoreCustomerId,
                ConsentId = "consent-1",
                Usage = StoredCredentialUsage.Recurring,
                Verify = true,
                Parameters = new NameValueCollection
                {
                    [ModuleConstants.DataDescriptorParamName] = "COMMON.ACCEPT.INAPP.PAYMENT",
                    [ModuleConstants.DataValueParamName] = "nonce",
                    [ModuleConstants.BillingAddressParamName] = "1 Main St",
                    [ModuleConstants.BillingZipParamName] = "98004",
                },
            };
        }

        private static PostProcessPaymentRequest CheckoutRequest(bool withNonce = true)
        {
            var payment = new PaymentIn { Id = "payment-1", Sum = 25m, Currency = "USD", PaymentStatus = PaymentStatus.Pending };
            var order = new CustomerOrder { Id = OrderId, Number = "CO-1", CustomerId = "customer-1", StoreId = "store-1", InPayments = [payment] };
            var parameters = new NameValueCollection();
            if (withNonce)
            {
                parameters[ModuleConstants.DataDescriptorParamName] = "COMMON.ACCEPT.INAPP.PAYMENT";
                parameters[ModuleConstants.DataValueParamName] = "nonce";
            }

            return new PostProcessPaymentRequest { Order = order, OrderId = order.Id, Payment = payment, PaymentId = payment.Id, Parameters = parameters };
        }
    }
}
