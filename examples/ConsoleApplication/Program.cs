//// Copyright (c) Microsoft Corporation.
//// Licensed under the MIT license.
////
//namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.Examples.ConsoleApplication
//{
//    using Microsoft.Extensions.Configuration;
//    using Microsoft.Extensions.Configuration.AzureAppConfiguration;
//    using System;
//    using System.Text;
//    using System.Threading;
//    using System.Threading.Tasks;

//    class Program
//    {
//        static IConfiguration Configuration { get; set; }
//        static IConfigurationRefresher _refresher;

//        static void Main(string[] args)
//        {
//            Configure();

//            var cts = new CancellationTokenSource();

//            _ = Run(cts.Token);

//            // Finish on key press
//            Console.ReadKey();
//            cts.Cancel();
//        }

//        private static void Configure()
//        {
//            var builder = new ConfigurationBuilder();

//            // Load a subset of the application's configuration from a json file and environment variables
//            builder.AddJsonFile("appsettings.json")
//                   .AddEnvironmentVariables();

//            IConfiguration configuration = builder.Build();

//            if (string.IsNullOrEmpty(configuration["connection_string"]))
//            {
//                Console.WriteLine("Connection string not found.");
//                Console.WriteLine("Please set the 'connection_string' environment variable to a valid Azure App Configuration connection string and re-run this example.");
//                return;
//            }

//            // Augment the configuration builder with Azure App Configuration
//            // Pull the connection string from an environment variable
//            builder.AddAzureAppConfiguration(options =>
//            {
//                options.Connect(configuration["connection_string"])
//                       .Select("AppName")
//                       .Select("Settings:BackgroundColor")
//                       .ConfigureClientOptions(clientOptions => clientOptions.Retry.MaxRetries = 5)
//                       .ConfigureRefresh(refresh =>
//                       {
//                           refresh.Register("AppName")
//                                  .Register("Language", refreshAll: true)
//                                  .SetCacheExpiration(TimeSpan.FromSeconds(10));
//                       });

//                // Get an instance of the refresher that can be used to refresh data
//                _refresher = options.GetRefresher();
//            });

//            Configuration = builder.Build();
//        }

//        private static async Task Run(CancellationToken token)
//        {
//            string display = string.Empty;
//            StringBuilder sb = new StringBuilder();

//            while (!token.IsCancellationRequested)
//            {
//                // Trigger an async refresh for registered configuration settings without wait
//                _ = _refresher.TryRefreshAsync();

//                sb.AppendLine($"{Configuration["AppName"]} has been configured to run in {Configuration["Language"]}");
//                sb.AppendLine();

//                sb.AppendLine(string.Equals(Configuration["Language"], "spanish", StringComparison.OrdinalIgnoreCase) ? "Buenos Dias." : "Good morning");
//                sb.AppendLine();

//                sb.AppendLine("Press any key to exit...");
//                await Task.Delay(1000);

//                if (!sb.ToString().Equals(display))
//                {
//                    display = sb.ToString();

//                    Console.Clear();
//                    Console.Write(display);
//                }

//                sb.Clear();
//            }
//        }
//    }
//}

using Microsoft.Azure.ServiceBus;
using System;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.Examples.ConsoleApplication
{
    class Program
    {
        private const string AppConfigurationConnectionStringEnvVarName = "AppConfigurationConnectionString"; // e.g. Endpoint=https://{store_name}.azconfig.io;Id={id};Secret={secret}
        private const string ServiceBusConnectionStringEnvVarName = "ServiceBusConnectionString"; // e.g. Endpoint=sb://{service_bus_name}.servicebus.windows.net/;SharedAccessKeyName={key_name};SharedAccessKey={key}
        private const string ServiceBusTopicEnvVarName = "ServiceBusTopic";
        private const string ServiceBusSubscriptionEnvVarName = "ServiceBusSubscription";

        private static IConfigurationRefresher _refresher = null;

        static async Task Main(string[] args)
        {
            string appConfigurationConnectionString = Environment.GetEnvironmentVariable(AppConfigurationConnectionStringEnvVarName);

            IConfiguration configuration = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.Connect(appConfigurationConnectionString);
                    options.ConfigureRefresh(refresh =>
                        refresh
                            .Register("TestApp:Settings:Message")
                            .SetCacheExpiration(TimeSpan.FromDays(30))  // Important: Reduce poll frequency
                    );

                    _refresher = options.GetRefresher();
                }).Build();

            RegisterRefreshEventHandler();
            var message = configuration["TestApp:Settings:Message"];
            Console.WriteLine($"Initial value: {configuration["TestApp:Settings:Message"]}");

            while (true)
            {
                await _refresher.TryRefreshAsync();

                if (configuration["TestApp:Settings:Message"] != message)
                {
                    Console.WriteLine($"New value: {configuration["TestApp:Settings:Message"]}");
                    message = configuration["TestApp:Settings:Message"];
                }

                await Task.Delay(TimeSpan.FromSeconds(1));
            }
        }

        private static void RegisterRefreshEventHandler()
        {
            string serviceBusConnectionString = Environment.GetEnvironmentVariable(ServiceBusConnectionStringEnvVarName);
            string serviceBusTopic = Environment.GetEnvironmentVariable(ServiceBusTopicEnvVarName);
            string serviceBusSubscription = Environment.GetEnvironmentVariable(ServiceBusSubscriptionEnvVarName);
            SubscriptionClient serviceBusClient = new SubscriptionClient(serviceBusConnectionString, serviceBusTopic, serviceBusSubscription);

            serviceBusClient.RegisterMessageHandler(
                handler: (message, cancellationToken) =>
                {
                    string messageText = Encoding.UTF8.GetString(message.Body);
                    JsonElement messageData = JsonDocument.Parse(messageText).RootElement.GetProperty("data");
                    string key = messageData.GetProperty("key").GetString();
                    Console.WriteLine($"Event received for Key = {key}");

                    PushNotification pushNotification = new PushNotification();

                    PushNotification.TryParse(messageText, out pushNotification);

                    _refresher.ProcessPushNotification(pushNotification);

                    return Task.CompletedTask;
                },
                exceptionReceivedHandler: (exceptionargs) =>
                {
                    Console.WriteLine($"{exceptionargs.Exception}");
                    return Task.CompletedTask;
                });
        }
    }
}