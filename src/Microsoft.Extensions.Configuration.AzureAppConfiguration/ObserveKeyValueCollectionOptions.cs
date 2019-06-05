using System;
using System.Reactive.Concurrency;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    class ObserveKeyValueCollectionOptions
    {
        /// <summary>
        /// A filter used to limit the key-values that are observed to those who's keys begin with the specified prefix.
        /// </summary>
        /// <remarks>See the documentation for this SDK for details on the format of filter expressions</remarks>
        public string Prefix { get; set; } = string.Empty;

        /// <summary>
        /// A filter used to select the label to use for observation.
        /// </summary>
        /// <remarks>See the documentation for this SDK for details on the format of filter expressions</remarks>
        public string Label { get; set; } = LabelFilter.Null;

        /// <summary>
        /// The interval used to poll for changes.
        /// </summary>
        public TimeSpan PollInterval { get; set; } = TimeSpan.FromMilliseconds(500);
    }
}
