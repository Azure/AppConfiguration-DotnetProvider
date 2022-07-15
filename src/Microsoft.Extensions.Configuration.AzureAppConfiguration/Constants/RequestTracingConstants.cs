// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    internal class RequestTracingConstants
    {
        public const string RequestTracingDisabledEnvironmentVariable = "AZURE_APP_CONFIGURATION_TRACING_DISABLED";
        public const string AzureFunctionEnvironmentVariable = "FUNCTIONS_EXTENSION_VERSION";
        public const string AzureWebAppEnvironmentVariable = "WEBSITE_SITE_NAME";
        public const string KubernetesEnvironmentVariable = "KUBERNETES_PORT";
        
        public const string AspNetCoreEnvironmentVariable = "ASPNETCORE_ENVIRONMENT";
        public const string DotNetCoreEnvironmentVariable = "DOTNET_ENVIRONMENT";
        public const string DevelopmentEnvironmentName = "Development";

        // Documentation : https://docs.microsoft.com/en-us/azure/service-fabric/service-fabric-environment-variables-reference
        public const string ServiceFabricEnvironmentVariable = "Fabric_NodeName";

        public const string IISExpressProcessName = "iisexpress";

        public const string RequestTypeKey = "RequestType";
        public const string HostTypeKey = "Host";
        public const string FilterTypeKey = "Filter";
        public const string EnvironmentKey = "Env";
        public const string DevEnvironmentValue = "Dev";
        public const string FeatureManagementSchemaVersionTag = "FeatureManagementSchemaVersion";
        public const string KeyVaultConfiguredTag = "UsesKeyVault";
        public const string KeyVaultRefreshConfiguredTag = "RefreshesKeyVault";
        public const string ReplicaCount = "ReplicaCount";

        public const string DiagnosticHeaderActivityName = "Azure.CustomDiagnosticHeaders";
        public const string CorrelationContextHeader = "Correlation-Context";
        public const string UserAgentHeader = "User-Agent";
    }
}
