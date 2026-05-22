// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Azure.Data.AppConfiguration;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.Configuration.AzureAppConfiguration.FeatureManagement;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    internal class AzureAppConfigurationSource : IConfigurationSource
    {
        private readonly bool _optional;
        private readonly Func<AzureAppConfigurationOptions> _optionsProvider;

        public AzureAppConfigurationSource(Action<AzureAppConfigurationOptions> optionsInitializer, bool optional = false, int priorAppConfigSourceCount = 0)
        {
            _optionsProvider = () =>
            {
                var options = new AzureAppConfigurationOptions();
                optionsInitializer(options);

                // If the caller didn't explicitly set an offset, derive it from the position of this
                // source relative to other Azure App Configuration sources on the configuration
                // builder. This avoids index collisions when multiple Azure App Configuration
                // providers emit feature flags under the Microsoft schema.
                if (options.FeatureFlagIndexOffset == 0 && priorAppConfigSourceCount > 0)
                {
                    options.FeatureFlagIndexOffset = priorAppConfigSourceCount * FeatureManagementConstants.FeatureFlagIndexStride;
                }

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

                if (options.ClientManager != null)
                {
                    return new AzureAppConfigurationProvider(options.ClientManager, options, _optional);
                }

                IEnumerable<Uri> endpoints;
                IAzureClientFactory<ConfigurationClient> clientFactory = options.ClientFactory;

                if (options.ConnectionStrings != null)
                {
                    endpoints = options.ConnectionStrings.Select(cs => new Uri(ConnectionStringUtils.Parse(cs, ConnectionStringUtils.EndpointSection)));

                    clientFactory ??= new AzureAppConfigurationClientFactory(options.ConnectionStrings, options.ClientOptions);
                }
                else if (options.Endpoints != null && options.Credential != null)
                {
                    endpoints = options.Endpoints;

                    clientFactory ??= new AzureAppConfigurationClientFactory(options.Credential, options.ClientOptions);
                }
                else
                {
                    throw new ArgumentException($"Please call {nameof(AzureAppConfigurationOptions)}.{nameof(AzureAppConfigurationOptions.Connect)} to specify how to connect to Azure App Configuration.");
                }

                provider = new AzureAppConfigurationProvider(new ConfigurationClientManager(clientFactory, endpoints, options.ReplicaDiscoveryEnabled, options.LoadBalancingEnabled), options, _optional);
            }
            catch (InvalidOperationException ex) // InvalidOperationException is thrown when any problems are found while configuring AzureAppConfigurationOptions or when SDK fails to create a configurationClient.
            {
                throw new ArgumentException(ex.Message, ex);
            }
            catch (FormatException fe) // FormatException is thrown when the connection string is not a well formed connection string.
            {
                throw new ArgumentException(fe.Message, fe);
            }

            return provider ?? new EmptyConfigurationProvider();
        }
    }
}
