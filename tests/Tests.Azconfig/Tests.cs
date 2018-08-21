namespace Tests.Azconfig
{
    using Microsoft.Azconfig.Client;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.Configuration.Azconfig;
    using System.Collections.Generic;
    using System.Threading;
    using Xunit;

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
                builder.AddRemoteAppConfiguration(new RemoteConfigurationOptions(), testClient);
                var config = builder.Build();
                Assert.True(config["TestKey1"] == "TestValue1");
            }
        }

        [Fact]
        public void TriggersChangeNotification()
        {
            using (var testClient = new AzconfigClient(_connectionString, new MockedGetKeyValueRequest(_kv, _kvCollectionPageOne)))
            {
                var builder = new ConfigurationBuilder();
                var remoteConfigOpt = new RemoteConfigurationOptions();
                remoteConfigOpt.Watch("TestKey1", 1000);
                builder.AddRemoteAppConfiguration(remoteConfigOpt, testClient);
                var config = builder.Build();
                Assert.True(config["TestKey1"] == "TestValue1");

                Thread.Sleep(4000);
                Assert.True(config["TestKey1"] == "newValue");
            }
        }
    }
}
