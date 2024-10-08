// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Azure.Data.AppConfiguration;
using Microsoft.Extensions.Azure;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    internal class AzureAppConfigurationSource : IConfigurationSource
    {
        private readonly bool _optional;
        private readonly Func<AzureAppConfigurationOptions> _optionsProvider;

        public AzureAppConfigurationSource(Action<AzureAppConfigurationOptions> optionsInitializer, bool optional = false)
        {
            _optionsProvider = () =>
            {
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

                if (options.ClientManager != null)
                {
                    return new AzureAppConfigurationProvider(options.ClientManager, options, _optional);
                }

                IEnumerable<Uri> endpoints;
                IAzureClientFactory<ConfigurationClient> clientFactory = options.ClientFactory;

                if (options.ConnectionStrings != null && options.ConnectionStrings.Any())
                {
                    endpoints = options.ConnectionStrings.Select(cs => new Uri(ConnectionStringUtils.Parse(cs, ConnectionStringUtils.EndpointSection)));

                    clientFactory ??= new AzureAppConfigurationClientFactory(options.ConnectionStrings.First(), options.ClientOptions);
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
