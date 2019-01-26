using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace Microsoft.Extensions.Configuration.Azconfig
{
    public class OfflineFileCache : OfflineCache
    {
        private string _localCachePath = null;
        private const int ERROR_SHARING_VIOLATION = unchecked((int)0x80070020);
        private const int retryMax = 20;
        private const int delayRange = 50;

        private const string dataProp = "d";
        private const string hashProp = "h";
        private const string scoprProp = "s";

        public OfflineFileCache(OfflineCacheOptions options)
        {
            _localCachePath = options.Target ?? throw new ArgumentNullException(nameof(options.Target));
            if (!Path.IsPathRooted(_localCachePath) || !string.Equals(Path.GetFullPath(_localCachePath), _localCachePath))
            {
                throw new ArgumentException("Must be full path", nameof(options.Target));
            }

            if (!options.IsCryptoDataReady)
            {
                throw new ArgumentException("Crypto keys must be set");
            }

            Options = options;
        }

        public override string Import()
        {
            return this.DoImport();
        }

        private string DoImport(int retry = 0)
        {
            if (retry <= retryMax)
            {
                try
                {
                    string json = File.ReadAllText(_localCachePath);

                    JsonTextReader reader = new JsonTextReader(new StringReader(json));
                    string data = null, dataHash = null, scopeHash = null;
                    while (reader.Read())
                    {
                        if ((reader.TokenType == JsonToken.PropertyName) && (reader.Value != null))
                        {
                            switch (reader.Value.ToString())
                            {
                                case dataProp:
                                    data = reader.ReadAsString();
                                    break;
                                case hashProp:
                                    dataHash = reader.ReadAsString();
                                    break;
                                case scoprProp:
                                    scopeHash = reader.ReadAsString();
                                    break;
                                default:
                                    return null;
                            }
                        }
                    }

                    if ((data != null) && (dataHash != null) && (scopeHash != null))
                    {
                        string newScopeHash = CryptoService.GetHash(Encoding.UTF8.GetBytes(Options.ScopeToken ?? ""), Options.SignKey);
                        if (string.CompareOrdinal(scopeHash, newScopeHash) == 0)
                        {
                            string newDataHash = CryptoService.GetHash(Convert.FromBase64String(data), Options.SignKey);
                            if (string.CompareOrdinal(dataHash, newDataHash) == 0)
                            {
                                return CryptoService.AESDecrypt(data, Options.Key, Options.IV);
                            }
                        }
                    }
                }
                catch (IOException ex)
                {
                    if (ex.HResult == ERROR_SHARING_VIOLATION)
                    {
                        Task.Delay(new Random().Next(delayRange)).Wait();
                        return this.DoImport(++retry);
                    }

                    throw;
                }
            }

            return null;
        }

        public override void Export(string data)
        {
            if ((DateTime.Now - File.GetLastWriteTime(_localCachePath)) > TimeSpan.FromMilliseconds(1000))
            {
                Task.Run(async () =>
                {
                    string tempFile = Path.Combine(Path.GetDirectoryName(_localCachePath), $"azconfigTemp-{Path.GetRandomFileName()}");

                    var dataBytes = Encoding.UTF8.GetBytes(data);
                    var encryptedBytes = CryptoService.AESEncrypt(dataBytes, Options.Key, Options.IV);
                    var dataHash = CryptoService.GetHash(encryptedBytes, Options.SignKey);
                    var scopeHash = CryptoService.GetHash(Encoding.UTF8.GetBytes(Options.ScopeToken ?? ""), Options.SignKey);

                    StringBuilder sb = new StringBuilder();
                    using (var sw = new StringWriter(sb))
                    using (var jtw = new JsonTextWriter(sw))
                    {
                        jtw.WriteStartObject();
                        jtw.WritePropertyName(dataProp);
                        jtw.WriteValue(Convert.ToBase64String(encryptedBytes));
                        jtw.WritePropertyName(hashProp);
                        jtw.WriteValue(dataHash);
                        jtw.WritePropertyName(scoprProp);
                        jtw.WriteValue(scopeHash);
                        jtw.WriteEndObject();
                    }

                    File.WriteAllText(tempFile, sb.ToString());

                    await this.DoUpdate(tempFile);
                });
            }
        }

        private async Task DoUpdate(string tempFile, int retry = 0)
        {
            if (retry <= retryMax)
            {
                try
                {
                    File.Delete(_localCachePath);
                    File.Move(tempFile, _localCachePath);
                }
                catch (IOException ex)
                {
                    if (ex.HResult == ERROR_SHARING_VIOLATION)
                    {
                        await Task.Delay(new Random().Next(delayRange));
                        await this.DoUpdate(tempFile, ++retry);
                    }
                    else
                    {
                        throw;
                    }
                }
            }
            else
            {
                File.Delete(tempFile);
            }
        }
    }
}
