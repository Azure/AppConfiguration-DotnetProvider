using System;
using System.Text;

namespace Tests.AzureAppConfiguration
{
    class TestHelpers
    {
        static public string CreateMockEndpointString()
        {
            byte[] toEncodeAsBytes = Encoding.ASCII.GetBytes("secret");
            string returnValue = Convert.ToBase64String(toEncodeAsBytes);
            return $"Endpoint=https://t-sebyusecondresource.azconfig.io;Id=2-l4-s0:OWhCnARLzy2wGn1/+nEz;Secret={returnValue}";
        }
    }
}
