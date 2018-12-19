using System;

namespace Microsoft.Extensions.Configuration.Azconfig.Models
{
    public class KeyValueSelector
    {
        /// <summary>
        /// Keys that will be used to filter.
        /// </summary>
        /// <remarks>See the documentation for this provider for details on the format of filter expressions</remarks>
        public string KeyFilter { get; set; }

        /// <summary>
        /// Labels that will be used to filter.
        /// </summary>
        public string LabelFilter { get; set; }

        /// <summary>
        /// If set, then key values will be retrieved exactly as they existed at the provided time.
        /// </summary>
        public DateTimeOffset? PreferredDateTime { get; set; }
    }
}
