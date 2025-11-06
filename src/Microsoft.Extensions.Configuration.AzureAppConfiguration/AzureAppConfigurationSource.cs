// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Azure.Core;
using Azure.Data.AppConfiguration;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.Configuration.AzureAppConfiguration.Afd;
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

                IAzureClientFactory<ConfigurationClient> clientFactory = options.ClientFactory;

                if (options.IsAfdUsed)
                {
                    if (options.LoadBalancingEnabled)
                    {
                        throw new InvalidOperationException(ErrorMessages.AfdLoadBalancingUnsupported);
                    }

                    if (clientFactory != null)
                    {
                        throw new InvalidOperationException(ErrorMessages.AfdCustomClientFactoryUnsupported);
                    }

                    if (options.IndividualKvWatchers.Any())
                    {
                        throw new InvalidOperationException($"Registering individual keys for refresh via `{nameof(AzureAppConfigurationRefreshOptions)}.{nameof(AzureAppConfigurationRefreshOptions.Register)}` is not supported when connecting to Azure Front Door. Instead, to enable configuration refresh, use `{nameof(AzureAppConfigurationRefreshOptions)}.{nameof(AzureAppConfigurationRefreshOptions.RegisterAll)}`.");
                    }

                    options.ClientOptions.AddPolicy(new AfdPolicy(), HttpPipelinePosition.PerRetry);
                }

                if (options.ClientManager != null)
                {
                    return new AzureAppConfigurationProvider(options.ClientManager, options, _optional);
                }

                IEnumerable<Uri> endpoints;

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
                    throw new ArgumentException($"Please call {nameof(AzureAppConfigurationOptions)}.{nameof(AzureAppConfigurationOptions.Connect)} or {nameof(AzureAppConfigurationOptions)}.{nameof(AzureAppConfigurationOptions.ConnectAzureFrontDoor)} to specify how to connect to Azure App Configuration.");
                }

                if (options.IsAfdUsed)
                {
                    provider = new AzureAppConfigurationProvider(new AfdConfigurationClientManager(clientFactory, endpoints.First()), options, _optional);
                }
                else
                {
                    provider = new AzureAppConfigurationProvider(new ConfigurationClientManager(clientFactory, endpoints, options.ReplicaDiscoveryEnabled, options.LoadBalancingEnabled), options, _optional);
                }
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
