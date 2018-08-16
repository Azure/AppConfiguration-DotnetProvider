using System;
using System.Text;

namespace Tests.Azconfig
{
    class TestHelpers
    {
        static public string CreateMockEndpointString()
        {
            byte[] toEncodeAsBytes = Encoding.ASCII.GetBytes("secret");
            string returnValue = Convert.ToBase64String(toEncodeAsBytes);
            return $"Endpoint=https://contoso.azconfig.io;Id=b1d9b31;Secret={returnValue}";
        }
    }
}
