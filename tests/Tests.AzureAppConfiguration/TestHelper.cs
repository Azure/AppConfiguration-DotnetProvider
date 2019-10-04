using Azure;
using Azure.Data.AppConfiguration;
using Moq;
using System;
using System.Collections.Generic;
using System.Text;

namespace Tests.AzureAppConfiguration
{
    class TestHelpers
    {
        static public string CreateMockEndpointString()
        {
            byte[] toEncodeAsBytes = Encoding.ASCII.GetBytes("secret");
            string returnValue = Convert.ToBase64String(toEncodeAsBytes);
            return $"Endpoint=https://xxxxx;Id=b1d9b31;Secret={returnValue}";
        }
    }

    class MockAsyncPageable : AsyncPageable<ConfigurationSetting>
    {
        private readonly List<ConfigurationSetting> _collection;

        public MockAsyncPageable(List<ConfigurationSetting> collection)
        {
            _collection = collection;
        }

#pragma warning disable 1998
        public async override IAsyncEnumerable<Page<ConfigurationSetting>> AsPages(string continuationToken = null, int? pageSizeHint = null)
#pragma warning restore 1998
        {
            yield return new Page<ConfigurationSetting>(_collection, null, new Mock<Response>().Object);

        }
    }

    class MockPageable : Pageable<ConfigurationSetting>
    {
        private readonly List<ConfigurationSetting> _collection;

        public MockPageable(List<ConfigurationSetting> collection)
        {
            _collection = collection;
        }

        public override IEnumerable<Page<ConfigurationSetting>> ByPage(string continuationToken = null, int? pageSizeHint = null)
        {
            yield return new Page<ConfigurationSetting>(_collection, null, new Mock<Response>().Object);
        }
    }
}
