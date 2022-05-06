// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//

using Azure.Identity;
using System.Text;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.Examples.ConsoleApplicationWithFailOver
{
    class Program
    {
        static IConfiguration? Configuration { get; set; }
        private static IConfigurationRefresher? _refresher;

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

            IConfigurationSection endpointsSection = configuration.GetSection("AppConfig:Endpoints");
            IEnumerable<Uri> endpoints = endpointsSection.GetChildren().Select(endpoint => new Uri(endpoint.Value));

            if (endpoints == null || !endpoints.Any())
            {
                Console.WriteLine("Endpoints not found.");
                Console.WriteLine("Please set the array 'Appconfig:Endpoints' in appsettings.json with valid Azure App Configuration replica endpoints and re-run this example.");
                return;
            }

            // Augment the configuration builder with Azure App Configuration
            // Pull the connection string from an environment variable
            builder.AddAzureAppConfiguration(options =>
            {
                options.Connect(endpoints, new DefaultAzureCredential())
                       .ConfigureRefresh(refresh =>
                       {
                           refresh.Register("AppName")
                                  .SetCacheExpiration(TimeSpan.FromSeconds(10));
                       });

                // Get an instance of the refresher that can be used to refresh data
                _refresher = options.GetRefresher();
            });

            Configuration = builder.Build();
        }

        private static async Task Run(CancellationToken token)
        {
            string display = string.Empty;
            StringBuilder sb = new();

            while (!token.IsCancellationRequested)
            {
                // Trigger an async refresh for registered configuration settings without wait
                _ = _refresher?.TryRefreshAsync(token);

                sb.AppendLine($"The AppName is: {Configuration?["AppName"]}.");
                sb.AppendLine();

                sb.AppendLine("Press any key to exit...");
                await Task.Delay(1000, token);

                if (!sb.ToString().Equals(display))
                {
                    display = sb.ToString();

                    Console.Clear();
                    Console.Write(display);
                }

                sb.Clear();
            }
        }
    }
}
