using Microsoft.Azconfig.Client;
using System;

namespace Microsoft.Extensions.Configuration.Azconfig
{
    class AzconfigConfigurationSource : IConfigurationSource
    {
        private readonly bool _optional;
        private readonly Func<RemoteConfigurationOptions> _optionsProvider;

        public AzconfigConfigurationSource(Action<RemoteConfigurationOptions> optionsInitializer, bool optional = false)
        {
            _optionsProvider = () => {

                var options = new RemoteConfigurationOptions();

                options.Optional = optional;

                optionsInitializer(options);

                return options;
            };

            _optional = optional;
        }

        public AzconfigConfigurationSource(RemoteConfigurationOptions options, bool optional = false)
        {
            options.Optional = _optional = optional;

            _optionsProvider = () => options;
        }

        public IConfigurationProvider Build(IConfigurationBuilder builder)
        {
            IConfigurationProvider provider = null;

            try
            {
                RemoteConfigurationOptions options = _optionsProvider();

                AzconfigClient client = options.Client ?? new AzconfigClient(options.ConnectionString);

                provider = new AzconfigConfigurationProvider(client, options);
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
