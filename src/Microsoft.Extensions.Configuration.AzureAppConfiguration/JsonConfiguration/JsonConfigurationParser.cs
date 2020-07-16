// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Azure.Data.AppConfiguration;
using Microsoft.Extensions.Configuration.AzureAppConfiguration.AzureKeyVault;
using Microsoft.Extensions.Configuration.AzureAppConfiguration.FeatureManagement;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mime;
using System.Text.Json;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.JsonConfiguration
{
    internal static class JsonConfigurationParser
    {
        private static IEnumerable<string> _excludedJsonContentTypes = new[] { FeatureManagementConstants.ContentType, KeyVaultConstants.ContentType };

        public static bool IsJsonContentType(string contentType)
        {
            if (string.IsNullOrWhiteSpace(contentType))
            {
                return false;
            }

            string acceptedMainType = "application";
            string acceptedSubType = "json";
            string mediaType;

            try
            {
                ContentType ct = new ContentType(contentType.Trim().ToLower());
                mediaType = ct.MediaType;
            }
            catch (FormatException)
            {
                return false;
            }
            catch (IndexOutOfRangeException)
            {
                // Bug in System.Net.Mime.ContentType throws this if contentType is "xyz/"
                return false;
            }

            if (!_excludedJsonContentTypes.Contains(mediaType))
            {
                // Since contentType has been validated using System.Net.Mime.ContentType,
                // mediaType will always have exactly 2 parts after splitting on '/'
                string[] types = mediaType.Split('/');
                if (types[0] == acceptedMainType)
                {
                    string[] subTypes = types[1].Split('+');
                    if (subTypes.Contains(acceptedSubType))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        public static IEnumerable<KeyValuePair<string, string>> Parse(ConfigurationSetting setting)
        {
            string rootJson = $"{{\"{setting.Key}\":{setting.Value}}}";
            JsonElement jsonData;
            try
            {
                jsonData = JsonSerializer.Deserialize<JsonElement>(rootJson);
            }
            catch (JsonException)
            {
                // If the value is not a valid JSON, treat it like regular string value
                return new List<KeyValuePair<string, string>> { new KeyValuePair<string, string>(setting.Key, setting.Value) };
            }

            return new JsonFlattener().FlattenJson(jsonData);
        }
    }
}
