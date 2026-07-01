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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Globalization;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Tests.AzureAppConfiguration
{
    class TestHelpers
    {
        public static readonly Uri PrimaryConfigStoreEndpoint = new Uri("https://azure.azconfig.io");
        public static readonly Uri SecondaryConfigStoreEndpoint = new Uri("https://azure---wus.azconfig.io");

        // Associates a mocked ConfigurationClient with the FeatureFlagClient that should be paired with it,
        // so that existing tests can keep calling SetupMockFeatureFlagEndpoint(mockClient) before building
        // the mocked client manager without having to thread the feature-flag client explicitly.
        private static readonly ConditionalWeakTable<ConfigurationClient, FeatureFlagClient> _featureFlagClients =
            new ConditionalWeakTable<ConfigurationClient, FeatureFlagClient>();

        static public ConfigurationClient CreateMockConfigurationClient(Uri endpoint, AzureAppConfigurationOptions options = null)
        {
            var mockTokenCredential = new Mock<TokenCredential>();
            mockTokenCredential.Setup(c => c.GetTokenAsync(It.IsAny<TokenRequestContext>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<AccessToken>(new AccessToken("", DateTimeOffset.Now.AddDays(2))));

            return new ConfigurationClient(endpoint, mockTokenCredential.Object, options.ClientOptions);
        }

        static public IClientManager CreateMockedConfigurationClientManager(AzureAppConfigurationOptions options)
        {
            ConfigurationClient c1 = CreateMockConfigurationClient(PrimaryConfigStoreEndpoint, options);
            ConfigurationClient c2 = CreateMockConfigurationClient(SecondaryConfigStoreEndpoint, options);

            ClientWrapper w1 = new ClientWrapper(PrimaryConfigStoreEndpoint, c1, CreateMockFeatureFlagClient(PrimaryConfigStoreEndpoint, options));
            ClientWrapper w2 = new ClientWrapper(SecondaryConfigStoreEndpoint, c2, CreateMockFeatureFlagClient(SecondaryConfigStoreEndpoint, options));

            IList<ClientWrapper> clients = new List<ClientWrapper>() { w1, w2 };

            MockedConfigurationClientManager provider = new MockedConfigurationClientManager(clients);

            return provider;
        }

        static public MockedConfigurationClientManager CreateMockedConfigurationClientManager(ConfigurationClient primaryClient, ConfigurationClient secondaryClient = null)
        {
            ClientWrapper w1 = new ClientWrapper(PrimaryConfigStoreEndpoint, primaryClient, GetAssociatedFeatureFlagClient(primaryClient));
            ClientWrapper w2 = secondaryClient != null ? new ClientWrapper(SecondaryConfigStoreEndpoint, secondaryClient, GetAssociatedFeatureFlagClient(secondaryClient)) : null;

            IList<ClientWrapper> clients = new List<ClientWrapper>() { w1 };

            if (secondaryClient != null)
            {
                clients.Add(w2);
            }

            MockedConfigurationClientManager provider = new MockedConfigurationClientManager(clients);

            return provider;
        }

        static private FeatureFlagClient CreateMockFeatureFlagClient(Uri endpoint, AzureAppConfigurationOptions options)
        {
            var mockTokenCredential = new Mock<TokenCredential>();
            mockTokenCredential.Setup(c => c.GetTokenAsync(It.IsAny<TokenRequestContext>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<AccessToken>(new AccessToken("", DateTimeOffset.Now.AddDays(2))));

            return new FeatureFlagClient(endpoint, mockTokenCredential.Object, options.GetFeatureFlagClientOptions());
        }

        static private FeatureFlagClient GetAssociatedFeatureFlagClient(ConfigurationClient client)
        {
            if (client != null && _featureFlagClients.TryGetValue(client, out FeatureFlagClient featureFlagClient))
            {
                return featureFlagClient;
            }

            return null;
        }

        static public string CreateMockEndpointString(string endpoint = "https://azure.azconfig.io")
        {
            byte[] toEncodeAsBytes = Encoding.ASCII.GetBytes("secret");
            string returnValue = Convert.ToBase64String(toEncodeAsBytes);
            return $"Endpoint={endpoint};Id=b1d9b31;Secret={returnValue}";
        }

        /// <summary>
        /// Sets up the standalone feature-flag endpoint to return the supplied feature flags (empty by
        /// default) and associates the resulting <see cref="FeatureFlagClient"/> with the given
        /// <see cref="ConfigurationClient"/> mock so that the mocked client manager pairs them. Returns the
        /// shared pageable so that change detection across reloads is stable.
        /// </summary>
        static public MockFeatureFlagAsyncPageable SetupMockFeatureFlagEndpoint(Mock<ConfigurationClient> mockClient, List<FeatureFlag> flags = null)
        {
            var pageable = new MockFeatureFlagAsyncPageable(flags);

            var mockFeatureFlagClient = new Mock<FeatureFlagClient>(MockBehavior.Strict);
            mockFeatureFlagClient.Setup(c => c.GetFeatureFlagsAsync(It.IsAny<FeatureFlagSelector>(), It.IsAny<CancellationToken>()))
                .Returns(pageable);

            _featureFlagClients.Remove(mockClient.Object);
            _featureFlagClients.Add(mockClient.Object, mockFeatureFlagClient.Object);

            return pageable;
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

        public static ConfigurationSetting ChangeValue(ConfigurationSetting setting, string value)
        {
            return ConfigurationModelFactory.ConfigurationSetting(setting.Key, value, setting.Label, setting.ContentType, new ETag(Guid.NewGuid().ToString()), setting.LastModified);
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
        private List<List<ConfigurationSetting>> _pages;
        private List<MockResponse> _responses;
        private int _status = 200;
        private readonly int _itemsPerPage;
        private List<ConfigurationSetting> _collection = new List<ConfigurationSetting>();
        private readonly TimeSpan? _delay;

        public MockAsyncPageable(List<ConfigurationSetting> collection, TimeSpan? delay = null, int itemsPerPage = 100, List<MockResponse> responses = null)
        {
            _itemsPerPage = itemsPerPage;
            _delay = delay;

            foreach (ConfigurationSetting setting in collection)
            {
                var newSetting = new ConfigurationSetting(setting.Key, setting.Value, setting.Label, setting.ETag);

                newSetting.ContentType = setting.ContentType;

                _collection.Add(newSetting);
            }

            if (responses != null)
            {
                _responses = responses;
            }

            SlicePages();
        }

        private void SlicePages()
        {
            int pageCount = (_collection.Count + _itemsPerPage - 1) / _itemsPerPage;

            _pages = new List<List<ConfigurationSetting>>();
            for (int i = 0; i < pageCount; i++)
            {
                _pages.Add(_collection.Skip(i * _itemsPerPage).Take(_itemsPerPage).ToList());
            }
        }

        public void UpdateCollection(List<ConfigurationSetting> newCollection, List<MockResponse> responses = null)
        {
            bool isUnchanged = _collection.Count == newCollection.Count &&
                               _collection.All(setting => newCollection.Any(newSetting =>
                                   setting.Key == newSetting.Key &&
                                   setting.Value == newSetting.Value &&
                                   setting.Label == newSetting.Label &&
                                   setting.ETag == newSetting.ETag));

            if (isUnchanged)
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

                SlicePages();
            }
        }

        public override async IAsyncEnumerable<Page<ConfigurationSetting>> AsPages(string continuationToken = null, int? pageSizeHint = null)
        {
            if (_delay.HasValue)
            {
                await Task.Delay(_delay.Value);
            }

            int pageIndex = 0;

            while (pageIndex < _pages.Count)
            {
                List<ConfigurationSetting> pageItems = _pages[pageIndex];

                MockResponse response;

                if (_responses == null)
                {
                    response = new MockResponse(_status);
                }
                else
                {
                    response = _responses[pageIndex];
                }

                yield return Page<ConfigurationSetting>.FromValues(pageItems, null, response);
                pageIndex++;
            }
        }
    }

    /// <summary>
    /// A mock <see cref="AsyncPageable{T}"/> for the standalone feature-flag endpoint
    /// (<see cref="ConfigurationClient.GetFeatureFlagsAsync(FeatureFlagSelector, CancellationToken)"/>).
    /// Yields a single page containing the supplied feature flags (empty by default). The page uses a
    /// stable ETag so that change detection across reloads reports "no change" unless the collection is
    /// explicitly updated via <see cref="UpdateCollection"/>.
    /// </summary>
    class MockFeatureFlagAsyncPageable : AsyncPageable<FeatureFlag>
    {
        private List<FeatureFlag> _collection;
        private string _etag;
        private readonly TimeSpan? _delay;

        public MockFeatureFlagAsyncPageable(List<FeatureFlag> collection = null, TimeSpan? delay = null)
        {
            _collection = collection ?? new List<FeatureFlag>();
            _delay = delay;
            _etag = ComputeETag(_collection);
        }

        public void UpdateCollection(List<FeatureFlag> newCollection)
        {
            _collection = newCollection ?? new List<FeatureFlag>();
            _etag = ComputeETag(_collection);
        }

        private static string ComputeETag(List<FeatureFlag> collection)
        {
            // Derive a deterministic ETag from the flag names + enabled state so that an unchanged
            // collection keeps the same ETag and a changed collection produces a different one.
            string content = string.Join("|", collection.Select(f => $"{f.Name}:{f.Enabled}"));

            return "ff-" + content.GetHashCode().ToString("x8");
        }

        public override async IAsyncEnumerable<Page<FeatureFlag>> AsPages(string continuationToken = null, int? pageSizeHint = null)
        {
            if (_delay.HasValue)
            {
                await Task.Delay(_delay.Value);
            }

            yield return Page<FeatureFlag>.FromValues(_collection, null, new MockResponse(200, _etag));
        }
    }

    internal class MockConfigurationSettingPageIterator : IConfigurationSettingPageIterator
    {
        public IAsyncEnumerable<Page<ConfigurationSetting>> IteratePages(AsyncPageable<ConfigurationSetting> pageable, IEnumerable<MatchConditions> matchConditions)
        {
            return pageable.AsPages();
        }

        public IAsyncEnumerable<Page<ConfigurationSetting>> IteratePages(AsyncPageable<ConfigurationSetting> pageable)
        {
            return pageable.AsPages();
        }
    }

    internal class MockFeatureFlagPageIterator : IFeatureFlagPageIterator
    {
        public IAsyncEnumerable<Page<FeatureFlag>> IteratePages(AsyncPageable<FeatureFlag> pageable)
        {
            return pageable.AsPages();
        }

        public IAsyncEnumerable<Page<FeatureFlag>> IteratePages(AsyncPageable<FeatureFlag> pageable, IEnumerable<MatchConditions> matchConditions)
        {
            return pageable.AsPages();
        }
    }
}
