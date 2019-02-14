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
                ETag = "c3c231fd-39a0-4cb6-3237-4614474b92c1",
                ContentType = "text"
            },
            new KeyValue("TestKey2")
            {
                Label = "label",
                Value = "TestValue2",
                ETag = "c3c231fd-39a0-4cb6-3237-4614474b92c2",
                ContentType = "text"
            },
            new KeyValue("TestKey3")
            {
                Label = "label",
                Value = "TestValue3",
                ETag = "c3c231fd-39a0-4cb6-3237-4614474b92c3",
                ContentType = "text"
            },
            new KeyValue("TestKey4")
            {
                Label = "label",
                Value = "TestValue4",
                ETag = "c3c231fd-39a0-4cb6-3237-4614474b92c4",
                ContentType = "text"
            }
        };

        [Fact]
        public void AddsConfigurationValues()
        {
            using (var testClient = new AzconfigClient(_connectionString, new MockedGetKeyValueRequest(_kv, _kvCollectionPageOne)))
            {
                var builder = new ConfigurationBuilder();
                builder.AddAzconfig(new AzconfigOptions() {
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
            builder.AddAzconfig(invalidConnectionString, true);
            var config = builder.Build();
            Assert.True(config["TestKey1"] == null);
        }

        [Fact]
        public void AddsInvalidConfigurationStore()
        {
            string invalidConnectionString = "invalid-Connection-String";
            var builder = new ConfigurationBuilder();
            var exception = Record.Exception(() => {
                builder.AddAzconfig(invalidConnectionString, false);
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
                var remoteConfigOpt = new AzconfigOptions() {
                    Client = testClient
                };
                remoteConfigOpt.Watch("TestKey1", TimeSpan.FromMilliseconds(500));
                builder.AddAzconfig(remoteConfigOpt);
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

                builder.AddAzconfig(new AzconfigOptions()
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
                var remoteConfigOpt = new AzconfigOptions()
                {
                    Client = testClient
                };
                remoteConfigOpt.WatchAndReloadAll("TestKey1", TimeSpan.FromMilliseconds(500));
                builder.AddAzconfig(remoteConfigOpt);
                var config = builder.Build();
                Assert.True(config["TestKey1"] == "TestValue1");
                Thread.Sleep(1500);
                Assert.True(config["TestKey1"] == "newValue");
                Assert.True(config["TestKey2"] == "newValue");
                Assert.True(config["TestKey3"] == "newValue");
                Assert.True(config["TestKey4"] == "newValue");
            }
        }
    }
}
