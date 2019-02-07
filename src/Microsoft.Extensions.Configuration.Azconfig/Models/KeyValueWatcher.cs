using System;

namespace Microsoft.Extensions.Configuration.Azconfig.Models
{
    class KeyValueWatcher
    {
        /// <summary>
        /// Key of the key-value to be watched.
        /// </summary>
        public string Key { get; set; }

        /// <summary>
        /// Label of the key-value to be watched.
        /// </summary>
        public string Label { get; set; }

        /// <summary>
        /// The interval at which the key-value will be polled for changes.
        /// </summary>
        public TimeSpan PollInterval { get; set; }

        /// <summary>
        /// A flag to reload all key-values.
        /// </summary>
        public bool ReloadAll { get; set; }
    }
}
