﻿using Microsoft.Extensions.Configuration.AzureAppConfiguration.Extensions;
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    /// <summary>
    /// An offline cache provider for Azure App Configuration that uses the file system to store data.
    /// </summary>
    public class OfflineFileCache : IOfflineCache
    {
        private string _localCachePath = null;
        private const int ERROR_SHARING_VIOLATION = unchecked((int)0x80070020);
        private const int retryMax = 20;
        private const int delayRange = 50;
        private static int instance = 0;

        /// <summary>
        /// Key name for cached data
        /// </summary>
        private const string dataProp = "d";

        /// <summary>
        /// Key name for signature
        /// </summary>
        private const string hashProp = "h";

        /// <summary>
        /// Key name for cached data scope
        /// </summary>
        private const string scopeProp = "s";

        /// <summary>
        /// An opaque token representing a query for Azure App Configuration data.
        /// The token is used to test if cached data matches the request being issued to Azure App Configuration.
        /// </summary>
        private string _scopeToken;

        private OfflineFileCacheOptions _options = null;

        /// <summary>
        /// A cache used for storing Azure App Configuration data using the file system.
        /// Supports encryption of the stored data.
        /// </summary>
        /// <param name="options">
        /// Options dictating the behavior of the offline cache.
        /// If the options are null or the encryption keys are omitted, they will be derived from the store's connection string.
        /// <see cref="OfflineFileCacheOptions.Path"/> is required unless the application is running inside of an Azure App Service instance, in which case it can be populated automatically.
        /// </param>
        public OfflineFileCache(OfflineFileCacheOptions options = null)
        {
            OfflineFileCacheOptions opts = options ?? new OfflineFileCacheOptions();

            // If the user does not specify the cache path, we will try to use the default path
            // For the moment, default path is only supported when running inside Azure App Service
            if (opts.Path == null)
            {
                // Generate default cache file name under $home/data/azureAppConfigCache/app{instance}-{hash}.json
                string homePath = Environment.GetEnvironmentVariable("HOME");
                if (Directory.Exists(homePath))
                {
                    string dataPath = Path.Combine(homePath, "data");
                    if (Directory.Exists(dataPath))
                    {
                        string cachePath = Path.Combine(dataPath, "azureAppConfigCache");
                        if (!Directory.Exists(cachePath))
                        {
                            Directory.CreateDirectory(cachePath);
                        }

                        string websiteName = Environment.GetEnvironmentVariable("WEBSITE_SITE_NAME");
                        if (websiteName != null)
                        {
                            byte[] hash = new byte[0];
                            using (var sha = SHA1.Create())
                            {
                                hash = sha.ComputeHash(Encoding.UTF8.GetBytes(websiteName));
                            }

                            // The instance count will help prevent multiple providers from overwriting each other's cache file
                            Interlocked.Increment(ref instance);
                            opts.Path = Path.Combine(cachePath, $"app{instance}-{BitConverter.ToString(hash).Replace("-", String.Empty)}.json");
                        }
                    }
                }

                if (opts.Path == null)
                {
                    throw new ArgumentNullException($"{nameof(OfflineFileCacheOptions)}.{nameof(OfflineFileCacheOptions.Path)}", "Default cache path is only supported when running inside of an Azure App Service.");
                }
            }

            ValidateCachePath(opts.Path);
            _localCachePath = opts.Path;
            _options = opts;
        }

        /// <summary>
        /// An implementation of <see cref="IOfflineCache.Import(AzureAppConfigurationOptions)"/> that retrieves the cached data from the file system.
        /// </summary>
        public string Import(AzureAppConfigurationOptions options)
        {
            EnsureOptions(options);

            int retry = 0;
            while (retry++ <= retryMax)
            {
                try
                {
                    string data = null, dataHash = null, scopeHash = null;
                    byte[] bytes = File.ReadAllBytes(_localCachePath);
                    var reader = new Utf8JsonReader(bytes);

                    while (reader.Read())
                    {
                        if (reader.TokenType == JsonTokenType.PropertyName)
                        {
                            switch (reader.GetString())
                            {
                                case dataProp:
                                    data = reader.ReadAsString();
                                    break;

                                case hashProp:
                                    dataHash = reader.ReadAsString();
                                    break;

                                case scopeProp:
                                    scopeHash = reader.ReadAsString();
                                    break;

                                default:
                                    return null;
                            }
                        }
                    }

                    if ((data != null) && (dataHash != null) && (scopeHash != null))
                    {
                        string newScopeHash = CryptoService.GetHash(Encoding.UTF8.GetBytes(_scopeToken), _options.SignKey);
                        if (string.CompareOrdinal(scopeHash, newScopeHash) == 0)
                        {
                            string newDataHash = CryptoService.GetHash(Convert.FromBase64String(data), _options.SignKey);
                            if (string.CompareOrdinal(dataHash, newDataHash) == 0)
                            {
                                return CryptoService.AESDecrypt(data, _options.Key, _options.IV);
                            }
                        }
                    }
                }
                catch (IOException ex) when (ex.HResult == ERROR_SHARING_VIOLATION)
                {
                    Task.Delay(new Random().Next(delayRange)).ConfigureAwait(false).GetAwaiter().GetResult();
                }
            }

            return null;
        }


        /// <summary>
        /// An implementation of <see cref="IOfflineCache.Export(AzureAppConfigurationOptions, string)"/> that caches the data in the file system.
        /// </summary>
        public void Export(AzureAppConfigurationOptions options, string data)
        {
            EnsureOptions(options);

            if ((DateTime.Now - File.GetLastWriteTime(_localCachePath)) > TimeSpan.FromMilliseconds(1000))
            {
                Task.Run(async () =>
                {
                    string tempFile = Path.Combine(Path.GetDirectoryName(_localCachePath), $"azconfigTemp-{Path.GetRandomFileName()}");

                    var dataBytes = Encoding.UTF8.GetBytes(data);
                    var encryptedBytes = CryptoService.AESEncrypt(dataBytes, _options.Key, _options.IV);
                    var dataHash = CryptoService.GetHash(encryptedBytes, _options.SignKey);
                    var scopeHash = CryptoService.GetHash(Encoding.UTF8.GetBytes(_scopeToken), _options.SignKey);

                    using (var fileStream = new FileStream(tempFile, FileMode.Create))
                    {
                        using (var memoryStream = new MemoryStream())
                        {
                            using (var writer = new Utf8JsonWriter(memoryStream))
                            {
                                writer.WriteStartObject();
                                writer.WriteString(dataProp, Convert.ToBase64String(encryptedBytes));
                                writer.WriteString(hashProp, dataHash);
                                writer.WriteString(scopeProp, scopeHash);
                                writer.WriteEndObject();
                            }

                            memoryStream.Position = 0;
                            memoryStream.CopyTo(fileStream);
                            fileStream.Flush();
                        }
                    }

                    await this.DoUpdate(tempFile).ConfigureAwait(false);
                });
            }
        }

        private async Task DoUpdate(string tempFile, int retry = 0)
        {
            if (retry <= retryMax)
            {
                try
                {
                    File.Delete(_localCachePath);
                    File.Move(tempFile, _localCachePath);
                }
                catch (IOException ex)
                {
                    if (ex.HResult == ERROR_SHARING_VIOLATION)
                    {
                        await Task.Delay(new Random().Next(delayRange)).ConfigureAwait(false);
                        await this.DoUpdate(tempFile, ++retry).ConfigureAwait(false);
                    }
                    else
                    {
                        throw;
                    }
                }
            }
            else
            {
                File.Delete(tempFile);
            }
        }

        private void EnsureOptions(AzureAppConfigurationOptions azconfigOptions)
        {
            if (azconfigOptions == null)
            {
                throw new ArgumentNullException(nameof(azconfigOptions));
            }

            if (azconfigOptions.ConnectionString == null)
            {
                throw new InvalidOperationException("An Azure App Configuration connection string is required.");
            }

            OfflineFileCacheOptions options = _options ?? new OfflineFileCacheOptions();

            if ((options.Key == null) || (options.SignKey == null) || (options.IV == null))
            {
                byte[] secret = Convert.FromBase64String(ConnectionStringParser.Parse(azconfigOptions.ConnectionString, "Secret"));
                using (SHA256 sha256 = SHA256.Create())
                {
                    byte[] hash = sha256.ComputeHash(secret);

                    options.Key = options.Key ?? hash;
                    options.SignKey = options.SignKey ?? hash;
                    options.IV = options.IV ?? hash.Take(16).ToArray();
                }
            }

            if (string.IsNullOrEmpty(_scopeToken))
            {
                //
                // The default scope token is the configuration store endpoint combined with all of the key-value filters

                string endpoint = ConnectionStringParser.Parse(azconfigOptions.ConnectionString, "Endpoint");

                if (string.IsNullOrWhiteSpace(endpoint))
                {
                    throw new InvalidOperationException("Invalid connection string format.");
                }

                var sb = new StringBuilder($"{endpoint}\0");

                foreach (var selector in azconfigOptions.KeyValueSelectors)
                {
                    sb.Append($"{selector.KeyFilter}\0{selector.LabelFilter}\0");
                }

                _scopeToken = sb.ToString();
            }

            _options = options;
        }

        internal static void ValidateCachePath(string path)
        {
            if (!Path.IsPathRooted(path) || !string.Equals(Path.GetFullPath(path), path) || string.IsNullOrWhiteSpace(Path.GetFileName(path)))
            {
                throw new ArgumentException($"The path {path} is not a full file path.");
            }

            if (Directory.Exists(path))
            {
                throw new ArgumentException($"The path {path} corresponds to an existing directory and cannot be used as file path.");
            }

            string directoryPath = Path.GetDirectoryName(path);

            if (!Directory.Exists(directoryPath))
            {
                throw new ArgumentException($"The directory with path {directoryPath} does not exist.");
            }
        }
    }
}
