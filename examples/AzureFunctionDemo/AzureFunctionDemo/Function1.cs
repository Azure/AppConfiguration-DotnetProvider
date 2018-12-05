using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Azconfig;

namespace AzureFunctionDemo
{
    public static class Function1
    {
        [FunctionName("Function1")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = null)] HttpRequest req,
            ILogger log)
        {
            log.LogInformation("C# HTTP trigger function processed a request.");

            // This demo assume you already
            // 1. provisioned a Configuration Store and set environment variable "ConfigStoreConnectionString" 
            //    to the connection string of your store
            // 2. had a key/value entry in the store with key named "name"
            var builder = new ConfigurationBuilder();
            builder.AddAzconfig(Environment.GetEnvironmentVariable("ConfigStoreConnectionString"));
            var config = builder.Build();
            string name = config["name"];

            name = name ?? req.Query["name"];

            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            dynamic data = JsonConvert.DeserializeObject(requestBody);
            name = name ?? data?.name;

            return name != null
                ? (ActionResult)new OkObjectResult($"Hello, {name}")
                : new BadRequestObjectResult("Please pass a name from a configuration store, on the query string or in the request body");
        }
    }
}
