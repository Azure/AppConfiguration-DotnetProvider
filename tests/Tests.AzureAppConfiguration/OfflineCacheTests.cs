// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Azure.Data.AppConfiguration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.AzureAppConfiguration;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using Xunit;

namespace Tests.AzureAppConfiguration
{
    public class OfflineCacheTests
    {
        [Fact]
        public void OfflineCacheTests_ValidateCacheFilePath_ThrowsIfPathIsInvalid()
        {
            // Arrange
            string currentDirectory = Directory.GetCurrentDirectory();

            var paths = new string[]
            {
                $@"NonExistentDir\cache.json",                    // Path is not rooted
                $@"{currentDirectory}",                           // Path is an existing directory
                $@"{currentDirectory}\NonExistentDir\",           // Path is an intentional directory path
                $@"{currentDirectory}\NonExistentDir\cache.json"  // Directory for the path does not exist
            };

            // Act and Assert
            foreach (var path in paths)
            {
                Assert.Throws<ArgumentException>(() => OfflineFileCache.ValidateCachePath(path));
            }
        }

        [Fact]
        public void OfflineCacheTests_ValidateCacheFilePath_DoesNotThrowIfPathIsValid()
        {
            // Arrange
            string currentDirectory = Directory.GetCurrentDirectory();
            Directory.CreateDirectory(Path.Combine(currentDirectory, "CacheDir"));

            var paths = new string[]
            {
                $@"{currentDirectory}\cache",
                $@"{currentDirectory}\cache.json",
                $@"{currentDirectory}\CacheDir\cache.json"
            };

            // Act and Assert
            foreach (var path in paths)
            {
                OfflineFileCache.ValidateCachePath(path);   // Validate no exception is thrown
            }
        }

        [Fact]
        public void OfflineCacheTests_ThrowsIfPathIsMissing()
        {
            var connectionString = TestHelpers.CreateMockEndpointString();
            var builder = new ConfigurationBuilder();
            var exception = Record.Exception(() =>
            {
                builder.AddAzureAppConfiguration(options =>
                {
                    options.Connect(connectionString)
                    .Select("AppName")
                    .SetOfflineCache(new OfflineFileCache(new OfflineFileCacheOptions
                    {
                        FileCacheExpiration = TimeSpan.FromDays(1)
                    }));
                });
                builder.Build();
            });
            Assert.NotNull(exception);
            Assert.IsType<ArgumentNullException>(exception);
        }

        [Fact]
        public void OfflineCacheTests_ThrowsIfFileCacheExpirationIsMissing()
        {
            var connectionString = TestHelpers.CreateMockEndpointString();
            var builder = new ConfigurationBuilder();
            var exception = Record.Exception(() =>
            {
                builder.AddAzureAppConfiguration(options =>
                {
                    options.Connect(connectionString)
                    .Select("AppName")
                    .SetOfflineCache(new OfflineFileCache(new OfflineFileCacheOptions
                    {
                        Path = Path.Combine(Directory.GetCurrentDirectory(), "cache.json")
                    }));
                });
                builder.Build();
            });
            Assert.NotNull(exception);
            Assert.IsType<ArgumentException>(exception);
        }

        [Fact]
        public void OfflineCacheTests_ThrowsIfFileCacheExpirationIsTooHigh()
        {
            var connectionString = TestHelpers.CreateMockEndpointString();
            var builder = new ConfigurationBuilder();
            var exception = Record.Exception(() =>
            {
                builder.AddAzureAppConfiguration(options =>
                {
                    options.Connect(connectionString)
                    .Select("AppName")
                    .SetOfflineCache(new OfflineFileCache(new OfflineFileCacheOptions
                    {
                        Path = Path.Combine(Directory.GetCurrentDirectory(), "cache.json"),
                        FileCacheExpiration = TimeSpan.MaxValue
                    }));
                });
                builder.Build();
            });
            Assert.NotNull(exception);
            Assert.IsType<ArgumentOutOfRangeException>(exception);
        }

        [Fact]
        public void OfflineCacheTests_ExportAndImport()
        {
            // Arrange
            var options = new AzureAppConfigurationOptions();
            options.Connect(TestHelpers.CreateMockEndpointString());
            options.Select("AppName");

            var offlineCache = new OfflineFileCache(new OfflineFileCacheOptions
            {
                Path = Path.Combine(Directory.GetCurrentDirectory(), "cache.json"),
                FileCacheExpiration = TimeSpan.FromDays(1)
            });

            IDictionary<string, ConfigurationSetting> mockData = new Dictionary<string, ConfigurationSetting>();
            mockData["AppName"] = new ConfigurationSetting(key: "AppName", value: "Azure App Configuration");

            // Act
            offlineCache.Export(options, JsonSerializer.Serialize(mockData));

            // Wait for file export to complete before importing from same file
            Thread.Sleep(1000);

            var result = offlineCache.Import(options);

            // Assert
            Assert.NotNull(result);

            var settings = JsonSerializer.Deserialize<IDictionary<string, ConfigurationSetting>>(result);
            Assert.Equal(1, settings.Count);

            var setting = settings.Single();
            Assert.Equal("AppName", setting.Key);
            Assert.NotNull(setting.Value);
            Assert.Equal("Azure App Configuration", setting.Value.Value);
        }
    }
}
