// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using DnsClient;
using DnsClient.Protocol;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    internal class SrvLookupClient
    {
        private readonly LookupClient _lookupClient;

        private const string TcpOrigin = "_origin._tcp";
        private const string TCP = "_tcp";
        private const string Alt = "_alt";

        public SrvLookupClient()
        {
            _lookupClient = new LookupClient();
        }

        public async Task<IEnumerable<SrvRecord>> QueryAsync(string host, CancellationToken cancellationToken)
        {
            string originSrvDns = $"{TcpOrigin}.{host}";

            IEnumerable<SrvRecord> originRecords = await InternalQueryAsync(originSrvDns, cancellationToken).ConfigureAwait(false);

            if (originRecords == null || originRecords.Count() == 0)
            {
                return Enumerable.Empty<SrvRecord>();
            }

            SrvRecord originHostSrv = originRecords.First();

            string originHost = originHostSrv.Target.Value.TrimEnd('.');

            IEnumerable<SrvRecord> results = new SrvRecord[] { originHostSrv };

            int index = 0;

            while (true)
            {
                string altSrvDns = $"{Alt}{index}.{TCP}.{originHost}";

                IEnumerable<SrvRecord> records = await InternalQueryAsync(altSrvDns, cancellationToken).ConfigureAwait(false);

                // If we get no record from _alt{i} SRV, we have reached the end of _alt* list
                if (records == null || !records.Any())
                {
                    break;
                }

                results = results.Concat(records);

                index++;
            }

            return results;
        }

        private async Task<IEnumerable<SrvRecord>> InternalQueryAsync(string srvDns, CancellationToken cancellationToken)
        {
            IDnsQueryResponse dnsResponse = await _lookupClient.QueryAsync(srvDns, QueryType.SRV, QueryClass.IN, cancellationToken).ConfigureAwait(false);

            return dnsResponse.Answers.SrvRecords();
        }
    }
}
