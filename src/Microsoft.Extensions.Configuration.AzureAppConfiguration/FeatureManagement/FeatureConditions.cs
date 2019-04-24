using Newtonsoft.Json;
using System.Collections.Generic;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.FeatureManagement
{
    [JsonObject(MemberSerialization.OptIn)]
    internal class FeatureConditions
    {
        [JsonProperty("client_filters")]
        public List<ClientFilter> ClientFilters { get; set; } = new List<ClientFilter>();
    }
}
