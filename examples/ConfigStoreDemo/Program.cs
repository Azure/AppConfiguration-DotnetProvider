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
                    // 1. Load settings from a JSON file and Azure App Configuration
                    // 2. Retrieve the Azure App Configuration connection string from an environment variable
                    // 3. Set up the provider to listen for changes to the background color key-value in Azure App Configuration

                    var settings = config.AddJsonFile("appsettings.json").Build();
                    config.AddAzureAppConfiguration(options =>
                    {
                        options.Connect("Endpoint=https://configstore.azconfig.io;Id=0-l3-s0:KHCTawEN7KgILTkc44xi;Secret=oMN6aENui0R23jkQvlCukF1V3RqZcP94d0B+QQ/bdmM=")//settings["connection_string"])
                               .ConfigureRefresh(refresh =>
                               {
                                   refresh.Register("Settings:BackgroundColor")
                                          .SetCacheExpiration(TimeSpan.FromSeconds(10));
                               })
                               .Use("abc*")
                               ;
                    });
                })
                .UseStartup<Startup>()
                .Build();
        }
    }
}
