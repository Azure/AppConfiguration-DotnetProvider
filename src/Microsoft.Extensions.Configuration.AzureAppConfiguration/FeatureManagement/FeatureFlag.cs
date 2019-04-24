using Newtonsoft.Json;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.FeatureManagement
{
    [JsonObject(MemberSerialization.OptIn)]
    internal class FeatureFlag
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("enabled")]
        public bool Enabled { get; set; }

        [JsonProperty("conditions")]
        public FeatureConditions Conditions { get; set; }
    }
}
