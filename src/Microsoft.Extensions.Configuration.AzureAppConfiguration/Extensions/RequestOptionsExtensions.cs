using Microsoft.Azure.AppConfiguration.Azconfig;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.Extensions
{
    internal static class RequestOptionsExtensions
    {
        public static void ConfigureRequestTracingOptions(this IRequestOptions options, bool requestTracingEnabled, bool isInitialLoadComplete, HostType hostType = HostType.None)
        {
            if (options != null && requestTracingEnabled)
            {
                options.AddRequestType(isInitialLoadComplete ? RequestType.Watch : RequestType.Startup);

                if (hostType != HostType.None)
                {
                    options.AddHostType(hostType);
                }
            }
        }
    }
}
