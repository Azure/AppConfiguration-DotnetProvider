namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    internal class GetKeyValueChangeCollectionOptions
    {
        public string Prefix { get; set; }
        public string Label { get; set; }
        public bool RequestTracingEnabled { get; set; }
        public HostType HostType { get; set; }
    }
}
