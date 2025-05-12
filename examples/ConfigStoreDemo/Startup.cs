// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.AzureAppConfiguration.FeatureManagement;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Generic;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.Examples.ConfigStoreDemo
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.Configure<MvcOptions>(options =>
            {
                options.EnableEndpointRouting = false;
            });

            services.Configure<Settings>(Configuration.GetSection("Settings"));
            services.AddAzureAppConfiguration();
            services.AddAzureAppConfiguration();
            services.AddMvc();
            services.AddApplicationInsightsTelemetry();

            var flags = Configuration.GetSection("feature_management").GetSection("feature_flags").Get<List<FeatureFlag>>();

            Console.WriteLine("variant flags count: " + flags?.Count);
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                app.UseExceptionHandler("/Error");
            }

            app.UseAzureAppConfiguration();
            app.UseStaticFiles();
            app.UseMvc();
        }
    }
}
