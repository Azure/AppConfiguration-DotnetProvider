using Microsoft.Azconfig.Client;
using Microsoft.Extensions.Configuration.Azconfig.Models;

namespace Microsoft.Extensions.Configuration.Azconfig
{
    class AzconfigConfigurationSource : IConfigurationSource
    {
        public AzconfigClient Client { get; set; }

        public RemoteConfigurationOptions Options { get; set; }

        public IConfigurationProvider Build(IConfigurationBuilder builder)
        {
            return new AzconfigConfigurationProvider(Client as IAzconfigReader, Client as IAzconfigWatcher, Options);
        }
    }
}