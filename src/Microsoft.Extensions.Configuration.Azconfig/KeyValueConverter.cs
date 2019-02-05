using System;
using Microsoft.Azconfig.Client;
using Newtonsoft.Json.Converters;

namespace Microsoft.Extensions.Configuration.Azconfig
{
    internal class KeyValueConverter : CustomCreationConverter<IKeyValue>
    {
        public override IKeyValue Create(Type _)
        {
            return new KeyValue(null);
        }
    }
}
