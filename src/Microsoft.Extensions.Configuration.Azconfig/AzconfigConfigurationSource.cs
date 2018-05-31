namespace Microsoft.Extensions.Configuration.Azconfig
{
    class AzconfigConfigurationSource : IConfigurationSource
    {
        public IAzconfigClient Client { get; set; }

        public RemoteConfigurationOptions Options { get; set; }

        public IConfigurationProvider Build(IConfigurationBuilder builder)
        {
            return new AzconfigConfigurationProvider(Client, Options);
        }
    }
}