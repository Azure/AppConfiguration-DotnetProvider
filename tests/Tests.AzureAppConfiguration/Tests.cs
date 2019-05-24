namespace Tests.AzureAppConfiguration
{
    using Microsoft.Azure.AppConfiguration.Azconfig;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.Configuration.AzureAppConfiguration;
    using System;
    using System.Net.Http;
    using System.Collections.Generic;
    using System.Threading;
    using Xunit;
    using System.Text;
    using System.Net.Http.Headers;
    using System.Linq;

    public class Tests
    {
        string _connectionString = TestHelpers.CreateMockEndpointString();

        IKeyValue _kv = new KeyValue("TestKey1")
        {
            Label = "test",
            Value = "newTestValue1",
            ETag = "c3c231fd-39a0-4cb6-3237-4614474b92c6",
            ContentType = "text"
        };

        IEnumerable<IKeyValue> _kvCollectionPageOne = new List<IKeyValue>
        {
            new KeyValue("TestKey1")
            {
                Label = "label",
                Value = "TestValue1",
                ETag = "0a76e3d7-7ec1-4e37-883c-9ea6d0d89e63",
                ContentType = "text"
            },
            new KeyValue("TestKey2")
            {
                Label = "label",
                Value = "TestValue2",
                ETag = "31c38369-831f-4bf1-b9ad-79db56c8b989",
                ContentType = "text"
            },
            new KeyValue("TestKey3")
            {
                Label = "label",
                Value = "TestValue3",
                ETag = "bb203f2b-c113-44fc-995d-b933c2143339",
                ContentType = "text"
            },
            new KeyValue("TestKey4")
            {
                Label = "label",
                Value = "TestValue4",
                ETag = "3ca43b3e-d544-4b0c-b3a2-e7a7284217a2",
                ContentType = "text"
            },
            new KeyValue("App2/TestKey1")
            {
                Label = "label",
                Value = "TestValue2.1",
                ETag = "88c8c740-f998-4c88-85cb-fe95e93e2263",
                ContentType = "text"
            }
        };

        [Fact]
        public void AddsConfigurationValues()
        {
            using (var testClient = new AzconfigClient(_connectionString, new MockedGetKeyValueRequest(_kv, _kvCollectionPageOne)))
            {
                var builder = new ConfigurationBuilder();
                builder.AddAzureAppConfiguration(new AzureAppConfigurationOptions() {
                    Client = testClient
                });
                var config = builder.Build();

                Assert.True(config["TestKey1"] == "TestValue1");

                //
                // Case-insensitive
                Assert.True(config["tEsTkEy1"] == "TestValue1");
            }
        }

        [Fact]
        public void AddsInvalidOptionalConfigurationStore()
        {
            string invalidConnectionString = "invalid-Connection-String";
            var builder = new ConfigurationBuilder();
            builder.AddAzureAppConfiguration(invalidConnectionString, true);
            var config = builder.Build();
            Assert.True(config["TestKey1"] == null);
        }

        [Fact]
        public void AddsInvalidConfigurationStore()
        {
            string invalidConnectionString = "invalid-Connection-String";
            var builder = new ConfigurationBuilder();
            var exception = Record.Exception(() => {
                builder.AddAzureAppConfiguration(invalidConnectionString, false);
                builder.Build();
            });
            Assert.NotNull(exception);
            Assert.IsType<ArgumentException>(exception);
        }

        [Fact]
        public void TriggersChangeNotification()
        {
            using (var testClient = new AzconfigClient(_connectionString, new MockedGetKeyValueRequest(_kv, _kvCollectionPageOne)))
            {
                var builder = new ConfigurationBuilder();
                var remoteConfigOpt = new AzureAppConfigurationOptions() {
                    Client = testClient
                };
                remoteConfigOpt.Watch("TestKey1", TimeSpan.FromMilliseconds(500));
                builder.AddAzureAppConfiguration(remoteConfigOpt);
                var config = builder.Build();
                Assert.True(config["TestKey1"] == "TestValue1");
                Thread.Sleep(1500);
                Assert.True(config["TestKey1"] == "newValue");
            }
        }

        [Fact]
        public void UsesPreferredDateTime()
        {
            bool kvsRetrieved = false;

            var messageHandler = new CallbackMessageHandler(r => {

                Assert.True(r.Headers.TryGetValues("Accept-Datetime", out var values));

                kvsRetrieved = true;

                var response = new HttpResponseMessage();

                response.Content = new StringContent("{}", Encoding.UTF8, "application/json");

                return response;
            });

            using (var testClient = new AzconfigClient(_connectionString, messageHandler))
            {
                var builder = new ConfigurationBuilder();

                builder.AddAzureAppConfiguration(new AzureAppConfigurationOptions()
                {
                    Client = testClient

                }.Use("*", null, DateTimeOffset.UtcNow));

                var config = builder.Build();

                Assert.True(kvsRetrieved);
            }
        }

        [Fact]
        public void WatchAndReloadAll()
        {
            using (var testClient = new AzconfigClient(_connectionString, new MockedGetKeyValueRequest(_kv, _kvCollectionPageOne)))
            {
                var builder = new ConfigurationBuilder();
                var remoteConfigOpt = new AzureAppConfigurationOptions()
                {
                    Client = testClient
                };
                remoteConfigOpt.WatchAndReloadAll("TestKey1", TimeSpan.FromMilliseconds(500));
                builder.AddAzureAppConfiguration(remoteConfigOpt);
                var config = builder.Build();
                Assert.True(config["TestKey1"] == "TestValue1");
                Thread.Sleep(1500);
                Assert.True(config["TestKey1"] == "newValue");
                Assert.True(config["TestKey2"] == "newValue");
                Assert.True(config["TestKey3"] == "newValue");
                Assert.True(config["TestKey4"] == "newValue");
            }
        }

        [Fact]
        public void TrimKeyPrefix()
        {
            using (var testClient = new AzconfigClient(_connectionString, new MockedGetKeyValueRequest(_kv, _kvCollectionPageOne)))
            {
                var builder = new ConfigurationBuilder();

                // Trim following prefixes from all keys in the configuration.
                var keyPrefix1 = "T";
                var keyPrefix2 = "App2/";
                var keyPrefix3 = "Test";

                builder.AddAzureAppConfiguration(new AzureAppConfigurationOptions()
                {
                    Client = testClient
                }.TrimKeyPrefix(keyPrefix1).TrimKeyPrefix(keyPrefix2).TrimKeyPrefix(keyPrefix3));

                var config = builder.Build();
                Assert.True(config["Key1"] == "TestValue1");
                Assert.True(config["Key2"] == "TestValue2");
                Assert.True(config["Key3"] == "TestValue3");
                Assert.True(config["Key4"] == "TestValue4");
                Assert.True(config["TestKey1"] == "TestValue2.1");

                builder.AddAzureAppConfiguration(new AzureAppConfigurationOptions()
                {
                    Client = testClient
                }.TrimKeyPrefix(keyPrefix3).TrimKeyPrefix(keyPrefix2).TrimKeyPrefix(keyPrefix1));

                var config2 = builder.Build();
                Assert.True(config2["Key1"] == "TestValue1");
                Assert.True(config2["Key2"] == "TestValue2");
                Assert.True(config2["Key3"] == "TestValue3");
                Assert.True(config2["Key4"] == "TestValue4");
                Assert.True(config2["TestKey1"] == "TestValue2.1");
            }
        }
    }
}
