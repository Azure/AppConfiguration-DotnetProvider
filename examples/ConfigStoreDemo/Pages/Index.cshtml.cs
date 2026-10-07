// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.Examples.ConfigStoreDemo.Pages
{
    using Microsoft.AspNetCore.Mvc.RazorPages;
    using Microsoft.Extensions.Options;
    using System;

    public class IndexModel : PageModel
    {
        private HomePageOptions _options;

        public IndexModel(IOptionsSnapshot<HomePageOptions> options)
        {
            _options = options?.Value ?? throw new ArgumentNullException();
        }
        public void OnGet()
        {
            ViewData["Messages"] = _options.Messages;
            ViewData["FontSize"] = _options.FontSize;
            ViewData["RefreshRate"] = _options.RefreshRate;
            ViewData["BackgroundColor"] = _options.BackgroundColor;
        }
    }
}
