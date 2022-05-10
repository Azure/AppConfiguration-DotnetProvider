// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//

using Azure.Identity;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.Examples.ConsoleApplicationWithFailOver
{
    class Program
    {
        static IConfiguration Configuration { get; set; }

        static void Main(string[] args)
        {
            Configure();

            Console.WriteLine($"The AppName is: {Configuration?["AppName"]}.");
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
                options.Connect(endpoints, new DefaultAzureCredential());
            });

            Configuration = builder.Build();
        }
    }
}
