using Azure;
using System;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.Extensions
{
    internal static class ResponseExtensions
    {
        public static DateTimeOffset GetDate(this Response response)
        {
            return response.Headers.Date ?? DateTimeOffset.UtcNow;
        }
    }
}
