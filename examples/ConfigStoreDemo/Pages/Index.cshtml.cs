// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.Examples.ConfigStoreDemo.Pages
{
    using Microsoft.AspNetCore.Mvc.RazorPages;
    using Microsoft.Extensions.Options;
    using Microsoft.FeatureManagement;
    using System;
    using System.Threading;
    using System.Threading.Tasks;

    public class IndexModel : PageModel
    {
        private const string EasterEggFeatureName = "EasterEgg";
        private readonly HomePageOptions _options;
        private readonly IVariantFeatureManagerSnapshot _featureManager;

        public IndexModel(IOptionsSnapshot<HomePageOptions> options, IVariantFeatureManagerSnapshot featureManager)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            if (featureManager == null)
            {
                throw new ArgumentNullException(nameof(featureManager));
            }

            _options = options.Value;
            _featureManager = featureManager;
        }

        /// <summary>
        /// Gets the image URL from the visitor's assigned EasterEgg variant.
        /// </summary>
        public string EasterEggImageUrl { get; private set; }

        /// <summary>
        /// Loads homepage settings and evaluates the visitor's EasterEgg variant.
        /// </summary>
        public async Task OnGetAsync(CancellationToken cancellationToken)
        {
            ViewData["Messages"] = _options.Messages;
            ViewData["FontSize"] = _options.FontSize;
            ViewData["RefreshRate"] = _options.RefreshRate;
            ViewData["BackgroundColor"] = _options.BackgroundColor;

            Variant variant = await _featureManager.GetVariantAsync(EasterEggFeatureName, cancellationToken);
            EasterEggImageUrl = variant?.Configuration?.Value;
        }
    }
}
