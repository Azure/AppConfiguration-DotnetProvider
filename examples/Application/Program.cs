namespace Application
{
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.Configuration.AzureAppConfiguration;
    using System;
    using System.Text;
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
            builder.AddJsonFile("appsettings.json")
                .AddEnvironmentVariables();

            IConfiguration configuration = builder.Build();

            if (string.IsNullOrEmpty(configuration["connection_string"]))
            {
                Console.WriteLine("Connection string not found.");
                Console.WriteLine("Please set the connection_string environment variable to a valid AppConfiguration connection string to and re-run this example.");
                return;
            }

            //
            // Pull configuration connection string from environment variable
            builder.AddAzconfig(o => {

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
