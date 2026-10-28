using Azure;
using Azure.Core;
using System;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.Extensions
{
    internal static class ResponseExtensions
    {
        public static DateTimeOffset GetMsDate(this Response response)
        {
            if (response.Headers.TryGetValue(HttpHeader.Names.XMsDate, out string value))
            {
                if (DateTimeOffset.TryParse(value, out DateTimeOffset date))
                {
                    return date;
                }
            }

            return DateTimeOffset.UtcNow;
        }
    }
}
