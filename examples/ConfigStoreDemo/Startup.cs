using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Azconfig;
using Microsoft.Extensions.DependencyInjection;

namespace ConfigStoreDemo
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            configuration["connection_string"] = "Endpoint=https://garywang-demo-store.azconfig.io;Id=1733fe435bb07b21;Secret=jCWt+aXO3IfJvEtFzvqMirXv7CAOKc01Lu3SuQGK6vo=";

            // load configurations from local json file and remote config store.
            // load all key-values with null label and listen one key.
            // Pull configuration connection string from environment variable
            var builder = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json")
                .AddAzconfig(o =>
                {
                    o.Connect(configuration["connection_string"])
                     .AddOfflineCache(new OfflineFileCache())
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
