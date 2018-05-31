# Azure Config Store - .NET Core

This package contains a .NET Core configuration provider for the Azure Config Store. The API design follows the patterns outlined by the [ASP.NET configuration](https://github.com/aspnet/configuration/) system to make switching to the Azure Config Store a familiar experience.

Use this SDK to:

* Add Azure Config Store data to the .NET Core configuration system
* Listen for configuration changes
* Format configuration values based off of content type

## Examples

Examples can be found [here](./examples).

```c#
var builder = new ConfigurationBuilder();

builder.AddRemoteAppConfiguration("https://<Azure Config Store URL>", new RemoteConfigurationOptions()
{
    Prefix = "App1/",
    AcceptVersion = "2.0"
}
.Listen("AppName", 30 * 60 * 1000));

IConfiguration configuration = builder.Build();
```

## Notable API

### AzconfigConfigurationExtensions

```csharp
static IConfigurationBuilder AddRemoteAppConfiguration(this IConfigurationBuilder configurationBuilder, string azconfigUri);

static IConfigurationBuilder AddRemoteAppConfiguration(this IConfigurationBuilder configurationBuilder, string azconfigUri, RemoteConfigurationOptions options);

static IConfigurationBuilder AddRemoteAppConfiguration(this IConfigurationBuilder configurationBuilder, string azconfigUri, RemoteConfigurationOptions options, IAzconfigClient client);
```
### RemoteConfigurationOptions

```csharp
string AcceptVersion { get; set; }

string Prefix { get; set; }

IKeyValueFormatter KeyValueFormatter { get; set; }

IEnumerable<KeyValueListener> ChangeListeners { get; }

RemoteConfigurationOptions Listen(string key, int pollInterval);
```

### IAzconfigClient

```csharp
Task<IEnumerable<IKeyValue>> GetSettings(string azconfigUri, string prefix)

Task<IKeyValue> GetSetting(string azconfigUri, string key)

Task<string> GetETag(string azconfigUri, string key)
```

# Contributing

This project welcomes contributions and suggestions.  Most contributions require you to agree to a
Contributor License Agreement (CLA) declaring that you have the right to, and actually do, grant us
the rights to use your contribution. For details, visit https://cla.microsoft.com.

When you submit a pull request, a CLA-bot will automatically determine whether you need to provide
a CLA and decorate the PR appropriately (e.g., label, comment). Simply follow the instructions
provided by the bot. You will only need to do this once across all repos using our CLA.

This project has adopted the [Microsoft Open Source Code of Conduct](https://opensource.microsoft.com/codeofconduct/).
For more information see the [Code of Conduct FAQ](https://opensource.microsoft.com/codeofconduct/faq/) or
contact [opencode@microsoft.com](mailto:opencode@microsoft.com) with any additional questions or comments.
