namespace Microsoft.Extensions.Configuration.Azconfig
{
    class KeyValueFormatter : IKeyValueFormatter
    {
        public string Format(IKeyValue keyValue)
        {
            return keyValue.Value;
        }
    }
}
