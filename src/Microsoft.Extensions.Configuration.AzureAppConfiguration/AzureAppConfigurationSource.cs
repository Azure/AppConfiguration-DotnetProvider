// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Microsoft.Extensions.Configuration.AzureAppConfiguration.Constants;
using System;
using System.Security;

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
            try
            {
                if (Environment.GetEnvironmentVariable(ConditionalProviderConstants.DisableProviderEnvironmentVariable) == "true")
                {
                    return new EmptyConfigurationProvider();
                }
            }
            catch (SecurityException)
            {
                // ignore
            }


            IConfigurationProvider provider = null;

            try
            {
                AzureAppConfigurationOptions options = _optionsProvider();
                IConfigurationClientManager clientManager;

                if (options.ClientManager != null)
                {
                    clientManager = options.ClientManager;
                }
                else if (options.ConnectionStrings != null)
                {
                    clientManager = new ConfigurationClientManager(options.ConnectionStrings, options.ClientOptions);
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
            catch (InvalidOperationException ex) // InvalidOperationException is thrown when any problems are found while configuring AzureAppConfigurationOptions or when SDK fails to create a configurationClient.
            {
                if (!_optional)
                {
                    throw new ArgumentException(ex.Message, ex);
                }
            }
            catch (FormatException fe) // FormatException is thrown when the connection string is not a well formed connection string.
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
