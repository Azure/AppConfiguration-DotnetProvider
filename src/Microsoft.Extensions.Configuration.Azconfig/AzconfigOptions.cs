﻿namespace Microsoft.Extensions.Configuration.Azconfig
{
    using Microsoft.Azconfig.Client;
    using Microsoft.Azconfig.ManagedIdentityConnector;
    using Microsoft.Extensions.Configuration.Azconfig.Models;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Security.Cryptography;
    using System.Text;
    using System.Threading;

    public class AzconfigOptions
    {
        private static int _instance = 0;
        private Dictionary<string, KeyValueWatcher> _changeWatchers = new Dictionary<string, KeyValueWatcher>();

        private List<KeyValueSelector> _kvSelectors = new List<KeyValueSelector>();

        public IEnumerable<KeyValueSelector> KeyValueSelectors => _kvSelectors;

        public OfflineCache OfflineCache { get; set; }

        /// <summary>
        /// The connection string to use to connect to the configuration store.
        /// </summary>
        public string ConnectionString { get; set; }

        /// <summary>
        /// An optional client that can be used to communicate with the configuration store. If provided, connection string will be ignored.
        /// </summary>
        public AzconfigClient Client { get; set; }

        public IEnumerable<KeyValueWatcher> ChangeWatchers {
            get
            {
                return _changeWatchers.Values;
            }
        }

        public AzconfigOptions Watch(string key, int pollInterval, string label = "")
        {
            _changeWatchers[key] = new KeyValueWatcher()
            {
                Key = key,
                Label = label,
                PollInterval = pollInterval
            };
            return this;
        }

        /// <summary>
        /// Instructs the AzconfigOptions to include all key-values with matching the specified key and label filters.
        /// </summary>
        /// <param name="keyFilter">
        /// The key filter to apply when querying the configuration store for key-values.
        /// </param>
        /// <param name="labelFilter">
        /// The label filter to apply when querying the configuration store for key-values.
        /// Does not support '*' and ','.
        /// </param>
        /// <param name="preferredDateTime">
        /// Used to query key-values in the state that they existed at the time provided.
        /// </param>
        public AzconfigOptions Use(string keyFilter, string labelFilter = null, DateTimeOffset? preferredDateTime = null)
        {
            if (string.IsNullOrEmpty(keyFilter))
            {
                throw new ArgumentNullException(nameof(keyFilter));
            }

            if (labelFilter == null)
            {
                labelFilter = string.Empty;
            }

            // Do not support * and , for label filter for now.
            if (labelFilter.Contains('*') || labelFilter.Contains(','))
            {
                throw new ArgumentException("The characters '*' and ',' are not supported in label filters.", nameof(labelFilter));
            }

            var keyValueSelector = new KeyValueSelector()
            {
                KeyFilter = keyFilter,
                LabelFilter = labelFilter,
                PreferredDateTime = preferredDateTime
            };

            _kvSelectors.Add(keyValueSelector);

            return this;
        }

        public AzconfigOptions AddOfflineFileCache(string path)
        {
            return AddOfflineFileCache(new OfflineCacheOptions() { Target = path });
        }

        public AzconfigOptions AddOfflineFileCache(OfflineCacheOptions options = null)
        {
            if (ConnectionString == null)
            {
                throw new InvalidOperationException("Please call Connect first.");
            }

            if (options == null)
            {
                options = new OfflineCacheOptions();
            }

            if (!options.IsCryptoDataReady)
            {
                byte[] secret = Convert.FromBase64String(Utility.ParseConnectionString(ConnectionString, "Secret"));

                // for AES 256 the block size must be 128 bits (16 bytes)
                if (secret.Length < 16)
                {
                    throw new InvalidOperationException("Invalid connection string length.");
                }

                options.Key = secret;
                options.SignKey = secret;
                options.IV = new byte[16];
                Array.Copy(secret, options.IV, 16);
            }

            if (options.ScopeToken == null)
            {
                // Default would be Endpoint and KeyValueSelectors
                string endpoint = Utility.ParseConnectionString(ConnectionString, "Endpoint");
                if (string.IsNullOrWhiteSpace(endpoint))
                {
                    throw new InvalidOperationException("Invalid connection string format.");
                }

                var sb = new StringBuilder($"{endpoint}\0");
                _kvSelectors.ForEach(selector => {
                    sb.Append($"{selector.KeyFilter}\0{selector.LabelFilter}\0{selector.PreferredDateTime.GetValueOrDefault().ToUnixTimeSeconds()}\0");
                });

                options.ScopeToken = sb.ToString();
            }

            // While user didn't specific the cache path, we will try to use the default path if it's running inside App Service
            if (options.Target == null)
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
                            Interlocked.Increment(ref _instance);
                            options.Target = Path.Combine(cahcePath, $"app{_instance}-{BitConverter.ToString(hash).Replace("-", String.Empty)}.json");
                        }
                    }
                }

                if (options.Target == null)
                {
                    throw new NotSupportedException("The application must be running inside of an Azure App Service to use this feature.");
                }
            }

            OfflineCache = new OfflineFileCache(options);
            return this;
        }

        public AzconfigOptions Connect(string connectionString)
        {
            ConnectionString = connectionString;
            return this;
        }

        public AzconfigOptions ConnectWithManagedIdentity(Uri endpoint)
        {
            Client = AzconfigClientFactory.CreateClient(endpoint, Permissions.Read).Result;

            return this;
        }
    }
}
