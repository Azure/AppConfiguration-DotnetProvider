namespace Microsoft.Extensions.Configuration.Azconfig
{
    public class OfflineFileCacheOptions
    {
        // Target location for cache
        public string Path { get; set; }

        // Indicate if cache for the same scope
        public string ScopeToken { get; set; }
        
        // Keys for encryption
        public byte[] Key { get; set; }
        public byte[] IV { get; set; }
        public byte[] SignKey { get; set; }

        // Helper 
        public bool IsCryptoDataReady => ((Key != null) && (IV != null) && (SignKey != null));
    }
}
