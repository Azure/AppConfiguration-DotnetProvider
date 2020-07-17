// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Azure;
using Azure.Data.AppConfiguration;
using Moq;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace Tests.AzureAppConfiguration
{
    class TestHelpers
    {
        static public string CreateMockEndpointString()
        {
            byte[] toEncodeAsBytes = Encoding.ASCII.GetBytes("secret");
            string returnValue = Convert.ToBase64String(toEncodeAsBytes);
            return $"Endpoint=https://xxxxx;Id=b1d9b31;Secret={returnValue}";
        }

        static public void SerializeSetting(ref Utf8JsonWriter json, ConfigurationSetting setting)
        {
            json.WriteStartObject();
            json.WriteString("key", setting.Key);
            json.WriteString("label", setting.Label);
            json.WriteString("value", setting.Value);
            json.WriteString("content_type", setting.ContentType);
            if (setting.Tags != null)
            {
                json.WriteStartObject("tags");
                foreach (KeyValuePair<string, string> tag in setting.Tags)
                {
                    json.WriteString(tag.Key, tag.Value);
                }
                json.WriteEndObject();
            }
            if (setting.ETag != default)
                json.WriteString("etag", setting.ETag.ToString());
            if (setting.LastModified.HasValue)
                json.WriteString("last_modified", setting.LastModified.Value.ToString());
            if (setting.IsReadOnly.HasValue)
                json.WriteBoolean("locked", setting.IsReadOnly.Value);
            json.WriteEndObject();
        }

        static public void SerializeBatch(ref Utf8JsonWriter json, ConfigurationSetting[] settings)
        {
            json.WriteStartObject();
            json.WriteStartArray("items");
            foreach (ConfigurationSetting item in settings)
            {
                SerializeSetting(ref json, item);
            }
            json.WriteEndArray();
            json.WriteEndObject();
        }

        public static ConfigurationSetting CloneSetting(ConfigurationSetting setting)
        {
            return ConfigurationModelFactory.ConfigurationSetting(setting.Key, setting.Value, setting.Label, setting.ContentType, setting.ETag, setting.LastModified);
        }
    }

    class MockAsyncPageable : AsyncPageable<ConfigurationSetting>
    {
        private readonly List<ConfigurationSetting> _collection;

        public MockAsyncPageable(List<ConfigurationSetting> collection)
        {
            _collection = collection;
        }

#pragma warning disable 1998
        public async override IAsyncEnumerable<Page<ConfigurationSetting>> AsPages(string continuationToken = null, int? pageSizeHint = null)
#pragma warning restore 1998
        {
            yield return Page<ConfigurationSetting>.FromValues(_collection, null, new Mock<Response>().Object);

        }
    }

    class MockPageable : Pageable<ConfigurationSetting>
    {
        private readonly List<ConfigurationSetting> _collection;

        public MockPageable(List<ConfigurationSetting> collection)
        {
            _collection = collection;
        }

        public override IEnumerable<Page<ConfigurationSetting>> AsPages(string continuationToken = null, int? pageSizeHint = null)
        {
            yield return Page<ConfigurationSetting>.FromValues(_collection, null, new Mock<Response>().Object);
        }
    }
}
