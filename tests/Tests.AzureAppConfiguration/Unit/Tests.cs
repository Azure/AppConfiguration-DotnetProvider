// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Azure;
using Azure.Core.Testing;
using Azure.Data.AppConfiguration;
using Azure.Data.AppConfiguration.Tests;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.AzureAppConfiguration;
using Moq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Tests.AzureAppConfiguration
{
    public class Tests
    {
        ConfigurationSetting _kv = ConfigurationModelFactory.ConfigurationSetting("TestKey1", "newTestValue1", "test",
            eTag: new ETag("c3c231fd-39a0-4cb6-3237-4614474b92c6"),
            contentType: "text");

        List<ConfigurationSetting> _kvCollectionPageOne = new List<ConfigurationSetting>
        {
            ConfigurationModelFactory.ConfigurationSetting("TestKey1", "TestValue1", "label",
                eTag: new ETag("0a76e3d7-7ec1-4e37-883c-9ea6d0d89e63"),
                contentType:"text"),
            ConfigurationModelFactory.ConfigurationSetting("TestKey2", "TestValue2", "label",
                eTag: new ETag("31c38369-831f-4bf1-b9ad-79db56c8b989"),
                contentType: "text"),
            ConfigurationModelFactory.ConfigurationSetting("TestKey3", "TestValue3", "label",

                eTag: new ETag("bb203f2b-c113-44fc-995d-b933c2143339"),
                contentType: "text"),
            ConfigurationModelFactory.ConfigurationSetting("TestKey4", "TestValue4", "label",
                eTag: new ETag("3ca43b3e-d544-4b0c-b3a2-e7a7284217a2"),
                contentType: "text"),
            ConfigurationModelFactory.ConfigurationSetting("App2/TestKey1", "TestValue2.1", "label",
                eTag: new ETag("88c8c740-f998-4c88-85cb-fe95e93e2263"),
                contentType: "text")
        };

        [Fact]
        public void AddsConfigurationValues()
        {
            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict);

            mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Returns(new MockAsyncPageable(_kvCollectionPageOne));

            mockClient.Setup(c => c.GetConfigurationSettingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Response.FromValue(_kv, mockResponse.Object));

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options => options.ClientManager = TestHelpers.CreateMockedConfigurationClientManager(mockClient.Object))
                .Build();

            Assert.True(config["TestKey1"] == "TestValue1");
            Assert.True(config["tEsTkEy1"] == "TestValue1");    // Case-insensitive
        }

        [Fact]
        public void AddsInvalidOptionalConfigurationStore()
        {
            string invalidConnectionString = "invalid-Connection-String";
            var builder = new ConfigurationBuilder();
            var exception = Record.Exception(() =>
            {
                builder.AddAzureAppConfiguration(invalidConnectionString, true);
                builder.Build();
            });
            Assert.NotNull(exception);
            Assert.IsType<ArgumentException>(exception);
        }

        [Fact]
        public void AddsInvalidConfigurationStore()
        {
            string invalidConnectionString = "invalid-Connection-String";
            var builder = new ConfigurationBuilder();
            var exception = Record.Exception(() =>
            {
                builder.AddAzureAppConfiguration(invalidConnectionString, false);
                builder.Build();
            });
            Assert.NotNull(exception);
            Assert.IsType<ArgumentException>(exception);
        }

        [Fact]
        public void AddsInvalidConfigurationStore_MalformedSecret()
        {
            string invalidConnectionString = "Endpoint=https://fake-endpoint;Id=fake-id;Secret=non-base64-string";
            var builder = new ConfigurationBuilder();
            var exception = Record.Exception(() =>
            {
                builder.AddAzureAppConfiguration(invalidConnectionString, false);
                builder.Build();
            });
            Assert.NotNull(exception);
            Assert.IsType<ArgumentException>(exception);
            Assert.IsType<InvalidOperationException>(exception.InnerException); // thrown by SDK
        }

        [Fact]
        public void LoadConfigurationStore_OnFailure()
        {
            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict);

            mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Throws(new RequestFailedException("Mock request failure"));

            var exception = Record.Exception(() =>
            {
                var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options => options.ClientManager = TestHelpers.CreateMockedConfigurationClientManager(mockClient.Object), false)
                .Build();
            });
            Assert.NotNull(exception);
            Assert.IsType<RequestFailedException>(exception);
        }

        [Fact]
        public void LoadOptionalConfigurationStore_OnFailure()
        {
            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict);

            mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Throws(new RequestFailedException("Mock request failure"));

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options => options.ClientManager = TestHelpers.CreateMockedConfigurationClientManager(mockClient.Object), true)
                .Build();
            Assert.Null(config["TestKey1"]);
        }

        [Fact]
        public void TrimKeyPrefix_TestCase1()
        {
            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict);

            mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Returns(new MockAsyncPageable(_kvCollectionPageOne));

            mockClient.Setup(c => c.GetConfigurationSettingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Response.FromValue(_kv, mockResponse.Object));

            // Trim following prefixes from all keys in the configuration.
            var keyPrefix1 = "T";
            var keyPrefix2 = "App2/";
            var keyPrefix3 = "Test";

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ClientManager = TestHelpers.CreateMockedConfigurationClientManager(mockClient.Object);
                    options.TrimKeyPrefix(keyPrefix1).TrimKeyPrefix(keyPrefix2).TrimKeyPrefix(keyPrefix3);
                })
                .Build();

            Assert.True(config["Key1"] == "TestValue1");
            Assert.True(config["Key2"] == "TestValue2");
            Assert.True(config["Key3"] == "TestValue3");
            Assert.True(config["Key4"] == "TestValue4");
            Assert.True(config["TestKey1"] == "TestValue2.1");
        }

        [Fact]
        public void TrimKeyPrefix_TestCase2()
        {
            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict);

            mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Returns(new MockAsyncPageable(_kvCollectionPageOne));

            mockClient.Setup(c => c.GetConfigurationSettingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Response.FromValue(_kv, mockResponse.Object));

            // Trim following prefixes from all keys in the configuration.
            var keyPrefix1 = "T";
            var keyPrefix2 = "App2/";
            var keyPrefix3 = "Test";

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ClientManager = TestHelpers.CreateMockedConfigurationClientManager(mockClient.Object);
                    options.TrimKeyPrefix(keyPrefix3).TrimKeyPrefix(keyPrefix2).TrimKeyPrefix(keyPrefix1);
                })
                .Build();

            Assert.True(config["Key1"] == "TestValue1");
            Assert.True(config["Key2"] == "TestValue2");
            Assert.True(config["Key3"] == "TestValue3");
            Assert.True(config["Key4"] == "TestValue4");
            Assert.True(config["TestKey1"] == "TestValue2.1");
        }

        [Fact]
        public void TestCorrelationContextInHeader()
        {
            var response = new MockResponse(200);
            response.SetContent(SerializationHelpers.Serialize(_kvCollectionPageOne.ToArray(), TestHelpers.SerializeBatch));

            var mockTransport = new MockTransport(response);

            var options = new AzureAppConfigurationOptions();
            options.ClientOptions.Transport = mockTransport;
            var clientManager = TestHelpers.CreateMockedConfigurationClientManager(options);
            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ClientManager = clientManager;
                    options.Select("*", null);
                })
                .Build();

            MockRequest request = mockTransport.SingleRequest;

            Assert.True(request.Headers.TryGetValues("Correlation-Context", out IEnumerable<string> correlationHeader));
            Assert.NotNull(correlationHeader.First());
            Assert.Contains(Enum.GetName(typeof(RequestType), RequestType.Startup), correlationHeader.First(), StringComparison.InvariantCultureIgnoreCase);
        }

        [Fact]
        public void TestUserAgentHeader()
        {
            // Regex to perform the following validations
            // 1. Contains the name of the configuration provider package
            // 2. Contains the informational version (not the assembly version) of the package
            // 3. Contains a valid version format for a stable or preview version of the package
            // 4. Contains the name and version of the App Configuration SDK package
            // 5. Contains the runtime information (target framework, OS description etc.) in the format set by the SDK
            // 6. Does not contain any additional components
            string userAgentRegex = @"^Microsoft\.Extensions\.Configuration\.AzureAppConfiguration/\d+\.\d+\.\d+(\+[a-z0-9]+)?(-preview(\.\d+)?)?,azsdk-net-Data.AppConfiguration/[.+\w-]+ \([.;\w\s]+\)$";

            var response = new MockResponse(200);
            response.SetContent(SerializationHelpers.Serialize(_kvCollectionPageOne.ToArray(), TestHelpers.SerializeBatch));

            var mockTransport = new MockTransport(response);
            var options = new AzureAppConfigurationOptions();
            options.ClientOptions.Transport = mockTransport;

            var clientManager = TestHelpers.CreateMockedConfigurationClientManager(options);
            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ClientManager = clientManager;
                }).Build();

            MockRequest request = mockTransport.SingleRequest;
            Assert.True(request.Headers.TryGetValue("User-Agent", out string userAgentHeader));
            Assert.Matches(userAgentRegex, userAgentHeader);
        }

        [Fact]
        public void TestTurnOffRequestTracing()
        {
            var response = new MockResponse(200);
            response.SetContent(SerializationHelpers.Serialize(_kvCollectionPageOne.ToArray(), TestHelpers.SerializeBatch));

            var mockTransport = new MockTransport(response);

            var options = new AzureAppConfigurationOptions();
            options.ClientOptions.Transport = mockTransport;

            Environment.SetEnvironmentVariable(EnvironmentVariables.DisableRequestTracing, "True");

            var clientManager = TestHelpers.CreateMockedConfigurationClientManager(options);
            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ClientManager = clientManager;
                    options.Select("*", null);
                })
                .Build();

            MockRequest request = mockTransport.SingleRequest;

            Assert.False(request.Headers.TryGetValues("Correlation-Context", out IEnumerable<string> correlationHeader));

            // Reset transport
            response = new MockResponse(200);
            response.SetContent(SerializationHelpers.Serialize(_kvCollectionPageOne.ToArray(), TestHelpers.SerializeBatch));

            mockTransport = new MockTransport(response);
            options.ClientOptions.Transport = mockTransport;

            // Delete the request tracing environment variable
            Environment.SetEnvironmentVariable(EnvironmentVariables.DisableRequestTracing, null);
            Environment.SetEnvironmentVariable(RequestTracingConstants.AzureFunctionEnvironmentVariable, "v1.0");

            var clientManager1 = TestHelpers.CreateMockedConfigurationClientManager(options);
            config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ClientManager = clientManager1;
                    options.Select("*", null);
                })
                .Build();

            request = mockTransport.SingleRequest;

            Assert.True(request.Headers.TryGetValues("Correlation-Context", out correlationHeader));
            Assert.NotNull(correlationHeader.First());
            Assert.Contains(Enum.GetName(typeof(HostType), HostType.AzureFunction), correlationHeader.First(), StringComparison.InvariantCultureIgnoreCase);

            // Delete the azure function environment variable
            Environment.SetEnvironmentVariable(RequestTracingConstants.AzureFunctionEnvironmentVariable, null);
        }

        [Fact]
        public void TestKeepSelectorPrecedenceAfterDedup()
        {
            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict);
            var kvOfDevLabel = new List<ConfigurationSetting>
            {
                ConfigurationModelFactory.ConfigurationSetting("message", "message from dev label", "dev")
            };
            var kvOfProdLabel = new List<ConfigurationSetting>
            {
                ConfigurationModelFactory.ConfigurationSetting("message", "message from prod label", "prod")
            };
            mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.Is<SettingSelector>(s => s.LabelFilter == "dev"), It.IsAny<CancellationToken>()))
                .Returns(new MockAsyncPageable(kvOfDevLabel));
            mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.Is<SettingSelector>(s => s.LabelFilter == "prod"), It.IsAny<CancellationToken>()))
                .Returns(new MockAsyncPageable(kvOfProdLabel));

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ClientManager = TestHelpers.CreateMockedConfigurationClientManager(mockClient.Object);
                    options.Select("message", "dev");
                    options.Select("message", "prod");
                    options.Select("message", "dev");
                })
                .Build();

            Assert.True(config["message"] == "message from dev label");
        }

        [Fact]
        public void TestActivitySource()
        {
            string activitySourceName = Guid.NewGuid().ToString();

            var _activities = new List<Activity>();
            var _activityListener = new ActivityListener
            {
                ShouldListenTo = source => source.Name == activitySourceName,
                Sample = (ref ActivityCreationOptions<ActivityContext> options) => ActivitySamplingResult.AllData,
                ActivityStarted = activity => _activities.Add(activity),
            };
            ActivitySource.AddActivityListener(_activityListener);

            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict);

            mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Returns(new MockAsyncPageable(_kvCollectionPageOne));

            mockClient.Setup(c => c.GetConfigurationSettingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Response.FromValue(_kv, mockResponse.Object));

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ClientManager = TestHelpers.CreateMockedConfigurationClientManager(mockClient.Object);
                    options.ActivitySourceName = activitySourceName;
                })
                .Build();

            Assert.Contains(_activities, a => a.OperationName == "Load");

            _activityListener.Dispose();
        }
    }
}
