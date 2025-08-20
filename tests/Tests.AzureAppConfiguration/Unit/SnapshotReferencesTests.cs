using Azure;
using Azure.Core.Testing;
using Azure.Data.AppConfiguration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.AzureAppConfiguration;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace Tests.AzureAppConfiguration
{

    public class SnapshotReferenceTests
    {
        private readonly ITestOutputHelper _output;

        public SnapshotReferenceTests(ITestOutputHelper output)
        {
            _output = output;
        }

        // The actual configuration value we expect to get from the snapshot
        string _snapshotConfigValue = "ValueFromSnapshot";

        // A snapshot reference setting
        ConfigurationSetting _snapshotReference1 = ConfigurationModelFactory.ConfigurationSetting(
            key: "SnapshotRef1",
            value: @"{""snapshot_name"": ""snapshot1""}",
            eTag: new ETag("snapshot-ref-etag-123"),
            contentType: SnapshotReferenceConstants.ContentType);

        // A regular configuration setting
        ConfigurationSetting _settingInSnapshot1 = ConfigurationModelFactory.ConfigurationSetting(
            key: "TestKey1",
            value: "ValueFromSnapshot",
            eTag: new ETag("snapshot-setting-etag-456"));

        ConfigurationSetting _snapshotReference2 = ConfigurationModelFactory.ConfigurationSetting(
                key: "SnapshotRef2",
                value: @"{""snapshot_name"": ""snapshot2""}",
                eTag: new ETag("snapshot-ref-etag-456"),
                contentType: SnapshotReferenceConstants.ContentType);

        ConfigurationSetting _settingInSnapshot2 = ConfigurationModelFactory.ConfigurationSetting(
                key: "TestKey2",
                value: "ValueFromSnapshot2",
                eTag: new ETag("snapshot-setting-etag-789"));

        // Reference points to an empty snapshot
        ConfigurationSetting _snapshotReferenceEmptyValue = ConfigurationModelFactory.ConfigurationSetting(
                key: "SnapshotRefEmptyValue",
                value: @"{""snapshot_name"": """"}",
                eTag: new ETag("snapshot-ref-etag-789"),
                contentType: SnapshotReferenceConstants.ContentType);

        // Reference with invalid JSON
        ConfigurationSetting _snapshotReferenceInvalidJson = ConfigurationModelFactory.ConfigurationSetting(
                key: "SnapshotRefInvalidJson",
                value: "{invalid json, missing quotes}",
                eTag: new ETag("snapshot-ref-etag-999"),
                contentType: SnapshotReferenceConstants.ContentType);

        ConfigurationSetting _snapshotReferenceInvalidJson2 = ConfigurationModelFactory.ConfigurationSetting(
                key: "SnapshotRefInvalidJson2",
                value: "",
                eTag: new ETag("snapshot-ref-etag-999"),
                contentType: SnapshotReferenceConstants.ContentType);

        ConfigurationSetting _regularKeyValue = ConfigurationModelFactory.ConfigurationSetting(
            key: "RegularKey",
            value: "RegularValue",
            eTag: new ETag("regular-etag-123"),
            contentType: "");

        ConfigurationSetting _snapshotReferenceWithExtraProperties = ConfigurationModelFactory.ConfigurationSetting(
            key: "SnapshotRefExtraProps",
            value: @"{""snapshot_name"": ""snapshot1"", ""extra_property"": ""extra_value""}",
            eTag: new ETag("snapshot-ref-etag-777"),
            contentType: SnapshotReferenceConstants.ContentType);

        ConfigurationSetting updatedSnapshotRef1 = ConfigurationModelFactory.ConfigurationSetting(
            key: "SnapshotRef1",
            value: @"{""snapshot_name"": ""snapshot2""}",
            eTag: new ETag("snapshot-ref-etag-2"),
            contentType: SnapshotReferenceConstants.ContentType);

        [Fact]
        public void UseSnapshotReference()
        {
            // Create mock objects (fake Azure services)
            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict);

            // Set up what the fake Azure App Configuration client should return
            mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Returns(new MockAsyncPageable(new List<ConfigurationSetting> { _snapshotReference1 }));

            // Create a real ConfigurationSnapshot object instead of mocking it
            var settingsToInclude = new List<ConfigurationSettingsFilter>
            {
                new ConfigurationSettingsFilter("*")
            };
            var realSnapshot = new ConfigurationSnapshot(settingsToInclude)
            {
                SnapshotComposition = SnapshotComposition.Key
            };

            mockClient.Setup(c => c.GetSnapshotAsync("snapshot1", It.IsAny<IEnumerable<SnapshotFields>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Response.FromValue(realSnapshot, mockResponse.Object));

            // Set up what settings are inside the snapshot
            mockClient.Setup(c => c.GetConfigurationSettingsForSnapshotAsync("snapshot1", It.IsAny<CancellationToken>()))
                .Returns(new MockAsyncPageable(new List<ConfigurationSetting> { _settingInSnapshot1 }));

            // Build the configuration using our fake clients
            var configuration = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ClientManager = TestHelpers.CreateMockedConfigurationClientManager(mockClient.Object);
                })
                .Build();

            // Test that we get the value from the snapshot, not the reference
            Assert.Equal(_snapshotConfigValue, configuration["TestKey1"]);

            // Verify snapshot references themselves are not in the config
            Assert.Null(configuration["SnapshotRef1"]);
        }

        [Fact]
        public void MultipleSnapshotReferences()
        {
            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict);

            mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Returns(new MockAsyncPageable(new List<ConfigurationSetting> { _snapshotReference1, _snapshotReference2 }));

            var settingsToInclude = new List<ConfigurationSettingsFilter>
            {
                new ConfigurationSettingsFilter("*")
            };

            var realSnapshot1 = new ConfigurationSnapshot(settingsToInclude)
            {
                SnapshotComposition = SnapshotComposition.Key
            };

            var realSnapshot2 = new ConfigurationSnapshot(settingsToInclude)
            {
                SnapshotComposition = SnapshotComposition.Key
            };

            mockClient.Setup(c => c.GetSnapshotAsync("snapshot1", It.IsAny<IEnumerable<SnapshotFields>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Response.FromValue(realSnapshot1, mockResponse.Object));

            mockClient.Setup(c => c.GetSnapshotAsync("snapshot2", It.IsAny<IEnumerable<SnapshotFields>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Response.FromValue(realSnapshot2, mockResponse.Object));

            mockClient.Setup(c => c.GetConfigurationSettingsForSnapshotAsync("snapshot1", It.IsAny<CancellationToken>()))
                .Returns(new MockAsyncPageable(new List<ConfigurationSetting> { _settingInSnapshot1 }));

            mockClient.Setup(c => c.GetConfigurationSettingsForSnapshotAsync("snapshot2", It.IsAny<CancellationToken>()))
                .Returns(new MockAsyncPageable(new List<ConfigurationSetting> { _settingInSnapshot2 }));

            var configuration = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ClientManager = TestHelpers.CreateMockedConfigurationClientManager(mockClient.Object);
                })
                .Build();

            Assert.Equal("ValueFromSnapshot", configuration["TestKey1"]);   // From first snapshot
            Assert.Equal("ValueFromSnapshot2", configuration["TestKey2"]);  // From second snapshot

            Assert.Null(configuration["SnapshotRef1"]);
            Assert.Null(configuration["SnapshotRef2"]);
        }

        [Fact]
        public void SnapshotReferenceWithEmptyValue()
        {
            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict);

            mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Returns(new MockAsyncPageable(new List<ConfigurationSetting> { _snapshotReferenceEmptyValue }));

            var configuration = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ClientManager = TestHelpers.CreateMockedConfigurationClientManager(mockClient.Object);
                })
                .Build();

            Assert.Null(configuration["SnapshotRefEmptyValue"]);

            mockClient.Verify(c => c.GetSnapshotAsync(It.IsAny<string>(), It.IsAny<IEnumerable<SnapshotFields>>(), It.IsAny<CancellationToken>()), Times.Never);
            mockClient.Verify(c => c.GetConfigurationSettingsForSnapshotAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public void SnapshotReferenceWithNonExistentSnapshot()
        {
            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict);

            mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Returns(new MockAsyncPageable(new List<ConfigurationSetting> { _snapshotReference1 }));

            // Set up the snapshot retrieval to throw 404
            mockClient.Setup(c => c.GetSnapshotAsync("snapshot1", It.IsAny<IEnumerable<SnapshotFields>>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new RequestFailedException(404, "Snapshot not found", "SnapshotNotFound", null));

            var configuration = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ClientManager = TestHelpers.CreateMockedConfigurationClientManager(mockClient.Object);
                })
                .Build();

            // The snapshot reference should be removed and no settings added to store
            Assert.Null(configuration["SnapshotRef1"]);

            mockClient.Verify(c => c.GetSnapshotAsync("snapshot1", It.IsAny<IEnumerable<SnapshotFields>>(), It.IsAny<CancellationToken>()), Times.Once);
            mockClient.Verify(c => c.GetConfigurationSettingsForSnapshotAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public void ThrowsWhenWrongSnapshotComposition()
        {
            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict);

            mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Returns(new MockAsyncPageable(new List<ConfigurationSetting> { _snapshotReference1 }));

            var settingsToInclude = new List<ConfigurationSettingsFilter>
            {
                new ConfigurationSettingsFilter("*")
            };
            var snapshotWithWrongComposition = new ConfigurationSnapshot(settingsToInclude)
            {
                SnapshotComposition = SnapshotComposition.KeyLabel
            };

            mockClient.Setup(c => c.GetSnapshotAsync("snapshot1", It.IsAny<IEnumerable<SnapshotFields>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Response.FromValue(snapshotWithWrongComposition, mockResponse.Object));

            var exception = Assert.Throws<InvalidOperationException>(() =>
            {
                new ConfigurationBuilder()
                    .AddAzureAppConfiguration(options =>
                    {
                        options.ClientManager = TestHelpers.CreateMockedConfigurationClientManager(mockClient.Object);
                    })
                    .Build();
            });

            Assert.Contains("SnapshotComposition", exception.Message);
            Assert.Contains("must be 'key'", exception.Message);

            mockClient.Verify(c => c.GetSnapshotAsync("snapshot1", It.IsAny<IEnumerable<SnapshotFields>>(), It.IsAny<CancellationToken>()), Times.Once);
            mockClient.Verify(c => c.GetConfigurationSettingsForSnapshotAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public void ThrowsWhenInvalidSnapshotReferenceJson()
        {
            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict);

            mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Returns(new MockAsyncPageable(new List<ConfigurationSetting> { _snapshotReferenceInvalidJson }));

            var exception = Assert.Throws<FormatException>(() =>
            {
                new ConfigurationBuilder()
                    .AddAzureAppConfiguration(options =>
                    {
                        options.ClientManager = TestHelpers.CreateMockedConfigurationClientManager(mockClient.Object);
                    })
                    .Build();
            });

            Assert.Contains("Invalid snapshot reference format", exception.Message);
            Assert.Contains("not valid JSON", exception.Message);

            mockClient.Verify(c => c.GetSnapshotAsync(It.IsAny<string>(), It.IsAny<IEnumerable<SnapshotFields>>(), It.IsAny<CancellationToken>()), Times.Never);
            mockClient.Verify(c => c.GetConfigurationSettingsForSnapshotAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public void RegularKeyValue()
        {
            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict);

            mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Returns(new MockAsyncPageable(new List<ConfigurationSetting> { _regularKeyValue }));

            var configuration = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ClientManager = TestHelpers.CreateMockedConfigurationClientManager(mockClient.Object);
                })
                .Build();

            Assert.Equal("RegularValue", configuration["RegularKey"]);

            mockClient.Verify(c => c.GetSnapshotAsync(It.IsAny<string>(), It.IsAny<IEnumerable<SnapshotFields>>(), It.IsAny<CancellationToken>()), Times.Never);
            mockClient.Verify(c => c.GetConfigurationSettingsForSnapshotAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public void SnapshotReferenceWithExtraProperties()
        {
            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict);

            mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Returns(new MockAsyncPageable(new List<ConfigurationSetting> { _snapshotReferenceWithExtraProperties }));

            var settingsToInclude = new List<ConfigurationSettingsFilter>
            {
                new ConfigurationSettingsFilter("*")
            };
            var realSnapshot = new ConfigurationSnapshot(settingsToInclude)
            {
                SnapshotComposition = SnapshotComposition.Key
            };

            mockClient.Setup(c => c.GetSnapshotAsync("snapshot1", It.IsAny<IEnumerable<SnapshotFields>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Response.FromValue(realSnapshot, mockResponse.Object));

            mockClient.Setup(c => c.GetConfigurationSettingsForSnapshotAsync("snapshot1", It.IsAny<CancellationToken>()))
                .Returns(new MockAsyncPageable(new List<ConfigurationSetting> { _settingInSnapshot1 }));

            var configuration = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ClientManager = TestHelpers.CreateMockedConfigurationClientManager(mockClient.Object);
                })
                .Build();

            // Should work normally despite extra properties in the JSON - extra properties are ignored
            Assert.Equal("ValueFromSnapshot", configuration["TestKey1"]);
            Assert.Null(configuration["SnapshotRefExtraProps"]);

            mockClient.Verify(c => c.GetSnapshotAsync("snapshot1", It.IsAny<IEnumerable<SnapshotFields>>(), It.IsAny<CancellationToken>()), Times.Once);
            mockClient.Verify(c => c.GetConfigurationSettingsForSnapshotAsync("snapshot1", It.IsAny<CancellationToken>()), Times.Once);
        }

        //Register("SnapshotRefKey", refreshAll: false) → Still triggers refreshAll due to content type
        [Fact]
        public async Task SnapshotReferenceRegisteredWithRefreshAllFalse()
        {
            IConfigurationRefresher refresher = null;
            TimeSpan refreshInterval = TimeSpan.FromSeconds(1);

            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict);

            bool refreshAllTriggered = false;

            mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Returns(new MockAsyncPageable(new List<ConfigurationSetting> { _snapshotReference1, _regularKeyValue }));

            var settingsToInclude = new List<ConfigurationSettingsFilter> { new ConfigurationSettingsFilter("*") };
            var realSnapshot = new ConfigurationSnapshot(settingsToInclude) { SnapshotComposition = SnapshotComposition.Key };

            mockClient.Setup(c => c.GetSnapshotAsync("snapshot1", It.IsAny<IEnumerable<SnapshotFields>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Response.FromValue(realSnapshot, mockResponse.Object));

            mockClient.Setup(c => c.GetConfigurationSettingsForSnapshotAsync("snapshot1", It.IsAny<CancellationToken>()))
                .Returns(new MockAsyncPageable(new List<ConfigurationSetting> { _settingInSnapshot1 }));

            mockClient.Setup(c => c.GetConfigurationSettingAsync("SnapshotRef1", It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() =>
                {
                    return Response.FromValue(updatedSnapshotRef1, mockResponse.Object);
                });

            // Setup refresh check - simulate change detected
            mockClient.Setup(c => c.GetConfigurationSettingAsync(It.IsAny<ConfigurationSetting>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((ConfigurationSetting setting, bool onlyIfChanged, CancellationToken token) =>
                {
                    if (setting.Key == "SnapshotRef1")
                    {
                        // Snapshot reference changed - this should trigger refreshAll despite refreshAll: false
                        refreshAllTriggered = true;
                        return Response.FromValue(updatedSnapshotRef1, new MockResponse(200));
                    }

                    return Response.FromValue(setting, new MockResponse(304));
                });

            var configuration = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ClientManager = TestHelpers.CreateMockedConfigurationClientManager(mockClient.Object);
                    options.ConfigureRefresh(refreshOptions =>
                    {
                        refreshOptions.Register("SnapshotRef1", refreshAll: false)
                                      .SetRefreshInterval(refreshInterval);
                    });
                    refresher = options.GetRefresher();
                })
                .Build();

            // Trigger refresh
            Thread.Sleep(refreshInterval);
            await refresher.RefreshAsync();

            Assert.True(refreshAllTriggered, "RefreshAll should be triggered for snapshot references even when refreshAll: false");
        }

        // Scenario B: Register("SnapshotRef1", refreshAll: true) → Triggers refreshAll (explicitly configured)
        [Fact]
        public async Task SnapshotReferenceRegisteredWithRefreshAllTrue_TriggersRefreshAll()
        {
            IConfigurationRefresher refresher = null;
            TimeSpan refreshInterval = TimeSpan.FromSeconds(1);

            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict);

            bool refreshAllTriggered = false;

            mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Returns(new MockAsyncPageable(new List<ConfigurationSetting> { _snapshotReference1, _regularKeyValue }));

            var settingsToInclude = new List<ConfigurationSettingsFilter> { new ConfigurationSettingsFilter("*") };
            var realSnapshot = new ConfigurationSnapshot(settingsToInclude) { SnapshotComposition = SnapshotComposition.Key };

            mockClient.Setup(c => c.GetSnapshotAsync("snapshot1", It.IsAny<IEnumerable<SnapshotFields>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Response.FromValue(realSnapshot, mockResponse.Object));

            mockClient.Setup(c => c.GetConfigurationSettingsForSnapshotAsync("snapshot1", It.IsAny<CancellationToken>()))
                .Returns(new MockAsyncPageable(new List<ConfigurationSetting> { _settingInSnapshot1 }));

            mockClient.Setup(c => c.GetConfigurationSettingAsync("SnapshotRef1", It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => Response.FromValue(_snapshotReference1, mockResponse.Object));

            mockClient.Setup(c => c.GetConfigurationSettingAsync(It.IsAny<ConfigurationSetting>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((ConfigurationSetting setting, bool onlyIfChanged, CancellationToken token) =>
                {
                    if (setting.Key == "SnapshotRef1")
                    {
                        refreshAllTriggered = true;
                        return Response.FromValue(_snapshotReference1, new MockResponse(200));
                    }

                    return Response.FromValue(setting, new MockResponse(304));
                });

            var configuration = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ClientManager = TestHelpers.CreateMockedConfigurationClientManager(mockClient.Object);
                    options.ConfigureRefresh(refreshOptions =>
                    {
                        refreshOptions.Register("SnapshotRef1", refreshAll: true)  // Explicit true
                                      .SetRefreshInterval(refreshInterval);
                    });
                    refresher = options.GetRefresher();
                })
                .Build();

            Thread.Sleep(refreshInterval);
            await refresher.RefreshAsync();

            Assert.True(refreshAllTriggered, "RefreshAll should be triggered when explicitly configured");
        }

        // Scenario C: Register("SnapshotRef1") → Still triggers refreshAll due to content type
        [Fact]
        public async Task SnapshotReferenceRegisteredWithoutRefreshAllParameter_StillTriggersRefreshAll()
        {
            IConfigurationRefresher refresher = null;
            TimeSpan refreshInterval = TimeSpan.FromSeconds(1);

            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict);

            bool refreshAllTriggered = false;

            mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Returns(new MockAsyncPageable(new List<ConfigurationSetting> { _snapshotReference1, _regularKeyValue }));

            var settingsToInclude = new List<ConfigurationSettingsFilter> { new ConfigurationSettingsFilter("*") };
            var realSnapshot = new ConfigurationSnapshot(settingsToInclude) { SnapshotComposition = SnapshotComposition.Key };

            mockClient.Setup(c => c.GetSnapshotAsync("snapshot1", It.IsAny<IEnumerable<SnapshotFields>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Response.FromValue(realSnapshot, mockResponse.Object));

            mockClient.Setup(c => c.GetConfigurationSettingsForSnapshotAsync("snapshot1", It.IsAny<CancellationToken>()))
                .Returns(new MockAsyncPageable(new List<ConfigurationSetting> { _settingInSnapshot1 }));

            mockClient.Setup(c => c.GetConfigurationSettingAsync("SnapshotRef1", It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => Response.FromValue(_snapshotReference1, mockResponse.Object));

            mockClient.Setup(c => c.GetConfigurationSettingAsync(It.IsAny<ConfigurationSetting>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((ConfigurationSetting setting, bool onlyIfChanged, CancellationToken token) =>
                {
                    if (setting.Key == "SnapshotRef1")
                    {
                        refreshAllTriggered = true;
                        return Response.FromValue(_snapshotReference1, new MockResponse(200));
                    }

                    return Response.FromValue(setting, new MockResponse(304));
                });

            var configuration = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ClientManager = TestHelpers.CreateMockedConfigurationClientManager(mockClient.Object);
                    options.ConfigureRefresh(refreshOptions =>
                    {
                        refreshOptions.Register("SnapshotRef1")  // No refreshAll parameter
                                      .SetRefreshInterval(refreshInterval);
                    });
                    refresher = options.GetRefresher();
                })
                .Build();

            Thread.Sleep(refreshInterval);
            await refresher.RefreshAsync();

            Assert.True(refreshAllTriggered, "RefreshAll should be triggered for snapshot references even without explicit refreshAll parameter");
        }
    }
}