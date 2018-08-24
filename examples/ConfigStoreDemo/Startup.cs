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
            //
            // load configuration from local json file and environment variables.
            var builder = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json")
                .AddEnvironmentVariables();

            var config = builder.Build();

            //
            // Add remote configuration using intermediate configuration
            // load all key-values with null label and listen to one key.
            builder.AddRemoteAppConfiguration(o => {

                o.Connect(configuration["ConfigurationStore__ConnectionString"])
                    .Watch("Settings__BackgroundColor", 1000);
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
