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
                FailOverClient client;

                if (options.Client != null)
                {
                    client = options.Client;
                }
                else if (!string.IsNullOrWhiteSpace(options.ConnectionString))
                {
                    client = new FailOverClient(options.ConnectionString, options);
                }
                else if (options.Endpoints != null && options.Credential != null)
                {
                    client = new FailOverClient(options.Endpoints, options.Credential, options);
                }
                else
                {
                    throw new ArgumentException($"Please call {nameof(AzureAppConfigurationOptions)}.{nameof(AzureAppConfigurationOptions.Connect)} to specify how to connect to Azure App Configuration.");
                }

                provider = new AzureAppConfigurationProvider(client, options, _optional);
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is FormatException)
            {
                if (!_optional)
                {
                    throw new ArgumentException(ex.Message, ex);
                }
            }

            return provider ?? new EmptyConfigurationProvider();
        }
    }
}
