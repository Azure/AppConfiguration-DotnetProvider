# Experimental Feature Diagnostics

This document lists the experimental feature diagnostic IDs used in the Azure App Configuration .NET Provider to mark APIs that are under development and subject to change.

## SCME0002 - Configuration-Based Client Construction

### Description

The `AddAppConfigurations` extension methods on `IConfigurationBuilder` are experimental APIs that allow constructing an Azure App Configuration client directly from an `IConfiguration` section. These methods use `ConfigurationClientSettings` from `Azure.Data.AppConfiguration` and `GetAzureClientSettings<T>` from `Azure.Identity` to read the endpoint and credential from configuration, eliminating the need for manual client construction.

These APIs depend on experimental features from the Azure SDK (`Azure.Data.AppConfiguration` and `Azure.Core`) that are also marked with `SCME0002`. They are subject to change or removal in future updates as the underlying SDK APIs stabilize.

### Affected APIs

- `AzureAppConfigurationExtensions.AddAppConfigurations(IConfigurationBuilder, string, bool)` — Adds App Configuration using a named configuration section.
- `AzureAppConfigurationExtensions.AddAppConfigurations(IConfigurationBuilder, string, Action<AzureAppConfigurationOptions>, bool)` — Adds App Configuration using a named configuration section with additional options configuration.

### Example Usage

```csharp
// appsettings.json
// {
//   "AppConfiguration": {
//     "Endpoint": "https://<your-store>.azconfig.io",
//     "Credential": {
//       "CredentialSource": "AzureCli"
//     }
//   }
// }

var builder = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json")
    .AddAppConfigurations("AppConfiguration");
```

For more information on the configuration schema, see the [Azure.Core Configuration and Dependency Injection](https://github.com/Azure/azure-sdk-for-net/blob/main/sdk/core/Azure.Core/src/docs/ConfigurationAndDependencyInjection.md) documentation.

For the upstream `SCME0002` diagnostic defined in Azure.Core, see the [Azure.Core Experimental Features](https://github.com/Azure/azure-sdk-for-net/blob/main/sdk/core/Azure.Core/src/docs/ExperimentalFeatures.md) documentation.

### Suppression

If you want to use these experimental APIs and accept the risk that they may change, you can suppress the warning:

```csharp
#pragma warning disable SCME0002 // Type is for evaluation purposes only and is subject to change or removal in future updates.
```

Or in your project file:

```xml
<PropertyGroup>
  <NoWarn>$(NoWarn);SCME0002</NoWarn>
</PropertyGroup>
```
