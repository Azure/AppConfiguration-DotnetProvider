namespace Microsoft.Extensions.Configuration.AppConfig
{
    class AppConfigConfigurationSource : IConfigurationSource
    {
        public IAppConfigClient Client { get; set; }

        public RemoteConfigurationOptions Options { get; set; }

        public IConfigurationProvider Build(IConfigurationBuilder builder)
        {
            return new AppConfigConfigurationProvider(Client, Options);
        }
    }
}