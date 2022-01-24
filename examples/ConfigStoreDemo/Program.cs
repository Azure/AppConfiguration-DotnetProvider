// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Azure.Identity;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using System;
using System.Linq;

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
            return WebHost.CreateDefaultBuilder(args)
                .ConfigureAppConfiguration((hostingContext, config) =>
                {
                    // 1. Load settings from a JSON file and Azure App Configuration
                    // 2. Retrieve the Azure App Configuration connection string from an environment variable
                    // 3. Set up the provider to listen for changes to the background color key-value in Azure App Configuration

                    var settings = config.AddJsonFile("appsettings.json").Build();

                    IConfigurationSection endpointsSection = settings.GetSection("endpoints");
                    var endpoints = endpointsSection.GetChildren().AsEnumerable().Select(endpoint => new Uri(endpoint.Value));

                    config.AddAzureAppConfiguration(options =>
                    {
                        options.Connect(endpoints.ToArray(), new DefaultAzureCredential())
                               .ConfigureRefresh(refresh =>
                               {
                                   refresh.Register("Settings:BackgroundColor")
                                          .SetCacheExpiration(TimeSpan.FromSeconds(10));
                               });
                    });
                })
                .UseStartup<Startup>()
                .Build();
        }
    }
}
