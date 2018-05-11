namespace ConfigStoreDemo.Pages
{
    using Microsoft.AspNetCore.Mvc.RazorPages;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.Configuration.AppConfig;

    public class IndexModel : PageModel
    {
        public void OnGet()
        {
            ViewData["AppName"] = Config["APPNAME"] ?? "ConfigStoreDemo";
            ViewData["Language"] = Config["LANGUAGE"] ?? "English";
            ViewData["Messages"] = Config["MESSAGES"] ?? "Hello There;Thanks For Using Config Service";
            ViewData["FontSize"] = Config["FONTSIZE"] ?? "50";
            ViewData["RefreshRate"] = Config["RefreshRate"] ?? "1000";
            ViewData["BackgroundColor"] = Config["BACKGROUND_COLOR"] ?? "Orange";
        }

        private static IConfiguration _config;
        public IConfiguration Config
        {
            get
            {
                if (_config == null)
                {
                    string configStoreUrl = "https://ReplaceWithYourConfigStoreName.azconfig.io";

                    var builder = new ConfigurationBuilder();
                    builder.AddRemoteAppConfiguration(configStoreUrl, new RemoteConfigurationOptions()
                        .Listen("MESSAGES", 1000)
                        .Listen("BACKGROUND_COLOR", 1000));
                    _config = builder.Build();
                }

                return _config;
            }
        }
    }
}
