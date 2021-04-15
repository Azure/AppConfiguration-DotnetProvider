// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Azure;
using Azure.Core;
using Azure.Core.Testing;
using Azure.Data.AppConfiguration;
using Azure.Data.AppConfiguration.Tests;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.AzureAppConfiguration;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Xunit;

namespace Tests.AzureAppConfiguration
{
    public class Tests
    {
        string _connectionString = TestHelpers.CreateMockEndpointString();

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

        List<ConfigurationSetting> _kvDelimitedCollection = new List<ConfigurationSetting>
        {
            ConfigurationModelFactory.ConfigurationSetting("Test.Key.1", "TestValue1", "label",
                eTag: new ETag("1c6a3612-9751-41b8-b444-d82b3a81bc2c"),
                contentType:"text"),
            ConfigurationModelFactory.ConfigurationSetting("Test,Key,2", "TestValue2", "label",
                eTag: new ETag("05449f5b-fd28-4d8a-a686-072eb704ecff"),
                contentType: "text"),
            ConfigurationModelFactory.ConfigurationSetting("Test:Key:3", "TestValue3", "label",
                eTag: new ETag("1f2ad89c-be57-412a-891a-53a738c6c6e0"),
                contentType: "text"),
            ConfigurationModelFactory.ConfigurationSetting("Test;Key;4", "TestValue4", "label",
                eTag: new ETag("6ae1ba59-00dc-4bef-9611-0e961879eb07"),
                contentType: "text"),
            ConfigurationModelFactory.ConfigurationSetting("Test/Key/5", "TestValue5", "label",
                eTag: new ETag("2817818f-2f3b-4b68-bb54-2ccc8cecd671"),
                contentType: "text"),
            ConfigurationModelFactory.ConfigurationSetting("Test-Key-6", "TestValue6", "label",
                eTag: new ETag("868336a7-83e3-47d6-a688-c97ba4a1728d"),
                contentType: "text"),
            ConfigurationModelFactory.ConfigurationSetting("Test_Key_7", "TestValue7", "label",
                eTag: new ETag("783dd1d3-11b8-4636-8e94-b4bf985c91fb"),
                contentType: "text"),
            ConfigurationModelFactory.ConfigurationSetting("Test__Key__8", "TestValue8", "label",
                eTag: new ETag("f0107901-3a84-48f3-b76e-5e75722709bb"),
                contentType: "text"),
            ConfigurationModelFactory.ConfigurationSetting("TestKey9", "TestValue9", "label",
                eTag: new ETag("b5cc8d9c-0860-499f-99fc-9c3a53a68eb1"),
                contentType: "text"),
        };

        [Fact]
        public void AddsConfigurationValues()
        {
            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict, TestHelpers.CreateMockEndpointString());

            mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Returns(new MockAsyncPageable(_kvCollectionPageOne));

            mockClient.Setup(c => c.GetConfigurationSettingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Response.FromValue(_kv, mockResponse.Object));

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options => options.Client = mockClient.Object)
                .Build();

            Assert.True(config["TestKey1"] == "TestValue1");
            Assert.True(config["tEsTkEy1"] == "TestValue1");    // Case-insensitive
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
            var exception = Record.Exception(() =>
            {
                builder.AddAzureAppConfiguration(invalidConnectionString, false);
                builder.Build();
            });
            Assert.NotNull(exception);
            Assert.IsType<ArgumentException>(exception);
        }

        [Fact]
        public void TrimKeyPrefix_TestCase1()
        {
            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict, TestHelpers.CreateMockEndpointString());

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
                    options.Client = mockClient.Object;
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
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict, TestHelpers.CreateMockEndpointString());

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
                    options.Client = mockClient.Object;
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
        public void ReplaceKeySeparator_TestCase1()
        {
            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict, TestHelpers.CreateMockEndpointString());

            mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                      .Returns(new MockAsyncPageable(_kvDelimitedCollection));

            mockClient.Setup(c => c.GetConfigurationSettingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                      .ReturnsAsync(Response.FromValue(_kv, mockResponse.Object));

            var config = new ConfigurationBuilder()
                         .AddAzureAppConfiguration(options =>
                         {
                             options.Client = mockClient.Object;
                             // Replace following separators with colon (:) from all keys in the configuration.
                             options.ReplaceKeySeparator(KeySeparator.Period)
                                    .ReplaceKeySeparator(KeySeparator.Comma)
                                    .ReplaceKeySeparator(KeySeparator.Semicolon)
                                    .ReplaceKeySeparator(KeySeparator.ForwardSlash)
                                    .ReplaceKeySeparator(KeySeparator.Dash)
                                    .ReplaceKeySeparator(KeySeparator.Underscore)
                                    .ReplaceKeySeparator(KeySeparator.DoubleUnderscore);
                         })
                         .Build();

            Assert.Equal("TestValue1", config["Test:Key:1"]);
            Assert.Equal("TestValue2", config["Test:Key:2"]);
            Assert.Equal("TestValue3", config["Test:Key:3"]);
            Assert.Equal("TestValue4", config["Test:Key:4"]);
            Assert.Equal("TestValue5", config["Test:Key:5"]);
            Assert.Equal("TestValue6", config["Test:Key:6"]);
            Assert.Equal("TestValue7", config["Test:Key:7"]);
            Assert.Equal("TestValue8", config["Test:Key:8"]);
            Assert.Equal("TestValue9", config["TestKey9"]);
        }

        [Fact]
        public void ReplaceKeySeparator_TestCase2()
        {
            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict, TestHelpers.CreateMockEndpointString());

            mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                      .Returns(new MockAsyncPageable(_kvDelimitedCollection));

            mockClient.Setup(c => c.GetConfigurationSettingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                      .ReturnsAsync(Response.FromValue(_kv, mockResponse.Object));

            var config = new ConfigurationBuilder()
                         .AddAzureAppConfiguration(options =>
                         {
                             options.Client = mockClient.Object;
                             // Replace following separators with colon (:) from all keys in the configuration.
                             options.ReplaceKeySeparator(KeySeparator.DoubleUnderscore)
                                    .ReplaceKeySeparator(KeySeparator.Underscore)
                                    .ReplaceKeySeparator(KeySeparator.Dash)
                                    .ReplaceKeySeparator(KeySeparator.ForwardSlash)
                                    .ReplaceKeySeparator(KeySeparator.Semicolon)
                                    .ReplaceKeySeparator(KeySeparator.Comma)
                                    .ReplaceKeySeparator(KeySeparator.Period);
                         })
                         .Build();

            Assert.Equal("TestValue1", config["Test:Key:1"]);
            Assert.Equal("TestValue2", config["Test:Key:2"]);
            Assert.Equal("TestValue3", config["Test:Key:3"]);
            Assert.Equal("TestValue4", config["Test:Key:4"]);
            Assert.Equal("TestValue5", config["Test:Key:5"]);
            Assert.Equal("TestValue6", config["Test:Key:6"]);
            Assert.Equal("TestValue7", config["Test:Key:7"]);
            Assert.Equal("TestValue8", config["Test:Key:8"]);
            Assert.Equal("TestValue9", config["TestKey9"]);
        }

        [Fact]
        public void TestCorrelationContextInHeader()
        {
            var response = new MockResponse(200);
            response.SetContent(SerializationHelpers.Serialize(_kvCollectionPageOne.ToArray(), TestHelpers.SerializeBatch));

            var mockTransport = new MockTransport(response);

            var clientOptions = new ConfigurationClientOptions
            {
                Transport = mockTransport
            };

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.Client = new ConfigurationClient(_connectionString, clientOptions);
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
            string userAgentRegex = @"^Microsoft\.Extensions\.Configuration\.AzureAppConfiguration/\d+\.\d+\.\d+(-preview)?(-\d+-\d+)?,azsdk-net-Data.AppConfiguration/[.+\w]+ \([.;\w\s]+\)$";
            
            var response = new MockResponse(200);
            response.SetContent(SerializationHelpers.Serialize(_kvCollectionPageOne.ToArray(), TestHelpers.SerializeBatch));

            var mockTransport = new MockTransport(response);
            var clientOptions = new ConfigurationClientOptions { Transport = mockTransport };
            clientOptions.AddPolicy(new UserAgentHeaderPolicy(), HttpPipelinePosition.PerCall);

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.Client = new ConfigurationClient(_connectionString, clientOptions);
                }).Build();

            MockRequest request = mockTransport.SingleRequest;
            string appUserAgent = UserAgentHeaderPolicy.GenerateUserAgent();
            Assert.True(request.Headers.TryGetValue("User-Agent", out string userAgentHeader));
            Assert.Matches(userAgentRegex, userAgentHeader);
        }

        [Fact]
        public void TestTurnOffRequestTracing()
        {
            var response = new MockResponse(200);
            response.SetContent(SerializationHelpers.Serialize(_kvCollectionPageOne.ToArray(), TestHelpers.SerializeBatch));

            var mockTransport = new MockTransport(response);

            var clientOptions = new ConfigurationClientOptions
            {
                Transport = mockTransport
            };

            Environment.SetEnvironmentVariable(RequestTracingConstants.RequestTracingDisabledEnvironmentVariable, "True");

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.Client = new ConfigurationClient(_connectionString, clientOptions);
                    options.Select("*", null);
                })
                .Build();

            MockRequest request = mockTransport.SingleRequest;

            Assert.False(request.Headers.TryGetValues("Correlation-Context", out IEnumerable<string> correlationHeader));

            // Reset transport
            response = new MockResponse(200);
            response.SetContent(SerializationHelpers.Serialize(_kvCollectionPageOne.ToArray(), TestHelpers.SerializeBatch));

            mockTransport = new MockTransport(response);
            clientOptions = new ConfigurationClientOptions
            {
                Transport = mockTransport
            };

            // Delete the request tracing environment variable
            Environment.SetEnvironmentVariable(RequestTracingConstants.RequestTracingDisabledEnvironmentVariable, null);
            Environment.SetEnvironmentVariable(RequestTracingConstants.AzureFunctionEnvironmentVariable, "v1.0");

            config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.Client = new ConfigurationClient(_connectionString, clientOptions);
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
    }
}
