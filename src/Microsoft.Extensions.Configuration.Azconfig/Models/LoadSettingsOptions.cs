namespace Microsoft.Extensions.Configuration.Azconfig.Models
{
    public class LoadSettingsOptions
    {
        /// <summary>
        /// Keys that will be used to filter.
        /// </summary>
        /// <remarks>See the documentation for this provider for details on the format of filter expressions</remarks>
        public string KeyFilter { get; set; }

        /// <summary>
        /// Label of the settings to be load.
        /// </summary>
        public string Label { get; set; }
    }
}
