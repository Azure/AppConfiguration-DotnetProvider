namespace Application
{
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.Configuration.Azconfig;
    using System;
    using System.Threading;
    using System.Threading.Tasks;

    class Program
    {
        static IConfiguration Configuration { get; set; }

        static void Main(string[] args)
        {
            Configure();

            var cts = new CancellationTokenSource();

            var t = Run(cts.Token);

            //
            // Finish on key press
            Console.ReadKey();

            cts.Cancel();
        }

        private static void Configure()
        {
            var builder = new ConfigurationBuilder();

            builder.AddJsonFile("appsettings.json", false, true);

            IConfiguration configuration = builder.Build();

            builder.AddRemoteAppConfiguration(configuration["config_url"], configuration["secret_id"], configuration["secret_value"],
                new RemoteConfigurationOptions()
                {
                    Prefix = "App1/",
                    AcceptVersion = configuration["version"],
                    KeyValueFormatter = new KeyValueFormatter()
                }
                .Listen("AppName", 1000)
                .Listen("Language", 1000));

            Configuration = builder.Build();
        }

        private static async Task Run(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                Console.Clear();

                Console.WriteLine("You're running " + Configuration["AppName"]);
                Console.WriteLine();

                Console.WriteLine(string.Equals(Configuration["Language"], "spanish", StringComparison.OrdinalIgnoreCase) ? "Buenos Dias." : "Good morning");
                Console.WriteLine();

                foreach (var section in Configuration.GetChildren())
                {
                    Console.WriteLine($"{section.Key}: {section.Value}");
                }

                Console.WriteLine();

                Console.WriteLine("Press any key to exit...");

                await Task.Delay(1000);
            }
        }
    }
}
