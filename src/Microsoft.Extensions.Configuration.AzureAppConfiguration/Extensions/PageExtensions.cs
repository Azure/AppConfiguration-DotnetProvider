// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Azure;
using Azure.Data.AppConfiguration;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.Extensions
{
    internal static class PageExtensions
    {
        public static string GetAfdToken(this Page<ConfigurationSetting> page)
        {
            using Response response = page.GetRawResponse();

            return response.Headers.ETag?.ToString();
        }
    }
}
