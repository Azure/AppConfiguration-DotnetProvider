using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using System;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.Examples.ConfigStoreDemo
{
    public class Program
    {
        private static IConfigurationRefresher _refresher;

        public static void Main(string[] args)
        {
            // Temporary code to trigger refresh in the web application
            // TODO (abarora) : Remove this code and use a middleware for refresh
            var timer = new System.Timers.Timer(2000);
            timer.Elapsed += (sender, e) => _refresher.Refresh();
            timer.Enabled = true;

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
                    config.AddAzureAppConfiguration(options =>
                    {
                        options.Connect(settings["connection_string"])
                               .ConfigureRefresh(refresh =>
                               {
                                   refresh.Register("Settings:BackgroundColor", refreshAll: false)
                                          .SetCacheExpiration(TimeSpan.FromSeconds(10));
                               });

                        _refresher = options.GetRefresher();
                    });
                })
                .UseStartup<Startup>()
                .Build();
        }
    }
}
