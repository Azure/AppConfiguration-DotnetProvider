using System;
using System.Threading;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    static class RandomGenerator
    {
        private static readonly Random _global = new Random();
        private static readonly ThreadLocal<Random> _rnd = new ThreadLocal<Random>(() =>
        {
            int seed;

            lock (_global)
            {
                seed = _global.Next();
            }

            return new Random(seed);
        });

        public static double NextDouble()
        {
            return _rnd.Value.NextDouble();
        }
    }
}
