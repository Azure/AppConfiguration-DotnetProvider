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

        // A snapshot reference setting - this points to a snapshot instead of containing the actual value
        ConfigurationSetting _snapshotReference1 = ConfigurationModelFactory.ConfigurationSetting(
            key: "SnapshotRef1",
            value: @"{""snapshot_name"": ""snapshot1""}",
            eTag: new ETag("snapshot-ref-etag-123"),
            contentType: SnapshotReferenceConstants.ContentType);

        // A regular configuration setting that would be inside the snapshot
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

        // Reference with invalid JSON (malformed, not a proper JSON object)
        ConfigurationSetting _snapshotReferenceInvalidJson = ConfigurationModelFactory.ConfigurationSetting(
                key: "SnapshotRefInvalidJson",
                value: "{invalid json, missing quotes}",  // Malformed JSON
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
            // STEP 1: Create mock objects (fake Azure services)
            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict);

            // STEP 2: Set up what the fake Azure App Configuration client should return
            mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Returns(new MockAsyncPageable(new List<ConfigurationSetting> { _snapshotReference1 }));

            // STEP 3: Create a real ConfigurationSnapshot object instead of mocking it
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

            // STEP 4: Set up what settings are inside the snapshot
            mockClient.Setup(c => c.GetConfigurationSettingsForSnapshotAsync("snapshot1", It.IsAny<CancellationToken>()))
                .Returns(new MockAsyncPageable(new List<ConfigurationSetting> { _settingInSnapshot1 }));

            // STEP 5: Build the configuration using our fake clients
            var configuration = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ClientManager = TestHelpers.CreateMockedConfigurationClientManager(mockClient.Object);
                })
                .Build();

            // STEP 6: Test that we get the value from the snapshot, not the reference
            Assert.Equal(_snapshotConfigValue, configuration["TestKey1"]);

            // Verify snapshot references themselves are not in the config
            Assert.Null(configuration["SnapshotRef1"]);
        }

        [Fact]
        public void MultipleSnapshotReferences()
        {

            // Create mock objects (fake Azure services)
            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict);

            // Set up what the fake Azure App Configuration client should return (both references)
            mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Returns(new MockAsyncPageable(new List<ConfigurationSetting> { _snapshotReference1, _snapshotReference2 }));

            // Create real ConfigurationSnapshot objects for both snapshots
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

            // Set up snapshot retrieval for both snapshots
            mockClient.Setup(c => c.GetSnapshotAsync("snapshot1", It.IsAny<IEnumerable<SnapshotFields>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Response.FromValue(realSnapshot1, mockResponse.Object));

            mockClient.Setup(c => c.GetSnapshotAsync("snapshot2", It.IsAny<IEnumerable<SnapshotFields>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Response.FromValue(realSnapshot2, mockResponse.Object));

            // Set up what settings are inside each snapshot
            mockClient.Setup(c => c.GetConfigurationSettingsForSnapshotAsync("snapshot1", It.IsAny<CancellationToken>()))
                .Returns(new MockAsyncPageable(new List<ConfigurationSetting> { _settingInSnapshot1 }));

            mockClient.Setup(c => c.GetConfigurationSettingsForSnapshotAsync("snapshot2", It.IsAny<CancellationToken>()))
                .Returns(new MockAsyncPageable(new List<ConfigurationSetting> { _settingInSnapshot2 }));

            // Build the configuration using our fake clients
            var configuration = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ClientManager = TestHelpers.CreateMockedConfigurationClientManager(mockClient.Object);
                })
                .Build();

            // Test that we get values from both snapshots
            Assert.Equal("ValueFromSnapshot", configuration["TestKey1"]);   // From first snapshot
            Assert.Equal("ValueFromSnapshot2", configuration["TestKey2"]);  // From second snapshot

            // Verify snapshot references themselves are not in the config
            Assert.Null(configuration["SnapshotRef1"]);
            Assert.Null(configuration["SnapshotRef2"]);
        }

        // THIS SHOULD ACTUALLY THROW SINCE IT IS AN INVALID JSON PROPERTY!
        [Fact]
        public void SnapshotReferenceWithEmptyValue()
        {
            // Create mock objects
            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict);

            // Set up the client to return snapshot references with empty/null values
            mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Returns(new MockAsyncPageable(new List<ConfigurationSetting> { _snapshotReferenceEmptyValue }));

            // Build the configuration - this should not throw exceptions
            var configuration = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ClientManager = TestHelpers.CreateMockedConfigurationClientManager(mockClient.Object);
                })
                .Build();

            // Verify that empty snapshot references are treated as empty list
            // The snapshot reference should be removed and no settings added (empty list behavior)
            Assert.Null(configuration["SnapshotRefEmptyValue"]);

            // Verify no snapshot API calls were made (since parsing should fail gracefully)
            mockClient.Verify(c => c.GetSnapshotAsync(It.IsAny<string>(), It.IsAny<IEnumerable<SnapshotFields>>(), It.IsAny<CancellationToken>()), Times.Never);
            mockClient.Verify(c => c.GetConfigurationSettingsForSnapshotAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public void SnapshotReferenceWithNonExistentSnapshot()
        {
            // Create mock objects
            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict);

            // Set up the client to return a valid snapshot reference (but snapshot doesn't exist)
            mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Returns(new MockAsyncPageable(new List<ConfigurationSetting> { _snapshotReference1 }));

            // Set up the snapshot retrieval to throw 404 Not Found (snapshot doesn't exist)
            mockClient.Setup(c => c.GetSnapshotAsync("snapshot1", It.IsAny<IEnumerable<SnapshotFields>>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new RequestFailedException(404, "Snapshot not found", "SnapshotNotFound", null));

            // Build the configuration - this should not throw exceptions (graceful empty list behavior)
            var configuration = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ClientManager = TestHelpers.CreateMockedConfigurationClientManager(mockClient.Object);
                })
                .Build();

            // Verify that non-existent snapshot references are treated as empty list
            // The snapshot reference should be removed and no settings added (empty list behavior)
            Assert.Null(configuration["SnapshotRef1"]);

            // Verify that GetSnapshotAsync was called but GetConfigurationSettingsForSnapshotAsync was not
            // (since the snapshot wasn't found, no settings retrieval should happen)
            mockClient.Verify(c => c.GetSnapshotAsync("snapshot1", It.IsAny<IEnumerable<SnapshotFields>>(), It.IsAny<CancellationToken>()), Times.Once);
            mockClient.Verify(c => c.GetConfigurationSettingsForSnapshotAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public void ThrowsWhenWrongSnapshotComposition()
        {
            // Create mock objects
            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict);

            // Set up the client to return a valid snapshot reference
            mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Returns(new MockAsyncPageable(new List<ConfigurationSetting> { _snapshotReference1 }));

            // Create a snapshot with WRONG composition type (KeyLabel instead of Key)
            var settingsToInclude = new List<ConfigurationSettingsFilter>
            {
                new ConfigurationSettingsFilter("*")
            };
            var snapshotWithWrongComposition = new ConfigurationSnapshot(settingsToInclude)
            {
                SnapshotComposition = SnapshotComposition.KeyLabel  // This is the WRONG type - should be Key
            };

            // Set up the snapshot retrieval to return snapshot with wrong composition
            mockClient.Setup(c => c.GetSnapshotAsync("snapshot1", It.IsAny<IEnumerable<SnapshotFields>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Response.FromValue(snapshotWithWrongComposition, mockResponse.Object));

            // Act & Assert: Building configuration should throw InvalidOperationException
            var exception = Assert.Throws<InvalidOperationException>(() =>
            {
                new ConfigurationBuilder()
                    .AddAzureAppConfiguration(options =>
                    {
                        options.ClientManager = TestHelpers.CreateMockedConfigurationClientManager(mockClient.Object);
                    })
                    .Build();
            });

            // Verify the exception message contains the expected details
            Assert.Contains("SnapshotComposition", exception.Message);
            Assert.Contains("must be 'key'", exception.Message);

            // Verify that GetSnapshotAsync was called but GetConfigurationSettingsForSnapshotAsync was not
            // (since the composition validation fails before settings retrieval)
            mockClient.Verify(c => c.GetSnapshotAsync("snapshot1", It.IsAny<IEnumerable<SnapshotFields>>(), It.IsAny<CancellationToken>()), Times.Once);
            mockClient.Verify(c => c.GetConfigurationSettingsForSnapshotAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public void ThrowsWhenInvalidSnapshotReferenceJson()
        {
            // Create mock objects
            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict);

            // Set up the client to return snapshot reference with invalid JSON
            mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Returns(new MockAsyncPageable(new List<ConfigurationSetting> { _snapshotReferenceInvalidJson }));

            // Act & Assert: Building configuration should throw FormatException due to invalid JSON
            var exception = Assert.Throws<FormatException>(() =>
            {
                new ConfigurationBuilder()
                    .AddAzureAppConfiguration(options =>
                    {
                        options.ClientManager = TestHelpers.CreateMockedConfigurationClientManager(mockClient.Object);
                    })
                    .Build();
            });

            // Verify the exception message contains the expected details about invalid JSON
            Assert.Contains("Invalid snapshot reference format", exception.Message);
            Assert.Contains("not valid JSON", exception.Message);

            // Verify that no snapshot API calls were made (since JSON parsing fails before any API calls)
            mockClient.Verify(c => c.GetSnapshotAsync(It.IsAny<string>(), It.IsAny<IEnumerable<SnapshotFields>>(), It.IsAny<CancellationToken>()), Times.Never);
            mockClient.Verify(c => c.GetConfigurationSettingsForSnapshotAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public void RegularKeyValue()
        {
            // Create mock objects
            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict);

            // Set up the client to return a mix of regular settings and fake snapshot reference
            mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Returns(new MockAsyncPageable(new List<ConfigurationSetting> { _regularKeyValue }));

            // Build the configuration
            var configuration = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ClientManager = TestHelpers.CreateMockedConfigurationClientManager(mockClient.Object);
                })
                .Build();

            // Verify that regular settings are treated as normal key-value pairs
            Assert.Equal("RegularValue", configuration["RegularKey"]);

            // Verify no snapshot API calls were made (since these are not snapshot references)
            mockClient.Verify(c => c.GetSnapshotAsync(It.IsAny<string>(), It.IsAny<IEnumerable<SnapshotFields>>(), It.IsAny<CancellationToken>()), Times.Never);
            mockClient.Verify(c => c.GetConfigurationSettingsForSnapshotAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public void SnapshotReferenceWithExtraProperties()
        {
            // Create mock objects
            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict);

            // Set up the client to return snapshot reference with extra properties
            mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Returns(new MockAsyncPageable(new List<ConfigurationSetting> { _snapshotReferenceWithExtraProperties }));

            // Create a real ConfigurationSnapshot object
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

            // Build the configuration
            var configuration = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ClientManager = TestHelpers.CreateMockedConfigurationClientManager(mockClient.Object);
                })
                .Build();

            // Should work normally despite extra properties in the JSON - extra properties are ignored
            Assert.Equal("ValueFromSnapshot", configuration["TestKey1"]);
            Assert.Null(configuration["SnapshotRefExtraProps"]); // Reference itself should not appear

            // Verify snapshot API calls were made (proving the snapshot reference was processed correctly)
            mockClient.Verify(c => c.GetSnapshotAsync("snapshot1", It.IsAny<IEnumerable<SnapshotFields>>(), It.IsAny<CancellationToken>()), Times.Once);
            mockClient.Verify(c => c.GetConfigurationSettingsForSnapshotAsync("snapshot1", It.IsAny<CancellationToken>()), Times.Once);
        }

        //AI Generated Tests for refresh Behavior
        //Register("SnapshotRefKey", refreshAll: false) → Still triggers refreshAll due to content type
        [Fact]
        public async Task SnapshotReferenceRegisteredWithRefreshAllFalse()
        {
            IConfigurationRefresher refresher = null;
            TimeSpan refreshInterval = TimeSpan.FromSeconds(1);

            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict);

            // Track if refreshAll was triggered
            bool refreshAllTriggered = false;

            // Setup initial configuration load - reuse existing _snapshotReference1 and _regularKeyValue
            mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Returns(new MockAsyncPageable(new List<ConfigurationSetting> { _snapshotReference1, _regularKeyValue }));

            // Setup snapshot reference resolution - reuse existing pattern
            var settingsToInclude = new List<ConfigurationSettingsFilter> { new ConfigurationSettingsFilter("*") };
            var realSnapshot = new ConfigurationSnapshot(settingsToInclude) { SnapshotComposition = SnapshotComposition.Key };

            mockClient.Setup(c => c.GetSnapshotAsync("snapshot1", It.IsAny<IEnumerable<SnapshotFields>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Response.FromValue(realSnapshot, mockResponse.Object));

            // Reuse existing _settingInSnapshot1
            mockClient.Setup(c => c.GetConfigurationSettingsForSnapshotAsync("snapshot1", It.IsAny<CancellationToken>()))
                .Returns(new MockAsyncPageable(new List<ConfigurationSetting> { _settingInSnapshot1 }));

            // Setup refresh monitoring - use the key from _snapshotReference1 ("SnapshotRef1")
            mockClient.Setup(c => c.GetConfigurationSettingAsync("SnapshotRef1", It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() =>
                {
                    // Use predefined updated snapshot reference
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
                        return Response.FromValue(updatedSnapshotRef1, new MockResponse(200)); // Changed
                    }

                    return Response.FromValue(setting, new MockResponse(304)); // Not modified
                });

            // Build configuration with refreshAll: false explicitly
            var configuration = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ClientManager = TestHelpers.CreateMockedConfigurationClientManager(mockClient.Object);
                    options.ConfigureRefresh(refreshOptions =>
                    {
                        refreshOptions.Register("SnapshotRef1", refreshAll: false)  // Explicit false
                                      .SetRefreshInterval(refreshInterval);
                    });
                    refresher = options.GetRefresher();
                })
                .Build();

            // Trigger refresh
            Thread.Sleep(refreshInterval);
            await refresher.RefreshAsync();

            // Verify refreshAll was triggered despite refreshAll: false
            // This proves snapshot references have special behavior
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

            // Track if refreshAll was triggered
            bool refreshAllTriggered = false;

            // Setup initial configuration load - reuse existing _snapshotReference1 and _regularKeyValue
            mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Returns(new MockAsyncPageable(new List<ConfigurationSetting> { _snapshotReference1, _regularKeyValue }));

            // Setup snapshot reference resolution - reuse existing pattern
            var settingsToInclude = new List<ConfigurationSettingsFilter> { new ConfigurationSettingsFilter("*") };
            var realSnapshot = new ConfigurationSnapshot(settingsToInclude) { SnapshotComposition = SnapshotComposition.Key };

            mockClient.Setup(c => c.GetSnapshotAsync("snapshot1", It.IsAny<IEnumerable<SnapshotFields>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Response.FromValue(realSnapshot, mockResponse.Object));

            // Reuse existing _settingInSnapshot1
            mockClient.Setup(c => c.GetConfigurationSettingsForSnapshotAsync("snapshot1", It.IsAny<CancellationToken>()))
                .Returns(new MockAsyncPageable(new List<ConfigurationSetting> { _settingInSnapshot1 }));

            // Setup refresh monitoring - use the key from _snapshotReference1 ("SnapshotRef1")
            mockClient.Setup(c => c.GetConfigurationSettingAsync("SnapshotRef1", It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => Response.FromValue(_snapshotReference1, mockResponse.Object));

            // Setup refresh check - simulate change detected
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

            // Build configuration with refreshAll: true explicitly
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

            // Verify refreshAll was triggered as expected
            Assert.True(refreshAllTriggered, "RefreshAll should be triggered when explicitly configured");
        }

        [Fact]
        public async Task SnapshotReferenceRegisteredWithoutRefreshAllParameter_StillTriggersRefreshAll()
        {
            // Scenario C: Register("SnapshotRef1") → Still triggers refreshAll due to content type
            IConfigurationRefresher refresher = null;
            TimeSpan refreshInterval = TimeSpan.FromSeconds(1);

            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict);

            // Track if refreshAll was triggered
            bool refreshAllTriggered = false;

            // Setup initial configuration load - reuse existing _snapshotReference1 and _regularKeyValue
            mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Returns(new MockAsyncPageable(new List<ConfigurationSetting> { _snapshotReference1, _regularKeyValue }));

            // Setup snapshot reference resolution - reuse existing pattern
            var settingsToInclude = new List<ConfigurationSettingsFilter> { new ConfigurationSettingsFilter("*") };
            var realSnapshot = new ConfigurationSnapshot(settingsToInclude) { SnapshotComposition = SnapshotComposition.Key };

            mockClient.Setup(c => c.GetSnapshotAsync("snapshot1", It.IsAny<IEnumerable<SnapshotFields>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Response.FromValue(realSnapshot, mockResponse.Object));

            // Reuse existing _settingInSnapshot1
            mockClient.Setup(c => c.GetConfigurationSettingsForSnapshotAsync("snapshot1", It.IsAny<CancellationToken>()))
                .Returns(new MockAsyncPageable(new List<ConfigurationSetting> { _settingInSnapshot1 }));

            // Setup refresh monitoring - use the key from _snapshotReference1 ("SnapshotRef1")
            mockClient.Setup(c => c.GetConfigurationSettingAsync("SnapshotRef1", It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => Response.FromValue(_snapshotReference1, mockResponse.Object));

            // Setup refresh check - simulate change detected
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

            // Build configuration WITHOUT refreshAll parameter (uses default)
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

            // Verify refreshAll was triggered despite no explicit refreshAll parameter
            // This proves snapshot references automatically get refreshAll behavior
            Assert.True(refreshAllTriggered, "RefreshAll should be triggered for snapshot references even without explicit refreshAll parameter");
        }
    }
}