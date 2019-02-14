namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    using System;

    public static class AzconfigConfigurationExtensions
    {
        public static IConfigurationBuilder AddAzconfig(
            this IConfigurationBuilder configurationBuilder,
            string connectionString,
            bool optional = false)
        {
            return configurationBuilder.AddAzconfig(new AzconfigOptions().Connect(connectionString), optional);
        }

        public static IConfigurationBuilder AddAzconfig(
            this IConfigurationBuilder configurationBuilder,
            Action<AzconfigOptions> action,
            bool optional = false)
        {
            AzconfigOptions options = new AzconfigOptions();

            return configurationBuilder.Add(new AzconfigConfigurationSource(action, optional));
        }

        public static IConfigurationBuilder AddAzconfig(
            this IConfigurationBuilder configurationBuilder,
            AzconfigOptions options,
            bool optional = false)
        {
            return configurationBuilder.Add(new AzconfigConfigurationSource(options, optional));
        }
    }
}
