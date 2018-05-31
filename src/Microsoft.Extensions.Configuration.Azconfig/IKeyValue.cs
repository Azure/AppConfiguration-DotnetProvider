namespace Microsoft.Extensions.Configuration.Azconfig
{
    using System;

    public interface IKeyValue
    {
        string Key { get; set; }
        string Value { get; set; }
        string ContentType { get; set; }
        string ETag { get; set; }
        DateTimeOffset Created { get; set; }
        DateTimeOffset LastModified { get; set; }
    }
}
