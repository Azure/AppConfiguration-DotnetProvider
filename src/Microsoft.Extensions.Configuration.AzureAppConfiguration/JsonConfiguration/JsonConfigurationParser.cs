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
        public static bool IsJsonContentType(string contentType)
        {
            try
            {
                if (string.IsNullOrEmpty(contentType))
                {
                    return false;
                }

                string acceptedMainType = "application";
                string acceptedSubType = "json";

                ContentType ct = new ContentType(contentType.Trim().ToLower());
                var type = ct.MediaType;
                if (string.Equals(type, FeatureManagementConstants.ContentType)
                    || string.Equals(type, KeyVaultConstants.ContentType))
                {
                    return false;
                }

                if (type.Contains('/'))
                {
                    string mainType = type.Split('/')[0];
                    string subType = type.Split('/')[1];
                    if (string.Equals(mainType, acceptedMainType))
                    {
                        if (subType.Contains('+'))
                        {
                            var subTypes = subType.Split('+');
                            if (Array.Exists(subTypes, x => x == acceptedSubType))
                            {
                                return true;
                            }
                        }
                        else if (string.Equals(subType, acceptedSubType))
                        {
                            return true;
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is ArgumentException || ex is FormatException)
            {
                // not a valid JSON content type
            }
            return false;
        }

        public static List<KeyValuePair<string, string>> ParseJsonSetting(ConfigurationSetting setting)
        {
            List<KeyValuePair<string, string>> keyValues = new List<KeyValuePair<string, string>>();
            SortedDictionary<string, string> keyValueDict = new SortedDictionary<string, string>();
            ParseSetting(setting.Key, setting.Value, keyValueDict);
            foreach (KeyValuePair<string, string> entry in keyValueDict)
            {
                keyValues.Add(entry);
            }
            return keyValues;
        }


        public static void ParseSetting(string currentKey, string currentValue, SortedDictionary<string, string> keyValueDict)
        {
            try
            {
                var json_data = JsonSerializer.Deserialize<JsonElement>(currentValue);
                switch (json_data.ValueKind)
                {
                    case JsonValueKind.Array:
                        var valueArray = json_data.EnumerateArray();
                        for (int index = 0; index < valueArray.Count(); index++)
                        {
                            var newKey = ConfigurationPath.Combine(new List<string> { currentKey, index.ToString() });
                            ParseSetting(newKey, valueArray.ElementAt(index).GetRawText(), keyValueDict);
                        }
                        break;

                    case JsonValueKind.Object:
                        var valueObject = json_data.EnumerateObject();
                        foreach(JsonProperty entry in valueObject)
                        {
                            var newKey = ConfigurationPath.Combine(new List<string> { currentKey, entry.Name.ToString() });
                            ParseSetting(newKey, entry.Value.GetRawText(), keyValueDict);
                        }
                        break;

                    default:
                        keyValueDict[currentKey] = json_data.ToString();
                        break;
                }

            }
            catch (Exception ex) when (ex is ArgumentNullException || ex is JsonException || ex is NotSupportedException)
            {
                // If it's not a valid JSON, treat it like regular string value
                keyValueDict[currentKey] = currentValue;
            }
        }
    }
}
