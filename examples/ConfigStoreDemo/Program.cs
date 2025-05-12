// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Azure.Core;
using Azure.Data.AppConfiguration;
using Azure.Identity;
using ConfigStoreDemo;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Options;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.Examples.ConfigStoreDemo
{
    public class Program
    {
        public static void Main(string[] args)
        {
            BuildWebHost(args).Run();
        }

        public static IWebHost BuildWebHost(string[] args)
        {
            //ConfigurationClient client = new ConfigurationClient(new Uri("https://ajusupovic-ac2.azconfig.io"), new DefaultAzureCredential());

            //ConfigurationSetting setting = client.GetConfigurationSetting("JsonKeyTest");

            //setting.ContentType = "application/json;profile=\"https://azconfig.io/mime-profiles/ai/chat-completion\";charset=UTF-8";

            //setting.Key = "JsonKeyTest2";

            //client.SetConfigurationSetting(setting);

            var builder = WebApplication.CreateBuilder(args);

            string connectionString = builder.Configuration.GetConnectionString("AppConfig");

            IList<string> tagsFilterList = new List<string> { "  group=g1", "key1=value1", "any=" };

            var host = WebHost.CreateDefaultBuilder(args)
                .ConfigureAppConfiguration((hostingContext, config) =>
                {
                    // 1. Load settings from a JSON file and Azure App Configuration
                    // 2. Retrieve the Azure App Configuration connection string from an environment variable
                    // 3. Set up the provider to listen for changes to the background color key-value in Azure App Configuration

                    config.AddAzureAppConfiguration(options =>
                    {
                        options.UseFeatureFlags(ff =>
                        {
                            ff.Select("TestFlag");
                        });
                        //options.SelectSnapshot("Test1");
                        //options.Select("TestLabel", "1", tagsFilterList);
                        options.Select("AppName");
                        options.Select("JsonKeyTest*");
                        options.Select("Settings*");
                        options.UseFeatureFlags(ff =>
                        {
                            ff.Select("OnOff");
                            ff.Select("TestVariants");
                            ff.SetRefreshInterval(TimeSpan.FromSeconds(2));
                        });
                        options.ConfigureStartupOptions(startup =>
                        {
                            startup.Timeout = TimeSpan.FromSeconds(60);
                        });
                        //options.Connect(Environment.GetEnvironmentVariable("ConnectionString"));
                        options.Connect(new Uri("https://ajusupovic-ac2.azconfig.io"), new DefaultAzureCredential());
                        options.ConfigureRefresh(refresh =>
                        {
                            //refresh.Register("Settings:BackgroundColor", refreshAll: true);
                            //refresh.Register("Settings:Messages");
                            refresh.RegisterAll();
                            refresh.SetRefreshInterval(TimeSpan.FromSeconds(2));
                        });
                        options.Map(setting =>
                        {
                            if (setting.Key.Contains("TestFlag"))
                            {
                                Console.WriteLine("\nTEST FLAG TIME\n");
                            }

                            if (setting.Key.Contains("Language"))
                            {
                                Console.WriteLine($"\nLANGUAGE: {setting.Value}\n");
                            }

                            return new ValueTask<ConfigurationSetting>(setting);
                        });
                        options.ConfigureClientOptions(clientOptions =>
                        {
                            clientOptions.AddPolicy(new ApiVersionHeaderPolicy(), HttpPipelinePosition.PerCall);
                        });
                    });

                    config.AddAzureAppConfiguration(options =>
                    {
                        options.Select("AppName");
                        options.UseFeatureFlags(ff =>
                        {
                            ff.Select("TestVariants2");
                            ff.SetRefreshInterval(TimeSpan.FromSeconds(2));
                        });
                        options.Connect(new Uri("https://ajusupovic-ac2.azconfig.io"), new DefaultAzureCredential());
                    });

                    //config.AddAzureAppConfiguration(options =>
                    //{
                    //    options.Select("JsonAITest");
                    //    options.Connect(Environment.GetEnvironmentVariable("PersonalConnectionString"));
                    //    options.ConfigureRefresh(refresh =>
                    //    {
                    //        refresh.RegisterAll();
                    //        refresh.SetRefreshInterval(TimeSpan.FromSeconds(1));
                    //    });
                    //});
                })
                .UseStartup<Startup>()
                .Build();

            var haha = builder.Configuration;

            return host;
        }
    }
}
