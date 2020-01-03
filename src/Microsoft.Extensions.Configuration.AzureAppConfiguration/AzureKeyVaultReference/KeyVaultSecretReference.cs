using System.Text.Json.Serialization;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.AzureKeyVault
{
    internal class KeyVaultSecretReference
    {
        [JsonPropertyName("uri")]
        public string Uri { get; set; }
    }
}
