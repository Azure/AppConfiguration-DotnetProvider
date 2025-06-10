// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Azure;
using Azure.Core.Testing;
using Azure.Data.AppConfiguration;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.AzureAppConfiguration;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Tests.AzureAppConfiguration
{
    public class CdnTests
    {
        List<ConfigurationSetting> _kvCollection = new List<ConfigurationSetting>
        {
            ConfigurationModelFactory.ConfigurationSetting(
                key: "TestKey1",
                label: "label",
                value: "TestValue1",
                eTag: new ETag("0a76e3d7-7ec1-4e37-883c-9ea6d0d89e63"),
                contentType: "text"),

            ConfigurationModelFactory.ConfigurationSetting(
                key: "TestKey2",
                label: "label",
                value: "TestValue2",
                eTag: new ETag("31c38369-831f-4bf1-b9ad-79db56c8b989"),
                contentType: "text"),

            ConfigurationModelFactory.ConfigurationSetting(
                key: "TestKey3",
                label: "label",
                value: "TestValue3",
                eTag: new ETag("bb203f2b-c113-44fc-995d-b933c2143339"),
                contentType: "text"),

            ConfigurationModelFactory.ConfigurationSetting(
                key: "TestKeyWithMultipleLabels",
                label: "label1",
                value: "TestValueForLabel1",
                eTag: new ETag("bb203f2b-c113-44fc-995d-b933c2143339"),
                contentType: "text"),

            ConfigurationModelFactory.ConfigurationSetting(
                key: "TestKeyWithMultipleLabels",
                label: "label2",
                value: "TestValueForLabel2",
                eTag: new ETag("bb203f2b-c113-44fc-995d-b933c2143339"),
                contentType: "text")
        };

        [Fact]
        public void CdnTests_CdnWithClientFactoryRequiresClientOptions()
        {
            var mockClientFactory = new Mock<IAzureClientFactory<ConfigurationClient>>();

            var configBuilder = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.SetClientFactory(mockClientFactory.Object) // No client options provided
                           .ConnectCdn(TestHelpers.MockCdnEndpoint);
                });

            Exception exception = Assert.Throws<ArgumentException>(() => configBuilder.Build());
            Assert.IsType<InvalidOperationException>(exception.InnerException);
        }

        [Fact]
        public void CdnTests_CdnWithClientFactoryAndClientOptionsSucceeds()
        {
            var mockClientFactory = new Mock<IAzureClientFactory<ConfigurationClient>>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict);
            var clientOptions = new ConfigurationClientOptions();

            mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Returns(new MockAsyncPageable(new List<ConfigurationSetting>()));

            mockClientFactory.Setup(f => f.CreateClient(It.IsAny<string>()))
                .Returns(mockClient.Object);

            AzureAppConfigurationOptions capturedOptions = null;

            var configBuilder = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.SetClientFactory(mockClientFactory.Object, clientOptions) // Client options provided
                           .ConnectCdn(TestHelpers.MockCdnEndpoint);
                    capturedOptions = options;
                });

            Assert.NotNull(configBuilder.Build());

            Assert.NotNull(capturedOptions);
            Assert.True(capturedOptions.IsCdnEnabled);
            Assert.Equal(clientOptions, capturedOptions.ClientOptions);

            mockClient.Verify(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public void CdnTests_DoesNotSupportLoadBalancing()
        {
            var configBuilder = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ConnectCdn(TestHelpers.MockCdnEndpoint)
                           .LoadBalancingEnabled = true;
                });

            Exception exception = Assert.Throws<ArgumentException>(() => configBuilder.Build());
            Assert.IsType<InvalidOperationException>(exception.InnerException);
        }

        [Fact]
        public async Task CdnTests_RefreshWithRegisterAll()
        {
            var keyValueCollection = new List<ConfigurationSetting>(_kvCollection);
            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict);
            var mockAsyncPageable = new MockAsyncPageable(keyValueCollection);

            mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Returns(mockAsyncPageable);

            IConfigurationRefresher refresher = null;
            AzureAppConfigurationOptions capturedOptions = null;

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ConnectCdn(TestHelpers.MockCdnEndpoint)
                        .Select("TestKey*")
                        .ConfigureRefresh(refreshOptions =>
                        {
                            refreshOptions.RegisterAll()
                                .SetRefreshInterval(TimeSpan.FromSeconds(1));
                        });

                    options.ClientManager = TestHelpers.CreateMockedConfigurationClientManager(mockClient.Object);

                    refresher = options.GetRefresher();
                    capturedOptions = options;
                })
                .Build();

            Assert.Equal("TestValue1", config["TestKey1"]);
            Assert.Equal("TestValue2", config["TestKey2"]);
            Assert.Equal("TestValue3", config["TestKey3"]);

            // Verify CDN is enabled
            Assert.True(capturedOptions.IsCdnEnabled);

            // Verify that current CDN token is null at startup
            Assert.Null(capturedOptions.CdnTokenAccessor.Current);

            //
            // change
            {
                keyValueCollection[0] = TestHelpers.ChangeValue(keyValueCollection[0], "newValue");

                mockAsyncPageable.UpdateCollection(keyValueCollection);

                // Wait for the cache to expire
                await Task.Delay(1500);

                // Trigger refresh - this should set a token in the CDN token accessor
                await refresher.RefreshAsync();

                // Verify that the CDN token accessor has a token set to new value
                Assert.NotNull(capturedOptions.CdnTokenAccessor.Current);
                Assert.NotEmpty(capturedOptions.CdnTokenAccessor.Current);

                // Verify the configuration was updated
                Assert.Equal("newValue", config["TestKey1"]);
            }

            string previousCdnToken = capturedOptions.CdnTokenAccessor.Current;

            //
            // no change
            {
                // Wait for the cache to expire
                await Task.Delay(1500);

                await refresher.RefreshAsync();

                // Verify that the CDN token accessor has a token set to previous CDN token
                Assert.Equal(previousCdnToken, capturedOptions.CdnTokenAccessor.Current);
            }

            //
            // another change
            {
                keyValueCollection[0] = TestHelpers.ChangeValue(keyValueCollection[0], "anotherNewValue");

                mockAsyncPageable.UpdateCollection(keyValueCollection);

                // Wait for the cache to expire
                await Task.Delay(1500);

                // Trigger refresh - this should set a token in the CDN token accessor
                await refresher.RefreshAsync();

                // Verify that the CDN token accessor has a token set to new value
                Assert.NotNull(capturedOptions.CdnTokenAccessor.Current);
                Assert.NotEmpty(capturedOptions.CdnTokenAccessor.Current);
                Assert.NotEqual(previousCdnToken, capturedOptions.CdnTokenAccessor.Current);

                // Verify the configuration was updated
                Assert.Equal("anotherNewValue", config["TestKey1"]);
            }
        }

        [Fact]
        public async Task CdnTests_RefreshWithRegister()
        {
            var keyValueCollection = new List<ConfigurationSetting>(_kvCollection);
            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict);

            Response<ConfigurationSetting> GetSettingFromService(string k, string l, CancellationToken ct)
            {
                return Response.FromValue(keyValueCollection.FirstOrDefault(s => s.Key == k && s.Label == l), mockResponse.Object);
            }

            Response<ConfigurationSetting> GetIfChanged(ConfigurationSetting setting, bool onlyIfChanged, CancellationToken cancellationToken)
            {
                var newSetting = keyValueCollection.FirstOrDefault(s => s.Key == setting.Key && s.Label == setting.Label);
                var unchanged = (newSetting.Key == setting.Key && newSetting.Label == setting.Label && newSetting.Value == setting.Value);
                var response = new MockResponse(unchanged ? 304 : 200);
                return Response.FromValue(newSetting, response);
            }

            mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Returns(() =>
                {
                    var copy = new List<ConfigurationSetting>();
                    foreach (var setting in keyValueCollection)
                    {
                        copy.Add(TestHelpers.CloneSetting(setting));
                    }

                    return new MockAsyncPageable(copy);
                });

            mockClient.Setup(c => c.GetConfigurationSettingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Func<string, string, CancellationToken, Response<ConfigurationSetting>>)GetSettingFromService);

            mockClient.Setup(c => c.GetConfigurationSettingAsync(It.IsAny<ConfigurationSetting>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Func<ConfigurationSetting, bool, CancellationToken, Response<ConfigurationSetting>>)GetIfChanged);

            IConfigurationRefresher refresher = null;
            AzureAppConfigurationOptions capturedOptions = null;

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ConnectCdn(TestHelpers.MockCdnEndpoint)
                        .Select("TestKey*")
                        .ConfigureRefresh(refreshOptions =>
                        {
                            refreshOptions.Register("TestKey1", "label", refreshAll: true)
                                .SetRefreshInterval(TimeSpan.FromSeconds(1));
                        });

                    options.ClientManager = TestHelpers.CreateMockedConfigurationClientManager(mockClient.Object);

                    refresher = options.GetRefresher();
                    capturedOptions = options;
                })
                .Build();

            Assert.Equal("TestValue1", config["TestKey1"]);
            Assert.Equal("TestValue2", config["TestKey2"]);
            Assert.Equal("TestValue3", config["TestKey3"]);

            // Verify CDN is enabled
            Assert.True(capturedOptions.IsCdnEnabled);

            // Verify that current CDN token is null at startup
            Assert.Null(capturedOptions.CdnTokenAccessor.Current);

            // 
            // change
            {
                keyValueCollection[0] = TestHelpers.ChangeValue(keyValueCollection[0], "newValue");

                // Wait for the cache to expire
                await Task.Delay(1500);

                // Trigger refresh - this should set a token in the CDN token accessor
                await refresher.RefreshAsync();

                // Verify that the CDN token is set to the new value
                Assert.NotNull(capturedOptions.CdnTokenAccessor.Current);
                Assert.NotEmpty(capturedOptions.CdnTokenAccessor.Current);

                // Verify the configuration was updated
                Assert.Equal("newValue", config["TestKey1"]);
            }

            string previousCdnToken = capturedOptions.CdnTokenAccessor.Current;

            //
            // no change
            {
                await Task.Delay(1500);

                await refresher.RefreshAsync();

                // Verify that the CDN token accessor has a token set to previous CDN token
                Assert.Equal(previousCdnToken, capturedOptions.CdnTokenAccessor.Current);
            }

            //
            // another change
            {
                keyValueCollection[0] = TestHelpers.ChangeValue(keyValueCollection[0], "anotherNewValue");

                // Wait for the cache to expire
                await Task.Delay(1500);

                // Trigger refresh - this should set a token in the CDN token accessor
                await refresher.RefreshAsync();

                // Verify that the CDN token accessor has a token set to new value
                Assert.NotNull(capturedOptions.CdnTokenAccessor.Current);
                Assert.NotEmpty(capturedOptions.CdnTokenAccessor.Current);
                Assert.NotEqual(previousCdnToken, capturedOptions.CdnTokenAccessor.Current);

                // Verify the configuration was updated
                Assert.Equal("anotherNewValue", config["TestKey1"]);
            }
        }
    }
}
