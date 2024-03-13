// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Azure.Data.AppConfiguration;
using Azure;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using System.Threading;
using System.Linq;

//namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.Examples.ConfigStoreDemo
//{
//    public class Program
//    {
//        public static void Main(string[] args)
//        {
//            BuildWebHost(args).Run();
//        }

//        public static IWebHost BuildWebHost(string[] args)
//        {
//            return WebHost.CreateDefaultBuilder(args)
//                .ConfigureAppConfiguration((ctx, config) =>
//                {
//                    // 1. Load settings from a JSON file and Azure App Configuration
//                    // 2. Retrieve the Azure App Configuration connection string from an environment variable
//                    // 3. Set up the provider to listen for changes to the background color key-value in Azure App Configuration

//                    var settings = config.AddJsonFile("appsettings.json").Build();
//                    config.AddAzureAppConfiguration(options =>
//                    {
//                        options.Connect(Environment.GetEnvironmentVariable("ConnectionString"))
//                                .Select("TestJson")
//                                .ConfigureRefresh(refresh =>
//                                {
//                                    refresh.Register("sentinel", "label", true)
//                                            .SetCacheExpiration(TimeSpan.FromSeconds(2));
//                                })
//                                .UseFeatureFlags(ffoptions =>
//                                {
//                                    ffoptions.Select("TestSdk", "prod");
//                                    ffoptions.CacheExpirationInterval = TimeSpan.FromSeconds(2);
//                                });
//                        //options.LoadBalancingEnabled = true;
//                    });
//                })
//                .UseStartup<Startup>()
//                .Build();
//        }
//    }
//}

namespace KvsetMonitoringSample
{
    internal class Program
    {
        const string connectionString = "";
        static ConfigurationClient _client = new ConfigurationClient(Environment.GetEnvironmentVariable("ConnectionString"));
        private static Dictionary<string, ConfigurationSetting> _data = new Dictionary<string, ConfigurationSetting>();
        private static Dictionary<SettingSelector, IEnumerable<ETag>> _watchedSettings = new Dictionary<SettingSelector, IEnumerable<ETag>>();

        static async Task Main(string[] args)
        {
            var selectors = new List<SettingSelector>
            {
                new SettingSelector
                {
                    KeyFilter = "App1*",
                    LabelFilter = "dev"
                },
                new SettingSelector
                {
                    KeyFilter = "App1*",
                    LabelFilter = "prod"
                }
            };

            // 
            // Initialize pages for each selector
            Dictionary<SettingSelector, IEnumerable<Page<ConfigurationSetting>>>
                selectorPageMap = await InitializePagesAsync(selectors);

            // 
            // Save all settings from the loaded pages
            var settings = new List<ConfigurationSetting>();

            foreach (SettingSelector select in selectorPageMap.Keys)
            {
                settings.AddRange(selectorPageMap[select].SelectMany(page => page.Values));
            }

            //
            // Refresh the pages and update IConfiguration if AppConfig values have changed
            if (await TryRefreshPagesAsync(selectorPageMap))
            {
                settings.Clear();

                foreach (SettingSelector select in selectorPageMap.Keys)
                {
                    settings.AddRange(selectorPageMap[select].SelectMany(page => page.Values));
                }
            }

            _data = await InitializeAsync(selectors);

            //
            // Refresh the pages and update IConfiguration if AppConfig values have changed
            if (await TryRefreshAsync(selectors))
            {
                foreach (SettingSelector select in selectors)
                {
                    settings.AddRange(selectorPageMap[select].SelectMany(page => page.Values));
                }
            }
        }

        private static async Task<bool> TryRefreshPagesAsync(Dictionary<SettingSelector, IEnumerable<Page<ConfigurationSetting>>> selectorPageMap)
        {
            bool hasConfigChanged = false;
            IAsyncEnumerator<Page<ConfigurationSetting>> enumerator = null;

            try
            {
                foreach (SettingSelector selector in selectorPageMap.Keys)
                {
                    IEnumerable<Page<ConfigurationSetting>> oldPages = selectorPageMap[selector];
                    var updatedPages = new List<Page<ConfigurationSetting>>();
                    var matchConditions = new List<MatchConditions>();

                    // 
                    // Create match conditions with etags corresponding to each existing page
                    foreach (Page<ConfigurationSetting> page in oldPages)
                    {
                        matchConditions.Add(new MatchConditions
                        {
                            IfNoneMatch = page.GetRawResponse().Headers.ETag.Value
                        });
                    }

                    //
                    // New API to get pages if anything changed on the server
                    enumerator = _client.GetConfigurationSettingsAsync(selector, matchConditions)
                        .AsPages()
                        .GetAsyncEnumerator();

                    Page<ConfigurationSetting> updatedPage;
                    int i = 0;

                    while (await enumerator.MoveNextAsync())
                    {
                        updatedPage = enumerator.Current;

                        // 
                        // If the response is 200, config has changed - store the new page
                        if (updatedPage.GetRawResponse().Status == (int)HttpStatusCode.OK &&
                            updatedPage.Values != null)
                        {
                            hasConfigChanged = true;
                            updatedPages.Add(updatedPage);
                        }
                        else
                        {
                            // reuse the previously loaded page
                            updatedPages.Add(oldPages.ElementAt(i));
                        }

                        // move iterator to point to the next page in currentPages
                        i++;
                    }

                    //
                    // update the pages for each selector
                    selectorPageMap[selector] = updatedPages;
                }
            }
            catch (RequestFailedException e)
            {
                // Error handling
            }
            finally
            {
                if (enumerator != null)
                {
                    await enumerator.DisposeAsync();
                }
            }

            return hasConfigChanged;
        }

        private static async Task<bool> TryRefreshAsync(IEnumerable<SettingSelector> selectors)
        {
            bool hasConfigChanged = false;
            IAsyncEnumerator<Page<ConfigurationSetting>> enumerator = null;

            try
            {
                foreach (SettingSelector selector in _watchedSettings.Keys)
                {
                    var matchConditions = new List<MatchConditions>();

                    // 
                    // Create match conditions with etags corresponding to each existing page
                    foreach (ETag etag in _watchedSettings[selector])
                    {
                        matchConditions.Add(new MatchConditions
                        {
                            IfNoneMatch = etag
                        });
                    }

                    //
                    // New API to get pages if anything changed on the server
                    enumerator = _client.GetConfigurationSettingsAsync(selector, matchConditions)
                        .AsPages()
                        .GetAsyncEnumerator();

                    Page<ConfigurationSetting> updatedPage;

                    while (await enumerator.MoveNextAsync())
                    {
                        updatedPage = enumerator.Current;

                        // 
                        // If the response is 200, config has changed - store the new page
                        if (updatedPage.GetRawResponse().Status == (int)HttpStatusCode.OK &&
                            updatedPage.Values != null)
                        {
                            hasConfigChanged = true;
                            break;
                        }
                    }

                    if (hasConfigChanged)
                    {
                        break;
                    }
                }
            }
            catch (RequestFailedException e)
            {
                // Error handling
            }
            finally
            {
                if (enumerator != null)
                {
                    await enumerator.DisposeAsync();
                }
            }

            if (hasConfigChanged)
            {
                _data = await InitializeAsync(selectors);
            }

            return hasConfigChanged;
        }

        private static async Task<Dictionary<SettingSelector, IEnumerable<Page<ConfigurationSetting>>>>
                InitializePagesAsync(IEnumerable<SettingSelector> selectors)
        {
            //
            // Map selectors to their pages in a dictionary
            var selectorPageMap = new Dictionary<SettingSelector, IEnumerable<Page<ConfigurationSetting>>>();
            Page<ConfigurationSetting> page;

            foreach (SettingSelector selector in selectors)
            {
                var pages = new List<Page<ConfigurationSetting>>();
                IAsyncEnumerator<Page<ConfigurationSetting>> enumerator = null;

                try
                {
                    enumerator = _client.GetConfigurationSettingsAsync(selector)
                            .AsPages()
                            .GetAsyncEnumerator();

                    while (await enumerator.MoveNextAsync())
                    {
                        page = enumerator.Current;
                        pages.Add(page);
                    }

                    selectorPageMap[selector] = pages;
                }
                catch (RequestFailedException e)
                {
                    // Error handling
                }
                finally
                {
                    if (enumerator != null)
                    {
                        await enumerator.DisposeAsync();
                    }
                }
            }

            return selectorPageMap;
        }

        private static async Task<Dictionary<string, ConfigurationSetting>>
        InitializeAsync(IEnumerable<SettingSelector> selectors)
        {
            //
            // Map selectors to their pages in a dictionary
            var data = new Dictionary<string, ConfigurationSetting>();
            var watchedSettings = new Dictionary<SettingSelector, IEnumerable<ETag>>();
            Page<ConfigurationSetting> page;

            foreach (SettingSelector selector in selectors)
            {
                IAsyncEnumerator<Page<ConfigurationSetting>> enumerator = null;

                try
                {
                    enumerator = _client.GetConfigurationSettingsAsync(selector)
                            .AsPages()
                            .GetAsyncEnumerator();

                    while (await enumerator.MoveNextAsync())
                    {
                        page = enumerator.Current;
                        foreach (ConfigurationSetting setting in page.Values)
                        {
                            data[setting.Key] = setting;
                        }
                        watchedSettings[selector].Append(page.GetRawResponse().Headers.ETag.Value);
                    }
                }
                catch (RequestFailedException e)
                {
                    // Error handling
                }
                finally
                {
                    if (enumerator != null)
                    {
                        await enumerator.DisposeAsync();
                    }
                }
            }

            _watchedSettings = watchedSettings;

            return data;
        }
    }

    internal static class ConfigurationClientExtensions
    {
        /// <summary>
        /// Get configuration settings if they have changed on the server
        /// </summary>
        /// <param name="client"></param>
        /// <param name="selector">Key/Label filters to specify the settings being selected.</param>
        /// <param name="matchConditions">Ordered collection of <see cref="MatchConditions"/> for conditionally requesting each <see cref="Page{T}"/>
        /// of <see cref="ConfigurationSetting"/>. MatchConditions should contain the ETags corresponding to Page ETags.</param>
        /// <param name="cancellationToken"></param>
        /// <returns>Page containing list of ConfigurationSettings if the server response is 200.
        /// Page doesnt contain any Values if response is 304.</returns>
        public static AsyncPageable<ConfigurationSetting> GetConfigurationSettingsAsync(this ConfigurationClient client, SettingSelector selector, IEnumerable<MatchConditions> matchConditions, CancellationToken cancellationToken = default)
        {
            // 
            // New SDK API implementation
            return client.GetConfigurationSettingsAsync(selector, cancellationToken);
        }
    }
}
