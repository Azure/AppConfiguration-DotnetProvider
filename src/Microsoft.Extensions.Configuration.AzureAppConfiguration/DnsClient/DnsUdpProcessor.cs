using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
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

                var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(TimeSpan.FromSeconds(5));

                var originRecords = await QueryAsyncInternal(endpoint, $"{OriginSrvPrefix}.{query}", udpClient, cts.Token).ConfigureAwait(false);
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
                    altRecords = await QueryAsyncInternal(endpoint, $"{AlternativeSrvPrefix(index)}.{originHost}", udpClient, cts.Token).ConfigureAwait(false);

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
            catch (OperationCanceledException)
            {
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
           
            cancellationToken.ThrowIfCancellationRequested();
                
#if NET6_0_OR_GREATER
            await udpClient.SendAsync(srvRequset, srvRequset.Length, endpoint, cancellationToken).ConfigureAwait(false);

            UdpReceiveResult received = await udpClient.ReceiveAsync(cancellationToken).ConfigureAwait(false);
#else
            await udpClient.SendAsync(srvRequset, srvRequset.Length, endpoint).ConfigureAwait(false);

            UdpReceiveResult received = await udpClient.ReceiveAsync().ConfigureAwait(false);
#endif
            var response = ProcessDnsResponse(received.Buffer, requestId);

            return response;
        }
    }
}
