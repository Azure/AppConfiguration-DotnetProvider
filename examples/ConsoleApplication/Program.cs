using Microsoft.Extensions.Configuration.AzureAppConfiguration;
using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.Examples.ConsoleApplication
{
    class Program
    {
        static void Main(string[] args)
        {
            var builder = new ConfigurationBuilder(); //new config builder

            builder.AddAzureAppConfiguration(Environment.GetEnvironmentVariable("ConnectionString")); //get the connecting string

            IConfigurationRoot config = builder.Build(); //call the build method in config builder

           Console.WriteLine(config["OutlookPassword"]);
        }
    }
}

