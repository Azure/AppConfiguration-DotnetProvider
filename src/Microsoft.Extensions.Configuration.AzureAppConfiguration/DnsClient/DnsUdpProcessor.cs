using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.DnsClient
{
    internal class DnsUdpProcessor: DnsProcessor
    {
        public override async Task<IReadOnlyCollection<SrvRecord>> QueryAsync(IPEndPoint endpoint, string query, CancellationToken cancellationToken)
        {
            var udpClient = new UdpClient(endpoint.AddressFamily);

            var resultRecords = new List<SrvRecord>();

            try
            {
                using var callback = cancellationToken.Register(() =>
                {
#if !NET45
                    udpClient.Dispose();
#else
                    udpClient.Close();
#endif
                });

                var originRecords = await QueryAsyncInternal(endpoint, $"{OriginSrvPrefix}.{query}", udpClient, cancellationToken).ConfigureAwait(false);
                string originHost = query;
                if (originRecords != null && originRecords.Count > 0)
                {
                    originHost = originRecords.First().Target;
                }
                else
                {
                    // If can't get any records from _origin query, we should return;
                    return resultRecords;
                }

                IReadOnlyCollection<SrvRecord> altRecords = null;
                ushort index = 0;

                while (true)
                {
                    altRecords = await QueryAsyncInternal(endpoint, $"{AlternativeSrvPrefix(index)}.{originHost}", udpClient, cancellationToken).ConfigureAwait(false);

                    if (altRecords == null || altRecords.Count == 0)
                    {
                        break;
                    }

                    resultRecords.AddRange(altRecords);

                    index++;
                }

                return resultRecords;
            }
            catch (SocketException se) when (se.SocketErrorCode == SocketError.OperationAborted)
            {
                throw new TimeoutException();
            }
            catch (ObjectDisposedException)
            {
                // we disposed it in case of a timeout request, just indicate it actually timed out.
                throw new TimeoutException();
            }
            catch (DnsResponseException)
            {
                return resultRecords;
            }
            finally
            {
                try
                {
#if !NET45
                    udpClient.Dispose();
#else
                    udpClient.Close();
#endif
                }
                catch { }
            }
        }

        private async Task<IReadOnlyCollection<SrvRecord>> QueryAsyncInternal(IPEndPoint endpoint, string query, UdpClient udpClient, CancellationToken cancellationToken)
        {
            ushort requestId = GetRandomRequestId();
            var srvRequset = BuildDnsQueryMessage(query, requestId);

            int retry = 0;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    await udpClient.SendAsync(srvRequset, srvRequset.Length, endpoint).ConfigureAwait(false);

#if NET6_0_OR_GREATER
                    UdpReceiveResult received = await udpClient.ReceiveAsync(cancellationToken).ConfigureAwait(false);
#else
                    UdpReceiveResult received = await udpClient.ReceiveAsync().ConfigureAwait(false);
#endif
                    var response = ProcessDnsResponse(received.Buffer, requestId);

                    return response;
                }
                catch (DnsResponseException)
                {
                    // No need to retry with DnsResponseException
                    throw;
                }
                catch (Exception)
                {
                    if (retry == RetryAttempt)
                    {
                        throw;
                    }
                    retry++;
                }
            }
        }
    }
}
