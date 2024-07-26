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
        public const string ContainerAppEnvironmentVariable = "CONTAINER_APP_NAME";
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
        public const string FeatureManagementVersionKey = "FMVer";
        public const string FeatureManagementAspNetCoreVersionKey = "FMANCVer";
        public const string DevEnvironmentValue = "Dev";
        public const string KeyVaultConfiguredTag = "UsesKeyVault";
        public const string KeyVaultRefreshConfiguredTag = "RefreshesKeyVault";
        public const string ReplicaCountKey = "ReplicaCount";
        public const string FeatureFlagUsesTelemetryTag = "Telemetry";
        public const string FeatureFlagUsesSeedTag = "Seed";
        public const string FeatureFlagMaxVariantsKey = "MaxVariants";
        public const string FeatureFlagUsesVariantConfigurationReferenceTag = "ConfigRef";
        public const string FeatureFlagFeaturesKey = "FFFeatures";
        public const string SignalRUsedTag = "UsesSignalR";
        public const string ReplicaDiscoveryEnabledTag = "UsesReplicaDiscovery";
        public const string IsFailoverRequestTag = "FailoverRequest";

        public const string DiagnosticHeaderActivityName = "Azure.CustomDiagnosticHeaders";
        public const string CorrelationContextHeader = "Correlation-Context";
        public const string UserAgentHeader = "User-Agent";

        public const string FeatureManagementAssemblyName = "Microsoft.FeatureManagement";
        public const string FeatureManagementAspNetCoreAssemblyName = "Microsoft.FeatureManagement.AspNetCore";
        public const string SignalRAssemblyName = "Microsoft.AspNetCore.SignalR";

        public const string Delimiter = "+";
    }
}
