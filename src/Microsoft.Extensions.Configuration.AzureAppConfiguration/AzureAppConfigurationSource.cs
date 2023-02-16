﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Azure.Data.AppConfiguration;
using Microsoft.Extensions.Hosting;
using System;
using System.Security;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    internal class AzureAppConfigurationSource : IConfigurationSource
    {
        private readonly bool _optional;
        private readonly Func<AzureAppConfigurationOptions> _optionsProvider;
        private readonly IConfigurationClientFactory _configurationClientFactory;
        private readonly string _environmentName;

        public AzureAppConfigurationSource(Action<AzureAppConfigurationOptions> optionsInitializer, string environmentName = null, bool optional = false, IConfigurationClientFactory configurationClientFactory = null)
        {
            _optionsProvider = () => {
                var options = new AzureAppConfigurationOptions();
                optionsInitializer(options);
                return options;
            };

            _environmentName = environmentName;
            _optional = optional;
            _configurationClientFactory = configurationClientFactory ?? new ConfigurationClientFactory();
        }

        public IConfigurationProvider Build(IConfigurationBuilder builder)
        {
            try
            {
                string envType = Environment.GetEnvironmentVariable(RequestTracingConstants.AspNetCoreEnvironmentVariable) ??
                                    Environment.GetEnvironmentVariable(RequestTracingConstants.DotNetCoreEnvironmentVariable);
                if (_environmentName != null && !envType.Equals(_environmentName, StringComparison.OrdinalIgnoreCase))
                {
                    return new EmptyConfigurationProvider();
                }
            }
            catch (SecurityException)
            {
                // Can't read environment variables - assume production environment
                if (!_environmentName.Equals(Environments.Production, StringComparison.OrdinalIgnoreCase))
                {
                    return new EmptyConfigurationProvider();
                }
            }

            IConfigurationProvider provider = null;

            try
            {
                AzureAppConfigurationOptions options = _optionsProvider();
                ConfigurationClient client;

                if (options.Client != null)
                {
                    client = options.Client;
                }
                else if (!string.IsNullOrWhiteSpace(options.ConnectionString))
                {
                    client = _configurationClientFactory.CreateConfigurationClient(options.ConnectionString, options.ClientOptions);
                }
                else if (options.Endpoint != null && options.Credential != null)
                {
                    client = _configurationClientFactory.CreateConfigurationClient(options.Endpoint, options.Credential, options.ClientOptions);
                }
                else
                {
                    throw new ArgumentException($"Please call {nameof(AzureAppConfigurationOptions)}.{nameof(AzureAppConfigurationOptions.Connect)} to specify how to connect to Azure App Configuration.");
                }

                provider = new AzureAppConfigurationProvider(client, options, _optional);
            }
            catch (InvalidOperationException e)
            {
                if (!_optional)
                {
                    throw new ArgumentException(e.Message, e);
                }
            }

            return provider ?? new EmptyConfigurationProvider();
        }
    }
}
