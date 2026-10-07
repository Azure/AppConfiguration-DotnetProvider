// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Azure.Identity;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using System;

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
                    const string endpointSettingName = "AppConfigurationEndpoint";

                    string endpoint = config.Build()[endpointSettingName];

                    if (string.IsNullOrEmpty(endpoint))
                    {
                        throw new InvalidOperationException(
                            $"The required setting '{endpointSettingName}' is missing. " +
                            $"Please set it in appsettings.json or as an environment variable.");
                    }

                    config.AddAzureAppConfiguration(options =>
                    {
                        options.Connect(new Uri(endpoint), new DefaultAzureCredential())
                            .ConfigureRefresh(refresh =>
                            {
                                refresh.RegisterAll()
                                    .SetRefreshInterval(TimeSpan.FromSeconds(1));
                            });
                    });
                })
                .UseStartup<Startup>()
                .Build();
        }
    }
}
