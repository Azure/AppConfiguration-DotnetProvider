// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Azure.Core;
using Azure.Core.Pipeline;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.AzureAppConfiguration;
using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.Examples.ConsoleApplication
{

    class Program
    {
        static IConfiguration Configuration { get; set; }
        static IConfigurationRefresher _refresher;

        static void Main(string[] args)
        {
            Configure();

            var cts = new CancellationTokenSource();

            _ = Run(cts.Token);

            // Finish on key press
            Console.ReadKey();
            cts.Cancel();
        }

        private static void Configure()
        {
            var builder = new ConfigurationBuilder();

            // Load a subset of the application's configuration from a json file and environment variables
            builder.AddJsonFile("appsettings.json")
                   .AddEnvironmentVariables();

            IConfiguration configuration = builder.Build();

            //if (string.IsNullOrEmpty(configuration["connection_string"]))
            //{
            //    Console.WriteLine("Connection string not found.");
            //    Console.WriteLine("Please set the 'connection_string' environment variable to a valid Azure App Configuration connection string and re-run this example.");
            //    return;
            //}

            // Augment the configuration builder with Azure App Configuration
            // Pull the connection string from an environment variable
            builder.AddAzureAppConfiguration(options =>
            {
                options.ConnectAzureFrontDoor(new Uri("https://staging-test-a5erhjeeacerb6ff.b01.azurefd.net"))
                       .ConfigureRefresh(refresh =>
                       {
                           refresh.RegisterAll()
                                  .SetRefreshInterval(TimeSpan.FromSeconds(10));
                       });

                // Get an instance of the refresher that can be used to refresh data
                _refresher = options.GetRefresher();
            });

            Configuration = builder.Build();
        }

        private static async Task Run(CancellationToken token)
        {
            string display = string.Empty;
            StringBuilder sb = new StringBuilder();

            Console.WriteLine($"Test: {Configuration["test"]}");

            //while (!token.IsCancellationRequested)
            //{
            //    // Trigger an async refresh for registered configuration settings without wait
            //    _ = _refresher.TryRefreshAsync();

            //    sb.AppendLine($"{Configuration["AppName"]} has been configured to run in {Configuration["Language"]}");
            //    sb.AppendLine();

            //    sb.AppendLine(string.Equals(Configuration["Language"], "spanish", StringComparison.OrdinalIgnoreCase) ? "Buenos Dias." : "Good morning");
            //    sb.AppendLine();

            //    sb.AppendLine("Press any key to exit...");
            //    await Task.Delay(1000);

            //    if (!sb.ToString().Equals(display))
            //    {
            //        display = sb.ToString();

            //        Console.Clear();
            //        Console.Write(display);
            //    }

            //    sb.Clear();
            //}
        }
    }
}
