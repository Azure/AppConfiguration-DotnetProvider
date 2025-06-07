// write one generic extension method that takes a KeyValueChange object and returns a string representation of the key and value
using Azure;
using Azure.Data.AppConfiguration;
using Microsoft.Extensions.Configuration.AzureAppConfiguration.Models;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.Extensions
{
    internal static class PageExtensions
    {
        public static string GetCdnToken(this Page<ConfigurationSetting> page)
        {
            using Response response = page.GetRawResponse();

            return response.Headers.ETag?.ToString();
        }
    }
}