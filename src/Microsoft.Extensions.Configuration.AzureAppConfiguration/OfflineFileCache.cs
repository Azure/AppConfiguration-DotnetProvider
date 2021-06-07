// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration.AzureAppConfiguration.Extensions;
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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

        /// <summary>
        /// Key name for cached data
        /// </summary>
        private const string dataProp = "d";

        /// <summary>
        /// Key name for cached data scope
        /// </summary>
        private const string scopeProp = "s";

        /// <summary>
        /// An opaque token representing a query for Azure App Configuration data.
        /// The token is used to test if cached data matches the request being issued to Azure App Configuration.
        /// </summary>
        private string _scopeToken;

        private ITimeLimitedDataProtector _timeLimitedDataProtector = null;

        private OfflineFileCacheOptions _options = null;

        /// <summary>
        /// A cache used for storing Azure App Configuration data using the file system.
        /// Supports encryption of the stored data using an instance of <see cref="IDataProtector"/>.
        /// </summary>
        /// <param name="options">
        /// Options dictating the behavior of the offline cache.
        /// <see cref="OfflineFileCacheOptions.Path"/> and <see cref="OfflineFileCacheOptions.FileCacheExpiration"/> are required.  
        /// </param>
        public OfflineFileCache(OfflineFileCacheOptions options)
        {
            ValidateFileCacheExpiration(options.FileCacheExpiration);
            ValidateCachePath(options.Path);
            _localCachePath = options.Path;

            if (options.DataProtector == null)
            {
                string appName = System.Diagnostics.Process.GetCurrentProcess().ProcessName;
                IDataProtectionProvider dataProtectionProvider = DataProtectionProvider.Create(appName);
                options.DataProtector = dataProtectionProvider.CreateProtector("AppConfigurationOfflineFileCacheProtector");
            }

            _timeLimitedDataProtector = options.DataProtector.ToTimeLimitedDataProtector();
            _options = options;
        }

        /// <summary>
        /// An implementation of <see cref="IOfflineCache.Import(AzureAppConfigurationOptions)"/> that retrieves the cached data from the file system.
        /// </summary>
        public string Import(AzureAppConfigurationOptions options)
        {
            CreateScopeToken(options);

            int retry = 0;
            while (retry++ <= retryMax)
            {
                try
                {
                    string data = null, scope = null;
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

                                case scopeProp:
                                    scope = reader.ReadAsString();
                                    break;

                                default:
                                    return null;
                            }
                        }
                    }

                    if ((data != null) && (scope != null) && (_scopeToken == scope))
                    {
                        return _timeLimitedDataProtector.Unprotect(data);
                    }
                }
                catch (IOException ex) when (ex.HResult == ERROR_SHARING_VIOLATION)
                {
                    Task.Delay(new Random().Next(delayRange)).ConfigureAwait(false).GetAwaiter().GetResult();
                }
                catch (CryptographicException)
                {
                    // TBD: This should be handled in AzureAppConfigurationProvider class.
                    // This exception means that one of the following occured:
                    //  - the encryption key is invalid/inaccessible;
                    //  - encrypted data has been tampered with;
                    //  - data has expired;
                    //  - any other internal error during decryption.
                    throw;
                }
            }

            return null;
        }


        /// <summary>
        /// An implementation of <see cref="IOfflineCache.Export(AzureAppConfigurationOptions, string)"/> that caches the data in the file system.
        /// </summary>
        public void Export(AzureAppConfigurationOptions options, string data)
        {
            CreateScopeToken(options);

            if ((DateTime.Now - File.GetLastWriteTime(_localCachePath)) > TimeSpan.FromMilliseconds(1000))
            {
                Task.Run(async () =>
                {
                    string tempFile = Path.Combine(Path.GetDirectoryName(_localCachePath), $"azconfigTemp-{Path.GetRandomFileName()}");

                    var encryptedData = _timeLimitedDataProtector.Protect(data, DateTimeOffset.UtcNow + _options.FileCacheExpiration);

                    using (var fileStream = new FileStream(tempFile, FileMode.Create))
                    {
                        using (var memoryStream = new MemoryStream())
                        {
                            using (var writer = new Utf8JsonWriter(memoryStream))
                            {
                                writer.WriteStartObject();
                                writer.WriteString(dataProp, encryptedData);
                                writer.WriteString(scopeProp, _scopeToken);
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

        private void CreateScopeToken(AzureAppConfigurationOptions azconfigOptions)
        {
            if (azconfigOptions == null)
            {
                throw new ArgumentNullException(nameof(azconfigOptions));
            }

            if (string.IsNullOrEmpty(_scopeToken))
            {
                // The default scope token is the configuration store endpoint combined with all of the key-value filters
                string endpoint = (azconfigOptions.Endpoint != null)
                                  ? azconfigOptions.Endpoint.ToString() 
                                  : ConnectionStringParser.Parse(azconfigOptions.ConnectionString, "Endpoint");
                
                if (string.IsNullOrWhiteSpace(endpoint))
                {
                    throw new InvalidOperationException("Invalid App Configuration endpoint.");
                }

                var sb = new StringBuilder($"{endpoint}\0");

                foreach (var selector in azconfigOptions.KeyValueSelectors)
                {
                    sb.Append($"{selector.KeyFilter}\0{selector.LabelFilter}\0");
                }

                _scopeToken = sb.ToString();
            }
        }

        internal static void ValidateCachePath(string path)
        {
            if (path == null)
            {
                throw new ArgumentNullException($"{nameof(OfflineFileCacheOptions)}.{nameof(OfflineFileCacheOptions.Path)}", "Please provide the path for storing offline file cache.");
            }

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

        internal static void ValidateFileCacheExpiration(TimeSpan expiration)
        {
            if (expiration <= TimeSpan.Zero)
            {
                throw new ArgumentException($"Please provide a valid {nameof(OfflineFileCacheOptions)}.{nameof(OfflineFileCacheOptions.FileCacheExpiration)}.");
            }

            try
            {
                _ = DateTimeOffset.UtcNow + expiration;
            }
            catch (ArgumentOutOfRangeException)
            {
                throw new ArgumentOutOfRangeException($"{nameof(OfflineFileCacheOptions)}.{nameof(OfflineFileCacheOptions.FileCacheExpiration)}", "Please provide a shorter file cache expiration. The expiration time added to the current UTC time results in an un-representable DateTime.");
            }
        }
    }
}
