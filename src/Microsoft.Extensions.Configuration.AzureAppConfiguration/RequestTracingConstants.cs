namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    internal class RequestTracingConstants
    {
        public const string RequestTracingDisabledEnvironmentVariable = "AZURE_APP_CONFIGURATION_TRACING_DISABLED";

        public const string AzureFunctionEnvironmentVariable = "FUNCTIONS_EXTENSION_VERSION";

        public const string AzureWebAppEnvironmentVariable = "WEBSITE_NODE_DEFAULT_VERSION";

        public const string RequestTypeKey = "RequestType";

        public const string HostTypeKey = "HostType";
    }
}
