using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.FeatureManagement
{
    [JsonObject(MemberSerialization.OptIn)]
    internal class ClientFilter
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("parameters")]
        public JObject Parameters { get; set; }
    }
}
