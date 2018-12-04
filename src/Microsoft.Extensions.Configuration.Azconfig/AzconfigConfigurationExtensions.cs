namespace Microsoft.Extensions.Configuration.Azconfig
{
    using System;

    public static class AzconfigConfigurationExtensions
    {
        public static IConfigurationBuilder AddAzconfig(
            this IConfigurationBuilder configurationBuilder,
            string connectionString,
            bool optional = false)
        {
            return AddAzconfig(configurationBuilder,
                                      new RemoteConfigurationOptions().Connect(connectionString),
                                      optional);
        }

        public static IConfigurationBuilder AddAzconfig(
            this IConfigurationBuilder configurationBuilder,
            Action<RemoteConfigurationOptions> action,
            bool optional = false)
        {
            RemoteConfigurationOptions options = new RemoteConfigurationOptions();

            return configurationBuilder.Add(new AzconfigConfigurationSource(action, optional));
        }

        public static IConfigurationBuilder AddAzconfig(
            this IConfigurationBuilder configurationBuilder,
            RemoteConfigurationOptions options,
            bool optional = false)
        {
            return configurationBuilder.Add(new AzconfigConfigurationSource(options, optional));
        }
    }
}
