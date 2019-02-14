namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.Examples.ConfigStoreDemo.Pages
{
    using Microsoft.AspNetCore.Mvc.RazorPages;
    using Microsoft.Extensions.Options;

    public class IndexModel : PageModel
    {
        private Settings settings;

        public IndexModel(IOptionsSnapshot<Settings> options)
        {
            settings = options.Value;
        }
        public void OnGet()
        {
            ViewData["AppName"] = settings.AppName;
            ViewData["Language"] = settings.Language;
            ViewData["Messages"] = settings.Messages;
            ViewData["FontSize"] = settings.FontSize;
            ViewData["RefreshRate"] = settings.RefreshRate;
            ViewData["BackgroundColor"] = settings.BackgroundColor;
        }
    }
}
