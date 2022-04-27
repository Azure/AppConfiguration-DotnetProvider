// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Azure;
using Azure.Core;
using Azure.Data.AppConfiguration;
using Microsoft.Extensions.Configuration.AzureAppConfiguration;
using Moq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Tests.AzureAppConfiguration
{
    class TestHelpers
    {
        public static readonly Uri PrimaryConfigStoreEndpoint = new Uri("https://xxxxx.azconfig.io");
        public static readonly Uri SecondaryConfigStoreEndpoint = new Uri("https://xxxxx---wus.azconfig.io");

        static public ConfigurationClient CreateMockConfigurationClient(Uri endpoint, AzureAppConfigurationOptions options = null)
        {
            var mockTokenCredential = new Mock<TokenCredential>();
            mockTokenCredential.Setup(c => c.GetTokenAsync(It.IsAny<TokenRequestContext>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<AccessToken>(new AccessToken("", DateTimeOffset.Now.AddDays(2))));

            return new ConfigurationClient(endpoint, mockTokenCredential.Object, options.ClientOptions);
        }

        static public IConfigurationClientProvider CreateMockedConfigurationClientProvider(AzureAppConfigurationOptions options)
        {
            ConfigurationClient c1 = CreateMockConfigurationClient(PrimaryConfigStoreEndpoint, options);
            ConfigurationClient c2 = CreateMockConfigurationClient(SecondaryConfigStoreEndpoint, options);

            ConfigurationClientWrapper w1 = new ConfigurationClientWrapper(PrimaryConfigStoreEndpoint, c1);
            ConfigurationClientWrapper w2 = new ConfigurationClientWrapper(SecondaryConfigStoreEndpoint, c2);

            IList<ConfigurationClientWrapper> clients = new List<ConfigurationClientWrapper>() { w1, w2 };

            MockedConfigurationClientProvider provider = new MockedConfigurationClientProvider(clients);

            return provider;
        }

        static public MockedConfigurationClientProvider CreateMockedConfigurationClientProvider(ConfigurationClient primaryClient, ConfigurationClient secondaryClient = null)
        {
            ConfigurationClientWrapper w1 = new ConfigurationClientWrapper(PrimaryConfigStoreEndpoint, primaryClient);
            ConfigurationClientWrapper w2 = secondaryClient != null ? new ConfigurationClientWrapper(SecondaryConfigStoreEndpoint, secondaryClient) : null;

            IList<ConfigurationClientWrapper> clients = new List<ConfigurationClientWrapper>() { w1 };

            if (secondaryClient != null)
            {
                clients.Add(w2);
            }

            MockedConfigurationClientProvider provider = new MockedConfigurationClientProvider(clients);

            return provider;
        }

        static public string CreateMockEndpointString(string endpoint = "https://xxxxx.azconfig.io")
        {
            byte[] toEncodeAsBytes = Encoding.ASCII.GetBytes("secret");
            string returnValue = Convert.ToBase64String(toEncodeAsBytes);
            return $"Endpoint={endpoint};Id=b1d9b31;Secret={returnValue}";
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

        public static List<ConfigurationSetting> LoadJsonSettingsFromFile(string path)
        {
            List<ConfigurationSetting> _kvCollection = new List<ConfigurationSetting>();
            var valueArray = JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(path)).EnumerateArray();
            foreach (var setting in valueArray)
            {
                ConfigurationSetting kv = ConfigurationModelFactory
                    .ConfigurationSetting(
                        key: setting.GetProperty("key").ToString(), 
                        value: setting.GetProperty("value").GetRawText(), 
                        contentType: setting.GetProperty("contentType").ToString());
                _kvCollection.Add(kv);
            }
            return _kvCollection;
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
