using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using System;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.Examples.ConfigStoreDemo
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            //
            // This example:
            // Loads settings from a json file and Azure App Configuration.
            // Sets up the provider to listen for changes to the background color key-value in Azure App Configuration.
            // Retrieves the Azure App Configuration connection string from an environment variable
            var builder = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json")
                .AddAzureAppConfiguration(o =>
                {
                    o.Connect(configuration["connection_string"])
                     .Watch("Settings:BackgroundColor", TimeSpan.FromMilliseconds(1000));
                });
            Configuration = builder.Build();
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.Configure<Settings>(Configuration.GetSection("Settings"));
            services.AddMvc();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IHostingEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseBrowserLink();
                app.UseDeveloperExceptionPage();
            }
            else
            {
                app.UseExceptionHandler("/Error");
            }

            app.UseStaticFiles();

            app.UseMvc();
        }
    }
}
