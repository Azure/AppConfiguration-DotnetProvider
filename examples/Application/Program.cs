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

            // load some local configurations from files
            builder.AddJsonFile("appsettings.json");

            IConfiguration configuration = builder.Build();

            // load key-values with prefix "App" label "label1" and listen two keys.
            // Pull configuration connection string from environment variable
            builder.AddRemoteAppConfiguration(o => {
                o.Connect(configuration["connection_string"])
                 .Use("App*", "label1")
                 .Watch("Language", 1000, "label1")
                 .Watch("AppName", 1000, "label1");
            });
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
