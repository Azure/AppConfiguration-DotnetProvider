// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.Models
{
    /// <summary>
    /// A selector used to control what key-values are retrieved from Azure App Configuration.
    /// </summary>
    public class KeyValueSelector
    {
        /// <summary>
        /// A filter that determines the set of keys that are included in the configuration provider.
        /// </summary>
        /// <remarks>See the documentation for this provider for details on the format of filter expressions</remarks>
        public string KeyFilter { get; set; }

        /// <summary>
        /// A filter that determines what label to use when selecting key-values for the the configuration provider
        /// </summary>
        public string LabelFilter { get; set; }
    }
}
