// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using System;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    internal class AzureAppConfigurationSource : IConfigurationSource
    {
        private readonly bool _optional;
        private readonly Func<AzureAppConfigurationOptions> _optionsProvider;

        public AzureAppConfigurationSource(Action<AzureAppConfigurationOptions> optionsInitializer, bool optional = false)
        {
            _optionsProvider = () => {
                var options = new AzureAppConfigurationOptions();
                optionsInitializer(options);
                return options;
            };

            _optional = optional;
        }

        public IConfigurationProvider Build(IConfigurationBuilder builder)
        {
            IConfigurationProvider provider = null;

            try
            {
                AzureAppConfigurationOptions options = _optionsProvider();
                IConfigurationClientManager clientManager;

                if (options.ClientManager != null)
                {
                    clientManager = options.ClientManager;
                }
                else if (!string.IsNullOrWhiteSpace(options.ConnectionString))
                {
                    clientManager = new ConfigurationClientManager(options.ConnectionString, options.ClientOptions);
                }
                else if (options.Endpoints != null && options.Credential != null)
                {
                    clientManager = new ConfigurationClientManager(options.Endpoints, options.Credential, options.ClientOptions);
                }
                else
                {
                    throw new ArgumentException($"Please call {nameof(AzureAppConfigurationOptions)}.{nameof(AzureAppConfigurationOptions.Connect)} to specify how to connect to Azure App Configuration.");
                }

                provider = new AzureAppConfigurationProvider(clientManager, options, _optional);
            }
            catch (InvalidOperationException ex)
            {
                if (!_optional)
                {
                    throw new ArgumentException(ex.Message, ex);
                }
            }
            catch (FormatException fe)
            {
                if (!_optional)
                {
                    throw new ArgumentException(fe.Message, fe);
                }
            }

            return provider ?? new EmptyConfigurationProvider();
        }
    }
}
