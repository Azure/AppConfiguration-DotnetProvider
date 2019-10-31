using Microsoft.Extensions.Configuration.AzureAppConfiguration;
using System;
using System.IO;
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
    }
}
