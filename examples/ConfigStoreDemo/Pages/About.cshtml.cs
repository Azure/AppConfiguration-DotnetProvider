// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.Examples.ConfigStoreDemo.Pages
{
    public class AboutModel : PageModel
    {
        public string? Message { get; set; }

        public void OnGet()
        {
            Message = "Your application description page.";
        }
    }
}
