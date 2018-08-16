namespace Microsoft.Extensions.Configuration.Azconfig.Models
{
    public class KeyValueListener
    {
        /// <summary>
        /// Key of the key-value to be listened
        /// </summary>
        public string Key { get; set; }

        /// <summary>
        /// Label of the key-value to be listened
        /// </summary>
        public string Label { get; set; }

        /// <summary>
        /// Poll interval in milliseconds of the key-value to be listened.
        /// </summary>
        public int PollInterval { get; set; }
    }
}
