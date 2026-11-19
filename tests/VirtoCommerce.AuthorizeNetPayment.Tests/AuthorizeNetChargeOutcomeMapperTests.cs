using System;
using FluentAssertions;
using VirtoCommerce.AuthorizeNetPayment.Core.Models;
using VirtoCommerce.AuthorizeNetPayment.Data.Services;
using VirtoCommerce.PaymentModule.Core.Model.StoredCredentials;
using Xunit;

namespace VirtoCommerce.AuthorizeNetPayment.Tests
{
    public class AuthorizeNetChargeOutcomeMapperTests
    {
        [Theory]
        // Approved
        [InlineData("1", null, ChargeOutcome.Succeeded)]
        // The card can't be charged again
        [InlineData("2", "4", ChargeOutcome.HardDecline)]
        [InlineData("3", "6", ChargeOutcome.HardDecline)]
        [InlineData("3", "8", ChargeOutcome.HardDecline)]
        [InlineData("2", "17", ChargeOutcome.HardDecline)]
        [InlineData("3", "37", ChargeOutcome.HardDecline)]
        [InlineData("2", "317", ChargeOutcome.HardDecline)]
        // Generic declines and referrals: worth retrying later
        [InlineData("2", "2", ChargeOutcome.SoftDecline)]
        [InlineData("2", "3", ChargeOutcome.SoftDecline)]
        [InlineData("2", null, ChargeOutcome.SoftDecline)]
        // The merchant's own filters: the card is fine
        [InlineData("2", "27", ChargeOutcome.ProviderError)]
        [InlineData("2", "65", ChargeOutcome.ProviderError)]
        [InlineData("2", "252", ChargeOutcome.ProviderError)]
        // Processor errors and held transactions
        [InlineData("3", "19", ChargeOutcome.ProviderError)]
        [InlineData("3", "120", ChargeOutcome.ProviderError)]
        [InlineData("4", null, ChargeOutcome.ProviderError)]
        [InlineData("3", "11", ChargeOutcome.ProviderError)]
        public void GetOutcome_MapsResponseAndReasonCodes(string responseCode, string reasonCode, ChargeOutcome expected)
        {
            var result = new AuthorizeNetTransactionResult { TransactionResponseCode = responseCode };
            if (reasonCode != null)
            {
                result.TransactionErrors.Add(new AuthorizeNetTransactionMessage { Code = reasonCode });
            }

            AuthorizeNetChargeOutcomeMapper.GetOutcome(result).Should().Be(expected);
        }

        [Fact]
        public void GetOutcome_DeletedProfile_IsHardDecline()
        {
            var result = new AuthorizeNetTransactionResult();
            result.Errors.Add(new AuthorizeNetTransactionMessage { Code = "E00040", Description = "The record cannot be found." });

            AuthorizeNetChargeOutcomeMapper.GetOutcome(result).Should().Be(ChargeOutcome.HardDecline);
        }

        [Theory]
        [InlineData("authorizedPendingCapture", null, ChargeOutcome.Succeeded)]
        [InlineData("capturedPendingSettlement", null, ChargeOutcome.Succeeded)]
        [InlineData("settledSuccessfully", null, ChargeOutcome.Succeeded)]
        [InlineData("declined", "2", ChargeOutcome.SoftDecline)]
        [InlineData("declined", "8", ChargeOutcome.HardDecline)]
        [InlineData("FDSPendingReview", null, ChargeOutcome.ProviderError)]
        [InlineData("voided", null, ChargeOutcome.ProviderError)]
        public void GetOutcomeByStatus_MapsTransactionDetails(string status, string reasonCode, ChargeOutcome expected)
        {
            var details = new AuthorizeNetTransactionResult { TransactionStatus = status, ResponseReasonCode = reasonCode };

            AuthorizeNetChargeOutcomeMapper.GetOutcomeByStatus(details).Should().Be(expected);
        }

        [Fact]
        public void GetRetryAfter_ProcessorError_RetriesSoon()
        {
            var result = new AuthorizeNetTransactionResult { TransactionResponseCode = "3" };
            result.TransactionErrors.Add(new AuthorizeNetTransactionMessage { Code = "19" });

            AuthorizeNetChargeOutcomeMapper.GetRetryAfter(result, ChargeOutcome.ProviderError).Should().Be(TimeSpan.FromMinutes(5));
        }

        [Fact]
        public void GetRetryAfter_HeldForReview_ChecksAgainLater()
        {
            var result = new AuthorizeNetTransactionResult { TransactionResponseCode = "4" };

            AuthorizeNetChargeOutcomeMapper.GetRetryAfter(result, ChargeOutcome.ProviderError).Should().Be(TimeSpan.FromHours(1));
        }

        [Fact]
        public void GetRetryAfter_MerchantFilterDecline_HasNoRetryTiming()
        {
            var result = new AuthorizeNetTransactionResult { TransactionResponseCode = "2" };
            result.TransactionErrors.Add(new AuthorizeNetTransactionMessage { Code = "65" });

            AuthorizeNetChargeOutcomeMapper.GetRetryAfter(result, ChargeOutcome.ProviderError).Should().BeNull();
            AuthorizeNetChargeOutcomeMapper.IsMerchantFilterDecline(result).Should().BeTrue();
        }
    }
}
