using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using VirtoCommerce.AuthorizeNetPayment.Core;
using VirtoCommerce.AuthorizeNetPayment.Core.Models;
using VirtoCommerce.AuthorizeNetPayment.Data.Providers;
using VirtoCommerce.AuthorizeNetPayment.Data.Services;
using VirtoCommerce.Platform.Core.Settings;

namespace VirtoCommerce.AuthorizeNetPayment.Tests.Sandbox
{
    /// <summary>
    /// The Authorize.Net sandbox for tests: credentials from environment variables, a connector wired to it, and the
    /// Accept payment nonce the storefront's Accept.js would post for a card.
    /// </summary>
    public static class AuthorizeNetSandbox
    {
        public const string ApiLoginVariable = "AUTHORIZENET_API_LOGIN_ID";
        public const string TransactionKeyVariable = "AUTHORIZENET_TRANSACTION_KEY";

        private const string ApiUrl = "https://apitest.authorize.net/xml/v1/request.api";

        private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(60) };

        private static readonly JsonSerializerOptions _jsonOptions = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

        public static string ApiLogin => Environment.GetEnvironmentVariable(ApiLoginVariable);

        public static string TransactionKey => Environment.GetEnvironmentVariable(TransactionKeyVariable);

        public static bool IsConfigured => !string.IsNullOrEmpty(ApiLogin) && !string.IsNullOrEmpty(TransactionKey);

        public static string SkipReason => IsConfigured
            ? null
            : $"Set {ApiLoginVariable} and {TransactionKeyVariable} to the Authorize.Net sandbox credentials to run the sandbox tests.";

        /// <summary>
        /// A connector that talks to the sandbox.
        /// </summary>
        public static AuthorizeNetPaymentMethod CreateConnector(string paymentActionType = ModuleConstants.AuthCapture)
        {
            var connector = new AuthorizeNetPaymentMethod(
                new AuthorizeNetClient(new Mock<IHttpClientFactory>().Object),
                new Mock<VirtoCommerce.AuthorizeNetPayment.Core.Services.IAuthorizeNetCheckoutService>().Object,
                Options.Create(new AuthorizeNetPaymentMethodOptions { ApiLogin = ApiLogin, TxnKey = TransactionKey }),
                NullLogger<AuthorizeNetPaymentMethod>.Instance);

            connector.Settings =
            [
                new ObjectSettingEntry(ModuleConstants.Settings.General.Mode) { Value = ModuleConstants.Test },
                new ObjectSettingEntry(ModuleConstants.Settings.General.PaymentActionType) { Value = paymentActionType },
            ];

            return connector;
        }

        /// <summary>
        /// The parameters Accept.js would post for a card: an Accept payment nonce, made server-side with
        /// <c>securePaymentContainerRequest</c> (the API Accept.js uses).
        /// </summary>
        public static async Task<NameValueCollection> GetCardParametersAsync(string cardNumber, string expirationMmYyyy, string cardCode, CancellationToken cancellationToken)
        {
            var request = new Dictionary<string, object>
            {
                ["securePaymentContainerRequest"] = new Dictionary<string, object>
                {
                    ["merchantAuthentication"] = new { name = ApiLogin, transactionKey = TransactionKey },
                    ["data"] = new
                    {
                        type = "TOKEN",
                        id = Guid.NewGuid().ToString(),
                        token = new { cardNumber, expirationDate = expirationMmYyyy, cardCode },
                    },
                },
            };

            using var response = await _httpClient.PostAsJsonAsync(ApiUrl, request, _jsonOptions, cancellationToken);
            response.EnsureSuccessStatusCode();

            // The API answers with a UTF-8 byte order mark, which System.Text.Json doesn't skip in a string.
            var body = (await response.Content.ReadAsStringAsync(cancellationToken)).TrimStart('﻿');
            using var json = JsonDocument.Parse(body);
            var opaqueData = json.RootElement.GetProperty("opaqueData");

            return new NameValueCollection
            {
                [ModuleConstants.DataDescriptorParamName] = opaqueData.GetProperty("dataDescriptor").GetString(),
                [ModuleConstants.DataValueParamName] = opaqueData.GetProperty("dataValue").GetString(),
            };
        }

        public const string CardCodeFilterIssue =
            "Sandbox account setting: the card code (CVV) filter rejects result \"P\" (not processed), so every charge of a stored card "
            + "is declined with reason 65. In the sandbox Merchant Interface open Account > Settings > Security Settings > "
            + "Basic Fraud Settings > Card Code Verification and clear \"P\" (and \"S\"), then run again.";

        /// <summary>
        /// Whether the account declines a charge without a card code (reason 65, card code result "P"): probed once per run.
        /// </summary>
        public static readonly Lazy<bool> RejectsChargesWithoutCardCode = new(() => ProbeCardCodeFilterAsync().GetAwaiter().GetResult());

        private static async Task<bool> ProbeCardCodeFilterAsync()
        {
            var client = new AuthorizeNetClient(new Mock<IHttpClientFactory>().Object);
            var nonce = await GetCardParametersAsync("4111111111111111", "122030", null, CancellationToken.None);
            var result = await client.CreateTransactionAsync(new AuthorizeNetCreateTransactionRequest
            {
                IsLiveMode = false,
                ApiLogin = ApiLogin,
                TransactionKey = TransactionKey,
                PaymentActionType = ModuleConstants.AuthCapture,
                Amount = 1.07m,
                CurrencyCode = "USD",
                InvoiceNumber = $"PROBE{DateTime.UtcNow:yyMMddHHmmssfff}",
                DataDescriptor = nonce[ModuleConstants.DataDescriptorParamName],
                DataValue = nonce[ModuleConstants.DataValueParamName],
            });

            if (result.TransactionResponse == TransactionResponse.Approved)
            {
                await client.VoidTransactionAsync(new AuthorizeNetVoidTransactionRequest
                {
                    IsLiveMode = false,
                    ApiLogin = ApiLogin,
                    TransactionKey = TransactionKey,
                    TransactionId = result.TransactionId,
                });
            }

            return result.ReasonCode == "65" && result.CvvResultCode == "P";
        }

        /// <summary>The network transaction ID a merchant-initiated charge referenced, read from the transaction details.</summary>
        public static async Task<string> GetOriginalNetworkTransactionIdAsync(string transactionId)
        {
            var details = await new AuthorizeNetClient(new Mock<IHttpClientFactory>().Object).GetTransactionDetailsAsync(new AuthorizeNetTransactionRequest
            {
                IsLiveMode = false,
                ApiLogin = ApiLogin,
                TransactionKey = TransactionKey,
                TransactionId = transactionId,
            });

            return details.OriginalNetworkTransactionId;
        }
    }
}
