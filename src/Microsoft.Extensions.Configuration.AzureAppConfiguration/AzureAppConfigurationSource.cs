﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Azure.Core;
using Azure.Data.AppConfiguration;
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
                IConfigurationClientManager startupClientManager;

                if (options.ClientManager != null)
                {
                    clientManager = options.ClientManager;

                    startupClientManager = options.ClientManager;
                }
                else if (options.ConnectionStrings != null)
                {
                    clientManager = new ConfigurationClientManager(options.ConnectionStrings, options.ClientOptions);

                    var startupOptions = GetStartupClientOptions(options.Startup.Timeout);
                    startupClientManager = new ConfigurationClientManager(options.ConnectionStrings, startupOptions);
                }
                else if (options.Endpoints != null && options.Credential != null)
                {
                    clientManager = new ConfigurationClientManager(options.Endpoints, options.Credential, options.ClientOptions);

                    var startupOptions = GetStartupClientOptions(options.Startup.Timeout);
                    startupClientManager = new ConfigurationClientManager(options.Endpoints, options.Credential, startupOptions);
                }
                else
                {
                    throw new ArgumentException($"Please call {nameof(AzureAppConfigurationOptions)}.{nameof(AzureAppConfigurationOptions.Connect)} to specify how to connect to Azure App Configuration.");
                }

                provider = new AzureAppConfigurationProvider(clientManager, startupClientManager, options, _optional);
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

        private static ConfigurationClientOptions GetStartupClientOptions(TimeSpan timeout)
        {
            var clientOptions = new ConfigurationClientOptions(ConfigurationClientOptions.ServiceVersion.V1_0);
            clientOptions.Retry.MaxRetries = MaxRetries;
            clientOptions.Retry.MaxDelay = MaxRetryDelay;
            clientOptions.Retry.Mode = RetryMode.Exponential;
            clientOptions.AddPolicy(new UserAgentHeaderPolicy(), HttpPipelinePosition.PerCall);

            return clientOptions;
        }
    }
}
