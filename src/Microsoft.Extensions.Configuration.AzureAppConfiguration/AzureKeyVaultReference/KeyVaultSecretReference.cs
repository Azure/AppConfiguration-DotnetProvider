using Newtonsoft.Json;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.AzureKeyVault
{
    [JsonObject(MemberSerialization.OptIn)]
    internal class KeyVaultSecretReference
    {
        [JsonProperty("uri")]
        public string Uri { get; set; }
    }
}
