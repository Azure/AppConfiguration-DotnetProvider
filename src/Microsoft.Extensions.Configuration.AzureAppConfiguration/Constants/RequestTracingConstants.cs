namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.Constants
{
    internal class RequestTracingConstants
    {
        public const string RequestTracingDisabledEnvironmentVariable = "AZURE_APP_CONFIGURATION_TRACING_DISABLED";

        public const string AzureFunctionEnvironmentVariable = "FUNCTIONS_EXTENSION_VERSION";

        public const string AzureWebAppEnvironmentVariable = "WEBSITE_NODE_DEFAULT_VERSION";

        public const string RequestTypeKey = "RequestType";

        public const string HostTypeKey = "Host";

        public const string DiagnosticHeaderActivityName = "Azure.CustomDiagnosticHeaders";

        public const string CorrelationContextHeader = "Correlation-Context";

        public const string UserAgentHeader = "User-Agent";
    }
}
