using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace Microsoft.Extensions.Configuration.Azconfig
{
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
        private const string scoprProp = "s";

        private OfflineFileCacheOptions _options = null;

        public OfflineFileCache(OfflineFileCacheOptions options = null)
        {
            if (options != null)
            {
                _localCachePath = options.Path ?? throw new ArgumentNullException(nameof(options.Path));
                if (!Path.IsPathRooted(_localCachePath) || !string.Equals(Path.GetFullPath(_localCachePath), _localCachePath))
                {
                    throw new ArgumentException("Must be full path", nameof(options.Path));
                }

                if ((options.Key == null) || (options.IV == null) || (options.SignKey == null))
                {
                    throw new ArgumentException("All crypto keys must be set");
                }
            }

            _options = options;
        }

        public string Import(AzconfigOptions options)
        {
            EnsureOptions(options);

            int retry = 0;
            while (retry++ <= retryMax)
            {
                try
                {
                    string json = File.ReadAllText(_localCachePath);

                    JsonTextReader reader = new JsonTextReader(new StringReader(json));
                    string data = null, dataHash = null, scopeHash = null;
                    while (reader.Read())
                    {
                        if ((reader.TokenType == JsonToken.PropertyName) && (reader.Value != null))
                        {
                            switch (reader.Value.ToString())
                            {
                                case dataProp:
                                    data = reader.ReadAsString();
                                    break;
                                case hashProp:
                                    dataHash = reader.ReadAsString();
                                    break;
                                case scoprProp:
                                    scopeHash = reader.ReadAsString();
                                    break;
                                default:
                                    return null;
                            }
                        }
                    }

                    if ((data != null) && (dataHash != null) && (scopeHash != null))
                    {
                        string newScopeHash = CryptoService.GetHash(Encoding.UTF8.GetBytes(_options.ScopeToken ?? ""), _options.SignKey);
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
                    Task.Delay(new Random().Next(delayRange)).Wait();
                }
            }

            return null;
        }

        public void Export(AzconfigOptions options, string data)
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
                    var scopeHash = CryptoService.GetHash(Encoding.UTF8.GetBytes(_options.ScopeToken ?? ""), _options.SignKey);

                    StringBuilder sb = new StringBuilder();
                    using (var sw = new StringWriter(sb))
                    using (var jtw = new JsonTextWriter(sw))
                    {
                        jtw.WriteStartObject();
                        jtw.WritePropertyName(dataProp);
                        jtw.WriteValue(Convert.ToBase64String(encryptedBytes));
                        jtw.WritePropertyName(hashProp);
                        jtw.WriteValue(dataHash);
                        jtw.WritePropertyName(scoprProp);
                        jtw.WriteValue(scopeHash);
                        jtw.WriteEndObject();
                    }

                    File.WriteAllText(tempFile, sb.ToString());

                    await this.DoUpdate(tempFile);
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
                        await Task.Delay(new Random().Next(delayRange));
                        await this.DoUpdate(tempFile, ++retry);
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

        private void EnsureOptions(AzconfigOptions azconfigOptions)
        {
            if (_options != null)
            {
                return;
            }

            if (azconfigOptions == null)
            {
                throw new ArgumentNullException(nameof(azconfigOptions));
            }

            if (azconfigOptions.ConnectionString == null)
            {
                throw new InvalidOperationException("Please call Connect first.");
            }

            _options = new OfflineFileCacheOptions();

            byte[] secret = Convert.FromBase64String(Utility.ParseConnectionString(azconfigOptions.ConnectionString, "Secret"));

            // for AES 256 the block size must be 128 bits (16 bytes)
            if (secret.Length < 16)
            {
                throw new InvalidOperationException("Invalid connection string length.");
            }

            _options.Key = secret;
            _options.SignKey = secret;
            _options.IV = new byte[16];
            Array.Copy(secret, _options.IV, 16);

            if (_options.ScopeToken == null)
            {
                // Default would be Endpoint and KeyValueSelectors
                string endpoint = Utility.ParseConnectionString(azconfigOptions.ConnectionString, "Endpoint");
                if (string.IsNullOrWhiteSpace(endpoint))
                {
                    throw new InvalidOperationException("Invalid connection string format.");
                }

                var sb = new StringBuilder($"{endpoint}\0");
                azconfigOptions.KeyValueSelectors.ForEach(selector => {
                    sb.Append($"{selector.KeyFilter}\0{selector.LabelFilter}\0{selector.PreferredDateTime.GetValueOrDefault().ToUnixTimeSeconds()}\0");
                });

                _options.ScopeToken = sb.ToString();
            }

            // While user didn't specific the cache path, we will try to use the default path if it's running inside App Service
            if (_options.Path == null)
            {
                // Generate default cahce file name under $home/data/azconfigCache/app{instance}-{hash}.json
                string homePath = Environment.GetEnvironmentVariable("HOME");
                if (Directory.Exists(homePath))
                {
                    string dataPath = Path.Combine(homePath, "data");
                    if (Directory.Exists(dataPath))
                    {
                        string cahcePath = Path.Combine(dataPath, "azconfigCache");
                        if (!Directory.Exists(cahcePath))
                        {
                            Directory.CreateDirectory(cahcePath);
                        }

                        string websiteName = Environment.GetEnvironmentVariable("WEBSITE_SITE_NAME");
                        if (websiteName != null)
                        {
                            byte[] hash = new byte[0];
                            using (var sha = SHA1.Create())
                            {
                                hash = sha.ComputeHash(Encoding.UTF8.GetBytes(websiteName));
                            }

                            // The instance count would help preventing multiple provider overwrite each other's cache file
                            Interlocked.Increment(ref instance);
                            _options.Path = Path.Combine(cahcePath, $"app{instance}-{BitConverter.ToString(hash).Replace("-", String.Empty)}.json");
                        }
                    }
                }

                if (_options.Path == null)
                {
                    throw new NotSupportedException("The application must be running inside of an Azure App Service to use this feature.");
                }
            }
        }
    }
}
