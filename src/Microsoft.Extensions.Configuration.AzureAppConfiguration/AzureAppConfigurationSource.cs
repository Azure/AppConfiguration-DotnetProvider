// Copyright (c) Microsoft Corporation.
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
        private readonly string _environmentName;

        public AzureAppConfigurationSource(Action<AzureAppConfigurationOptions> optionsInitializer, string environmentName, bool optional)
        {
            _optionsProvider = () => {
                var options = new AzureAppConfigurationOptions();
                optionsInitializer(options);
                return options;
            };

            _environmentName = environmentName;
            _optional = optional;
        }

        public IConfigurationProvider Build(IConfigurationBuilder builder)
        {
            if (!string.IsNullOrEmpty(_environmentName))
            {
                try
                {
                    string envType = Environment.GetEnvironmentVariable(RequestTracingConstants.AspNetCoreEnvironmentVariable) ??
                                        Environment.GetEnvironmentVariable(RequestTracingConstants.DotNetCoreEnvironmentVariable);
                    if (!envType.Equals(_environmentName, StringComparison.OrdinalIgnoreCase))
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
