using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.Extensions.Configuration.Azconfig
{
    public class FileOfflineCache : OfflineCacheProvider
    {
        private string _localCachePath = null;
        private const int ERROR_SHARING_VIOLATION = unchecked((int)0x80070020);
        private const int retryMax = 20;
        private const int delayRange = 50;

        public FileOfflineCache()
        {
            // Generate default cahce file name
            string homePath = Environment.GetEnvironmentVariable("HOME");
            if (Directory.Exists(homePath))
            {
                string dataPath = Path.Combine(homePath, "data");
                if (Directory.Exists(dataPath))
                {
                    string cahcePath = Path.Combine(dataPath, "azconfigCache");
                    if (!Directory.Exists(cahcePath))
                    {
                        Directory.CreateDirectory(cahcePath);
                    }

                    string websiteName = Environment.GetEnvironmentVariable("WEBSITE_SITE_NAME");
                    if (websiteName != null)
                    {
                        byte[] hash = new byte[0];
                        using (var sha = SHA1.Create())
                        {
                            hash = sha.ComputeHash(Encoding.UTF8.GetBytes(websiteName));
                        }

                        var sb = new StringBuilder();
                        for (var i = 0; i < hash.Length; i++)
                        {
                            sb.Append(hash[i].ToString("X2"));
                        }

                        _localCachePath = Path.Combine(cahcePath, $"app{sb.ToString()}.json");
                    }
                }
            }

            if (_localCachePath == null)
            {
                throw new NotSupportedException("Only work under Azure App Service");
            }
        }

        public FileOfflineCache(string path)
        {
            _localCachePath = path ?? throw new ArgumentNullException(nameof(path));
            if (!Path.IsPathRooted(path) || !string.Equals(Path.GetFullPath(path), path))
            {
                throw new ArgumentException("Must be full path", nameof(path));
            }
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
                    return File.ReadAllText(_localCachePath);
                }
                catch (IOException ex)
                {
                    if (ex.HResult == ERROR_SHARING_VIOLATION)
                    {
                        Task.Delay(new Random().Next(delayRange)).Wait();
                        return this.DoImport(++retry);
                    }
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
                    File.WriteAllText(tempFile, data);

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
                }
            }
            else
            {
                File.Delete(tempFile);
            }
        }
    }
}
