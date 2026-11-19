using System;
using System.Collections.Generic;
using VirtoCommerce.AuthorizeNetPayment.Core.Models;
using VirtoCommerce.PaymentModule.Core.Model.StoredCredentials;

namespace VirtoCommerce.AuthorizeNetPayment.Data.Services
{
    /// <summary>
    /// Maps Authorize.Net transaction results to the provider-independent <see cref="ChargeOutcome"/>.
    /// Reason codes: https://developer.authorize.net/api/reference/responseCodes.html
    /// </summary>
    public static class AuthorizeNetChargeOutcomeMapper
    {
        /// <summary>How soon a temporary processor error is worth retrying.</summary>
        public static readonly TimeSpan ProcessorErrorRetryAfter = TimeSpan.FromMinutes(5);

        /// <summary>How soon a transaction held by the merchant's fraud filters is worth checking again.</summary>
        public static readonly TimeSpan HeldForReviewRetryAfter = TimeSpan.FromHours(1);

        /// <summary>The card can't be charged again: it is invalid, expired, not accepted, or must be picked up.</summary>
        private static readonly HashSet<string> _hardDeclineCodes =
        [
            "4", "6", "7", "8", "17", "28", "37", "78", "315", "316", "317",
            // The stored customer or payment profile no longer exists.
            "E00040",
        ];

        /// <summary>
        /// Rejected by the merchant's own AVS, card code or fraud filter settings. The card itself is fine, so it must not be
        /// marked for update; for a stored card (no card code) these usually mean the filters need to allow "not processed".
        /// </summary>
        private static readonly HashSet<string> _merchantFilterCodes =
        [
            "27", "44", "45", "65", "127", "165", "250", "251", "252", "253", "254",
        ];

        /// <summary>Temporary processor or network errors that are worth retrying soon.</summary>
        private static readonly HashSet<string> _retryableErrorCodes =
        [
            "19", "20", "21", "22", "23", "25", "26", "57", "58", "59", "60", "61", "62", "63", "120", "121", "122",
        ];

        public static ChargeOutcome GetOutcome(AuthorizeNetTransactionResult result)
        {
            ArgumentNullException.ThrowIfNull(result);

            var reasonCode = result.ReasonCode;

            if (result.TransactionResponse == TransactionResponse.Approved)
            {
                return ChargeOutcome.Succeeded;
            }

            if (reasonCode != null && _hardDeclineCodes.Contains(reasonCode))
            {
                return ChargeOutcome.HardDecline;
            }

            if (reasonCode != null && (_merchantFilterCodes.Contains(reasonCode) || _retryableErrorCodes.Contains(reasonCode)))
            {
                return ChargeOutcome.ProviderError;
            }

            return result.TransactionResponse switch
            {
                TransactionResponse.Declined => ChargeOutcome.SoftDecline,
                _ => ChargeOutcome.ProviderError,
            };
        }

        /// <summary>
        /// Maps a transaction found through <c>getTransactionDetails</c> (for example the original of a duplicate)
        /// by its status, since a duplicate response carries no outcome of its own.
        /// </summary>
        public static ChargeOutcome GetOutcomeByStatus(AuthorizeNetTransactionResult details)
        {
            ArgumentNullException.ThrowIfNull(details);

            return details.TransactionStatus switch
            {
                "authorizedPendingCapture" or "capturedPendingSettlement" or "settledSuccessfully" => ChargeOutcome.Succeeded,
                "declined" => GetOutcome(new AuthorizeNetTransactionResult
                {
                    TransactionResponseCode = "2",
                    TransactionErrors = [new AuthorizeNetTransactionMessage { Code = details.ResponseReasonCode }],
                }),
                _ => ChargeOutcome.ProviderError,
            };
        }

        public static TimeSpan? GetRetryAfter(AuthorizeNetTransactionResult result, ChargeOutcome outcome)
        {
            ArgumentNullException.ThrowIfNull(result);

            if (result.TransactionResponse == TransactionResponse.HeldForReview || result.TransactionStatus is "FDSPendingReview" or "FDSAuthorizedPendingReview")
            {
                return HeldForReviewRetryAfter;
            }

            var reasonCode = result.ReasonCode;
            if (outcome == ChargeOutcome.ProviderError && reasonCode != null && _retryableErrorCodes.Contains(reasonCode))
            {
                return ProcessorErrorRetryAfter;
            }

            return null;
        }

        /// <summary>True when the merchant's AVS, card code or fraud filters rejected the transaction.</summary>
        public static bool IsMerchantFilterDecline(AuthorizeNetTransactionResult result)
        {
            ArgumentNullException.ThrowIfNull(result);

            var reasonCode = result.ReasonCode;
            return reasonCode != null && _merchantFilterCodes.Contains(reasonCode);
        }
    }
}
