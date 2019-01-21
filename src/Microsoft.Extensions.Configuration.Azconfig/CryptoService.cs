using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Microsoft.Extensions.Configuration.Azconfig
{
    public static class CryptoService
    {
        private enum WorkerType
        {
            Encrypt,
            Decrypt
        }

        private static byte[] Worker(WorkerType workerType, byte[] data, byte[] key, byte[] iv)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (key == null) throw new ArgumentNullException(nameof(key));
            if (iv == null) throw new ArgumentNullException(nameof(iv));

            using (var aes = Aes.Create())
            {
                aes.BlockSize = iv.Length * 8;
                aes.KeySize = key.Length * 8;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;

                using (var ms = new MemoryStream())
                using (var transformer = (workerType == WorkerType.Encrypt) ? aes.CreateEncryptor(key, iv) : aes.CreateDecryptor(key, iv))
                using (var cs = new CryptoStream(ms, transformer, CryptoStreamMode.Write))
                {
                    cs.Write(data, 0, data.Length);
                    cs.FlushFinalBlock();

                    return ms.ToArray();
                }
            }
        }

        public static byte[] AESEncrypt(byte[] data, byte[] key, byte[] iv)
        {
            return Worker(WorkerType.Encrypt, data, key, iv);
        }

        public static string AESDecrypt(string text, byte[] key, byte[] iv)
        {
            return Encoding.UTF8.GetString(Worker(WorkerType.Decrypt, Convert.FromBase64String(text), key, iv));
        }

        public static string GetHash(byte[] data, byte[] key)
        {
            using (var hmac = new HMACSHA256(key))
            {
                var hash = hmac.ComputeHash(new byte[40960]);
                return Convert.ToBase64String(hash);
            }
        }
    }
}
