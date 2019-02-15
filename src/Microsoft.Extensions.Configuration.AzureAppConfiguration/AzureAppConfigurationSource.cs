﻿using Microsoft.Azure.AppConfiguration.Azconfig;
using System;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    class AzureAppConfigurationSource : IConfigurationSource
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

        public AzureAppConfigurationSource(AzureAppConfigurationOptions options, bool optional = false)
        {
            _optional = optional;

            _optionsProvider = () => options;
        }

        public IConfigurationProvider Build(IConfigurationBuilder builder)
        {
            IConfigurationProvider provider = null;

            try
            {
                AzureAppConfigurationOptions options = _optionsProvider();

                AzconfigClient client = options.Client ?? new AzconfigClient(options.ConnectionString);

                provider = new AzureAppConfigurationProvider(client, options, _optional);
            }
            catch (ArgumentException)
            {
                if (!_optional)
                {
                    throw;
                }
            }
            catch (FormatException)
            {
                if (!_optional)
                {
                    throw;
                }
            }

            return provider ?? new EmptyConfigurationProvider();
        }
    }
}
