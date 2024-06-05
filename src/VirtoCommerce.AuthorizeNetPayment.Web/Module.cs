using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using VirtoCommerce.AuthorizeNetPayment.Core;
using VirtoCommerce.AuthorizeNetPayment.Core.Models;
using VirtoCommerce.AuthorizeNetPayment.Core.Services;
using VirtoCommerce.AuthorizeNetPayment.Data.Providers;
using VirtoCommerce.AuthorizeNetPayment.Data.Services;
using VirtoCommerce.PaymentModule.Core.Services;
using VirtoCommerce.Platform.Core.Modularity;
using VirtoCommerce.Platform.Core.Settings;

namespace VirtoCommerce.AuthorizeNetPayment.Web
{
    public class Module : IModule, IHasConfiguration
    {
        public ManifestModuleInfo ModuleInfo { get; set; }
        public IConfiguration Configuration { get; set; }

        public void Initialize(IServiceCollection serviceCollection)
        {
            serviceCollection.AddOptions<AuthorizeNetPaymentMethodOptions>().Bind(Configuration.GetSection("Payments:AuthorizeNet")).ValidateDataAnnotations();

            serviceCollection.AddTransient<IAuthorizeNetCheckoutService, AuthorizeNetCheckoutService>();
            serviceCollection.AddTransient<IAuthorizeNetClient, AuthorizeNetClient>();
            serviceCollection.AddTransient<AuthorizeNetPaymentMethod>();
        }

        public void PostInitialize(IApplicationBuilder appBuilder)
        {
            // register settings
            var settingsRegistrar = appBuilder.ApplicationServices.GetRequiredService<ISettingsRegistrar>();
            settingsRegistrar.RegisterSettings(ModuleConstants.Settings.AllSettings, ModuleInfo.Id);
            var paymentMethodsRegistrar = appBuilder.ApplicationServices.GetRequiredService<IPaymentMethodsRegistrar>();
            paymentMethodsRegistrar.RegisterPaymentMethod(() => 
                appBuilder.ApplicationServices.GetService<AuthorizeNetPaymentMethod>());

            settingsRegistrar.RegisterSettingsForType(ModuleConstants.Settings.General.AllSettings, nameof(AuthorizeNetPaymentMethod));
        }

        public void Uninstall()
        {
            // do nothing in here
        }
    }
}
