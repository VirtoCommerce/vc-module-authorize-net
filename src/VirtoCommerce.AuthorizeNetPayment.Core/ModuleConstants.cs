using System.Collections.Generic;
using VirtoCommerce.Platform.Core.Settings;

namespace VirtoCommerce.AuthorizeNetPayment.Core
{
    public static class ModuleConstants
    {
        public const string Test = "test";
        public const string Real = "real";

        public const string Sale = "Sale";
        public const string AuthCapture = "Authorization/Capture";

        public const string DataDescriptorParamName = "dataDescriptor";
        public const string DataValueParamName = "dataValue";

        public static class Settings
        {
            public static class General
            {
                public static readonly SettingDescriptor Mode = new SettingDescriptor
                {
                    Name = "VirtoCommerce.Payment.AuthorizeNetPayment.Mode",
                    GroupName = "Payment|Authorize.Net Accept",
                    ValueType = SettingValueType.ShortText,
                    AllowedValues = new[] { Test, Real },
                    DefaultValue = Test,
                };

                public static readonly SettingDescriptor PaymentActionType = new SettingDescriptor
                {
                    Name = "VirtoCommerce.Payment.AuthorizeNetPayment.PaymentActionType",
                    GroupName = "Payment|Authorize.Net Accept",
                    ValueType = SettingValueType.ShortText,
                    AllowedValues = new[] { Sale, AuthCapture, },
                    DefaultValue = Sale,
                };

                public static readonly SettingDescriptor ProcessPaymentAction = new SettingDescriptor
                {
                    Name = "VirtoCommerce.Payment.AuthorizeNetPayment.ProcessPaymentAction",
                    GroupName = "Payment|Authorize.Net Accept",
                    ValueType = SettingValueType.ShortText,
                    DefaultValue = "{storefrontURL}/cart/externalpaymentcallback"
                };

                public static readonly SettingDescriptor AcceptJSTestPath = new SettingDescriptor
                {
                    Name = "VirtoCommerce.Payment.AuthorizeNetPayment.AcceptJSTestPath",
                    GroupName = "Payment|Authorize.Net Accept.js",
                    ValueType = SettingValueType.ShortText,
                    DefaultValue = "https://jstest.authorize.net/v1/Accept.js"
                };

                public static readonly SettingDescriptor AcceptJSProdPath = new SettingDescriptor
                {
                    Name = "VirtoCommerce.Payment.AuthorizeNetPayment.AcceptJSProdPath",
                    GroupName = "Payment|Authorize.Net Accept.js",
                    ValueType = SettingValueType.ShortText,
                    DefaultValue = "https://js.authorize.net/v1/Accept.js"
                };

                public static IEnumerable<SettingDescriptor> AllSettings
                {
                    get
                    {
                        yield return Mode;
                        yield return PaymentActionType;
                        yield return ProcessPaymentAction;
                        yield return AcceptJSTestPath;
                        yield return AcceptJSProdPath;
                    }
                }
            }

            public static IEnumerable<SettingDescriptor> AllSettings
            {
                get
                {
                    return General.AllSettings;
                }
            }
        }
    }
}
