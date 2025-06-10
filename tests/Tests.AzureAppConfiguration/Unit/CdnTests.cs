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

            Response<ConfigurationSetting> GetIfChanged(ConfigurationSetting setting, bool _, CancellationToken cancellationToken)
            {
                var currentSetting = keyValueCollection.FirstOrDefault(s => s.Key == setting.Key && s.Label == setting.Label);

                if (currentSetting == null)
                {
                    throw new RequestFailedException(new MockResponse(404));
                }

                return Response.FromValue(currentSetting, new MockResponse(200));
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
            // another change: sentinel deleted
            {
                keyValueCollection.Remove(keyValueCollection[0]);

                // Wait for the cache to expire
                await Task.Delay(1500);

                // Trigger refresh - this should set a token in the CDN token accessor
                await refresher.RefreshAsync();

                // Verify that the CDN token accessor has a token set to new value
                Assert.NotNull(capturedOptions.CdnTokenAccessor.Current);
                Assert.NotEmpty(capturedOptions.CdnTokenAccessor.Current);
                Assert.NotEqual(previousCdnToken, capturedOptions.CdnTokenAccessor.Current);

                // Verify the configuration was updated
                Assert.Null(config["TestKey1"]);
            }
        }

        [Fact]
        public async Task CdnTests_ParallelAppsHaveSameCdnTokenSequence()
        {
            var mockAsyncPageable = new MockAsyncPageable(_kvCollection.ToList());

            // async coordination: Both apps are ready => wait for two releases
            var startupSync = new SemaphoreSlim(0, 2);
            var noChangeSync = new SemaphoreSlim(0, 2);

            // broadcast gates: coordinator releases twice, each app awaits once
            var firstChangeGate = new SemaphoreSlim(0, 2);
            var noChangeGate = new SemaphoreSlim(0, 2);
            var secondChangeGate = new SemaphoreSlim(0, 2);

            async Task CreateAppTask(List<string> cdnTokenList)
            {
                var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict);

                // Both clients use the same shared pageable for consistency
                mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                    .Returns(() => mockAsyncPageable);

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

                // Initial state - CDN token should be null
                cdnTokenList.Add(capturedOptions.CdnTokenAccessor.Current);

                // Signal that this app is initialized
                startupSync.Release();

                // Wait for first change to be applied
                await firstChangeGate.WaitAsync();
                await Task.Delay(1500);
                await refresher.RefreshAsync();

                cdnTokenList.Add(capturedOptions.CdnTokenAccessor.Current);

                // No change (should keep same token)
                await noChangeGate.WaitAsync();
                await Task.Delay(1500);
                await refresher.RefreshAsync();

                cdnTokenList.Add(capturedOptions.CdnTokenAccessor.Current);

                // Signal that this app is done with no-change refresh
                noChangeSync.Release();

                // Wait for second change to be applied
                await secondChangeGate.WaitAsync();
                await Task.Delay(1500);
                await refresher.RefreshAsync();

                cdnTokenList.Add(capturedOptions.CdnTokenAccessor.Current);
            }

            var changeTask = Task.Run(async () =>
            {
                // First change
                await Task.WhenAll(startupSync.WaitAsync(), startupSync.WaitAsync()); // Wait for both apps to complete startup
                var updatedCollection = _kvCollection.ToList();
                updatedCollection[0] = TestHelpers.ChangeValue(updatedCollection[0], "newValue");
                mockAsyncPageable.UpdateCollection(updatedCollection);

                firstChangeGate.Release(2);

                // No change
                noChangeGate.Release(2);

                // Second change
                await Task.WhenAll(noChangeSync.WaitAsync(), noChangeSync.WaitAsync()); ; // Wait for both apps to complete no-change refresh
                updatedCollection = _kvCollection.ToList();
                updatedCollection[0] = TestHelpers.ChangeValue(updatedCollection[0], "anotherNewValue");
                mockAsyncPageable.UpdateCollection(updatedCollection);

                secondChangeGate.Release(2);
            });

            // Run both apps in parallel along with the change coordinator
            var app1CdnTokens = new List<string>();
            var app2CdnTokens = new List<string>();
            var task1 = CreateAppTask(app1CdnTokens);
            var task2 = CreateAppTask(app2CdnTokens);

            await Task.WhenAll(task1, task2, changeTask);

            // Verify both apps captured the same number of tokens
            Assert.Equal(4, app1CdnTokens.Count);
            Assert.Equal(4, app2CdnTokens.Count);

            // Verify the CDN token sequences are identical between the two apps
            for (int i = 0; i < app1CdnTokens.Count; i++)
            {
                Assert.True(app1CdnTokens[i] == app2CdnTokens[i]);
            }

            // Verify the expected token pattern:
            // Index 0: null (initial state)
            // Index 1: non-null (after first change)
            // Index 2: same as index 1 (no change)
            // Index 3: non-null and different from index 1 (after second change)
            Assert.Null(app1CdnTokens[0]);
            Assert.NotNull(app1CdnTokens[1]);
            Assert.NotEmpty(app1CdnTokens[1]);
            Assert.Equal(app1CdnTokens[1], app1CdnTokens[2]);
            Assert.NotNull(app1CdnTokens[3]);
            Assert.NotEmpty(app1CdnTokens[3]);
            Assert.NotEqual(app1CdnTokens[1], app1CdnTokens[3]);
        }
    }
}
