// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Azure;
using Azure.Core;
using Azure.Core.Testing;
using Azure.Data.AppConfiguration;
using Microsoft.Extensions.Configuration.AzureAppConfiguration;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Tests.AzureAppConfiguration
{
    class TestHelpers
    {
        public static readonly Uri PrimaryConfigStoreEndpoint = new Uri("https://azure.azconfig.io");
        public static readonly Uri SecondaryConfigStoreEndpoint = new Uri("https://azure---wus.azconfig.io");

        static public ConfigurationClient CreateMockConfigurationClient(Uri endpoint, AzureAppConfigurationOptions options = null)
        {
            var mockTokenCredential = new Mock<TokenCredential>();
            mockTokenCredential.Setup(c => c.GetTokenAsync(It.IsAny<TokenRequestContext>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<AccessToken>(new AccessToken("", DateTimeOffset.Now.AddDays(2))));

            return new ConfigurationClient(endpoint, mockTokenCredential.Object, options.ClientOptions);
        }

        static public IConfigurationClientManager CreateMockedConfigurationClientManager(AzureAppConfigurationOptions options)
        {
            ConfigurationClient c1 = CreateMockConfigurationClient(PrimaryConfigStoreEndpoint, options);
            ConfigurationClient c2 = CreateMockConfigurationClient(SecondaryConfigStoreEndpoint, options);

            ConfigurationClientWrapper w1 = new ConfigurationClientWrapper(PrimaryConfigStoreEndpoint, c1);
            ConfigurationClientWrapper w2 = new ConfigurationClientWrapper(SecondaryConfigStoreEndpoint, c2);

            IList<ConfigurationClientWrapper> clients = new List<ConfigurationClientWrapper>() { w1, w2 };

            MockedConfigurationClientManager provider = new MockedConfigurationClientManager(clients);

            return provider;
        }

        static public MockedConfigurationClientManager CreateMockedConfigurationClientManager(ConfigurationClient primaryClient, ConfigurationClient secondaryClient = null)
        {
            ConfigurationClientWrapper w1 = new ConfigurationClientWrapper(PrimaryConfigStoreEndpoint, primaryClient);
            ConfigurationClientWrapper w2 = secondaryClient != null ? new ConfigurationClientWrapper(SecondaryConfigStoreEndpoint, secondaryClient) : null;

            IList<ConfigurationClientWrapper> clients = new List<ConfigurationClientWrapper>() { w1 };

            if (secondaryClient != null)
            {
                clients.Add(w2);
            }

            MockedConfigurationClientManager provider = new MockedConfigurationClientManager(clients);

            return provider;
        }

        static public string CreateMockEndpointString(string endpoint = "https://azure.azconfig.io")
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

            using (JsonDocument document = JsonDocument.Parse(File.ReadAllText(path)))
            {
                var valueArray = document.RootElement.EnumerateArray();

                foreach (var setting in valueArray)
                {
                    ConfigurationSetting kv = ConfigurationModelFactory
                        .ConfigurationSetting(
                            key: setting.GetProperty("key").ToString(),
                            value: setting.GetProperty("value").GetRawText(),
                            contentType: setting.GetProperty("contentType").ToString());
                    _kvCollection.Add(kv);
                }
            }

            return _kvCollection;
        }

        public static bool ValidateLog(Mock<ILogger> logger, string expectedMessage, LogLevel level)
        {
            Func<object, Type, bool> state = (v, t) => v.ToString().Contains(expectedMessage);

            logger.Verify(
                x => x.Log(
                    It.Is<LogLevel>(l => l == level),
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => state(v, t)),
                    It.IsAny<Exception>(),
                    It.Is<Func<It.IsAnyType, Exception, string>>((v, t) => true)));

            return true;
        }
    }

    class MockAsyncPageable : AsyncPageable<ConfigurationSetting>
    {
        private readonly List<ConfigurationSetting> _collection = new List<ConfigurationSetting>();
        private int _status;

        public MockAsyncPageable(List<ConfigurationSetting> collection)
        {
            foreach (ConfigurationSetting setting in collection)
            {
                var newSetting = new ConfigurationSetting(setting.Key, setting.Value, setting.Label, setting.ETag);

                newSetting.ContentType = setting.ContentType;

                _collection.Add(newSetting);
            }
            //_collection = collection;

            _status = 200;
        }

        public void UpdateFeatureFlags(List<ConfigurationSetting> newCollection)
        {
            if (_collection.All(setting => newCollection.Any(newSetting =>
                setting.Key == newSetting.Key &&
                setting.Value == newSetting.Value &&
                setting.Label == newSetting.Label &&
                setting.ETag == newSetting.ETag)))
            {
                _status = 304;
            }
            else
            {
                _status = 200;

                _collection.Clear();

                foreach (ConfigurationSetting setting in newCollection)
                {
                    var newSetting = new ConfigurationSetting(setting.Key, setting.Value, setting.Label, setting.ETag);

                    newSetting.ContentType = setting.ContentType;

                    _collection.Add(newSetting);
                }
            }
        }

#pragma warning disable 1998
        public async override IAsyncEnumerable<Page<ConfigurationSetting>> AsPages(string continuationToken = null, int? pageSizeHint = null)
#pragma warning restore 1998
        {
            yield return Page<ConfigurationSetting>.FromValues(_collection, null, new MockResponse(_status));
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
            yield return Page<ConfigurationSetting>.FromValues(_collection, null, new MockResponse(200));
        }
    }
}
