namespace Microsoft.Extensions.Configuration.AppConfig
{
    using System;

    public class KeyValueListener
    {
        public string Key { get; set; }

        public int PollInterval { get; set; }

        public Action Callback { get; set; }
    }
}
