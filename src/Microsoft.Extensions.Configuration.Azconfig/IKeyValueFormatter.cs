namespace Microsoft.Extensions.Configuration.Azconfig
{
    public interface IKeyValueFormatter
    {
        string Format(IKeyValue keyValue);
    }
}
