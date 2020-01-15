using Azure.Data.AppConfiguration;
using Microsoft.Extensions.Configuration.AzureAppConfiguration;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
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
        public void OfflineCacheTests_Import()
        {
            // Arrange
            var options = new AzureAppConfigurationOptions();
            options.Connect($"Endpoint=https://dotnetprovider-test.azconfig.io;Id=b1d9b31;Secret=c2VjcmV0");
            options.Select("AppName");

            var offlineCache = new OfflineFileCache(new OfflineFileCacheOptions
            {
                Path = Path.Combine(Directory.GetCurrentDirectory(), "cache.json")
            });

            // Act
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
