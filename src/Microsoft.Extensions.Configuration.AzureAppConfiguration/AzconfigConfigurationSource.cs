using Microsoft.Azure.AppConfiguration.Azconfig;
using System;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    class AzconfigConfigurationSource : IConfigurationSource
    {
        private readonly bool _optional;
        private readonly Func<AzconfigOptions> _optionsProvider;

        public AzconfigConfigurationSource(Action<AzconfigOptions> optionsInitializer, bool optional = false)
        {
            _optionsProvider = () => {

                var options = new AzconfigOptions();

                optionsInitializer(options);

                return options;
            };

            _optional = optional;
        }

        public AzconfigConfigurationSource(AzconfigOptions options, bool optional = false)
        {
            _optional = optional;

            _optionsProvider = () => options;
        }

        public IConfigurationProvider Build(IConfigurationBuilder builder)
        {
            IConfigurationProvider provider = null;

            try
            {
                AzconfigOptions options = _optionsProvider();

                AzconfigClient client = options.Client ?? new AzconfigClient(options.ConnectionString);

                provider = new AzconfigConfigurationProvider(client, options, _optional);
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
