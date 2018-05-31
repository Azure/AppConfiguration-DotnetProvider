namespace Tests.AppConfig
{
    using Microsoft.Extensions.Configuration.AppConfig;
    using System.Collections.Generic;

    class Fixture
    {
        public static IEnumerable<IKeyValue> GetData()
        {
            var kvs = new List<IKeyValue>();

            kvs.Add(new KeyValue()
            {
                Key = "ConnectionString",
                Value = "3.1"
            });

            kvs.Add(new KeyValue()
            {
                Key = "svc1/AppName",
                Value = "Contoso"
            });

            kvs.Add(new KeyValue()
            {
                Key = "svc1/Difficulty",
                Value = "Easy"
            });

            kvs.Add(new KeyValue()
            {
                Key = "svc2/AppName",
                Value = "Amerigo"
            });

            kvs.Add(new KeyValue()
            {
                Key = "svc2/Difficulty",
                Value = "Hard"
            });

            return kvs;
        }
    }
}
