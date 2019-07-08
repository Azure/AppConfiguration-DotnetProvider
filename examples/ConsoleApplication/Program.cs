namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.Examples.ConsoleApplication
{
    using Microsoft.Azure.KeyVault;
    using Microsoft.Azure.KeyVault.Models;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.Configuration.AzureAppConfiguration;
    using Microsoft.IdentityModel.Clients.ActiveDirectory;
    using System;
    using System.Collections.Generic;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

    class Program
    {
        static IConfiguration Configuration { get; set; }


       

        static void Main(string[] args)
        {



            ///Version 1
            /////////////////////////////////////////////////////////////
            var builder = new ConfigurationBuilder(); //new config builder

            builder.AddAzureAppConfiguration(Environment.GetEnvironmentVariable("ConnectionString")); //get the connecting string

            IConfigurationRoot config = builder.Build(); //call the build method in config builder

            string value = config[".appconfig.keyvault/TheTrialSecret"];
            Console.WriteLine(value);

            /*AzureKeyVaultKeyValueAdapter azureKeyVaultKeyValueAdapter = new AzureKeyVaultKeyValueAdapter();
            azureKeyVaultKeyValueAdapter.GetSecretFromKeyVault(value);*/
            /////////////////////////////////////////////////////////////


            ///version 0
            /////////////////////////////////////////////////////////////
           /* Configure();

            var cts = new CancellationTokenSource();

            var t = Run(cts.Token);

            //
            // Finish on key press
            Console.ReadKey();

            cts.Cancel();*/
            /////////////////////////////////////////////////////////////
        }

        /// version 0
        /////////////////////////////////////////////////////////////

        private static void Configure()
        {
            var builder = new ConfigurationBuilder();

            //
            // load a subset of the application's configuration from a json file and environment variables.
            builder.AddJsonFile("appsettings.json")
                .AddEnvironmentVariables();

            IConfiguration configuration = builder.Build();

            if (string.IsNullOrEmpty(configuration["connection_string"]))
            {
                Console.WriteLine("Connection string not found.");
                Console.WriteLine("Please set the 'connection_string' environment variable to a valid Azure App Configuration connection string and re-run this example.");
                return;
            }

            //
            // Augment the configuration builder with Azure App Configuration
            // Pull the connection string from an environment variable
            builder.AddAzureAppConfiguration(o =>
            {

                o.Connect(configuration["connection_string"])
                 //
                 // Uncomment to filter selected key-values by key and/or label
                 //.Use("App*", "label1")
                 .Watch("Language", TimeSpan.FromMilliseconds(1000))
                 .Watch("AppName", TimeSpan.FromMilliseconds(1000));
            });

            Configuration = builder.Build();
        }

        private static async Task Run(CancellationToken token)
        {
            string display = string.Empty;
            StringBuilder sb = new StringBuilder();

            while (!token.IsCancellationRequested)
            {
                sb.AppendLine($"{Configuration["AppName"]} has been configured to run in {Configuration["Language"]}");
                sb.AppendLine();

                sb.AppendLine(string.Equals(Configuration["Language"], "spanish", StringComparison.OrdinalIgnoreCase) ? "Buenos Dias." : "Good morning");
                sb.AppendLine();

                sb.AppendLine("Press any key to exit...");

                await Task.Delay(1000);

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
