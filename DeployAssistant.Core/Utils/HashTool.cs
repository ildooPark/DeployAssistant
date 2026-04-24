using DeployAssistant.Model;
using System.Buffers;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace DeployAssistant.Utils
{
    public class HashTool
    {
        #region Binary Comparision Through MD5 CheckSum
        /// <summary>
        /// Returns true if content is the same. 
        /// </summary>
        /// <param name="srcFile"></param>
        /// <param name="dstFile"></param>
        /// <param name="result">First is srcHash, Second is dstHash</param>
        /// <returns></returns>
        public bool TryCompareMD5CheckSum(string? srcFile, string? dstFile, out (string?, string?) result)
        {
            if (srcFile == null || dstFile == null)
            {
                result = (null, null);
                return false;
            }
            byte[] srcHashBytes, dstHashBytes;
            using (var srcStream = File.OpenRead(srcFile))
            {
                srcHashBytes = MD5.HashData(srcStream);
            }
            using (var dstStream = File.OpenRead(dstFile))
            {
                dstHashBytes = MD5.HashData(dstStream);
            }
            string srcHashString = Convert.ToHexString(srcHashBytes);
            string dstHashString = Convert.ToHexString(dstHashBytes);
            result = (srcHashString, dstHashString);
            return srcHashString == dstHashString;
        }
        public string GetFileMD5CheckSum(string projectPath, string srcFileRelPath)
        {
            string srcFileFullPath = Path.Combine(projectPath, srcFileRelPath);
            using var srcStream = File.OpenRead(srcFileFullPath);
            return Convert.ToHexString(MD5.HashData(srcStream));
        }
        public async Task GetFileMD5CheckSumAsync(ProjectFile file)
        {
            try
            {
                using var srcStream = File.OpenRead(file.DataAbsPath);
                byte[] srcHashBytes = await MD5.HashDataAsync(srcStream);
                file.DataHash = Convert.ToHexString(srcHashBytes);
            }
            catch (Exception ex)
            {
                Trace.TraceError($"Error occured {ex.Message} \nwhile Computing hash async by this file {file.DataName}");
            }
        }
        public void GetFileMD5CheckSum(ProjectFile file)
        {
            try
            {
                using var srcStream = File.OpenRead(file.DataAbsPath);
                file.DataHash = Convert.ToHexString(MD5.HashData(srcStream));
            }
            catch (Exception ex)
            {
                Trace.TraceError($"Error occured {ex.Message} \nwhile Computing hash by this file {file.DataName}");
            }
        }
        public async Task<string?> GetFileMD5CheckSumAsync(string fileFullPath)
        {
            try
            {
                using var srcStream = File.OpenRead(fileFullPath);
                byte[] srcHashBytes = await MD5.HashDataAsync(srcStream);
                return Convert.ToHexString(srcHashBytes);
            }
            catch (Exception ex)
            {
                Trace.TraceError($"Error occured {ex.Message} \nwhile Computing hash async by this file {Path.GetFileName(fileFullPath)}");
                return null;
            }
        }
        #endregion
        public string GetUniqueComputerID(string userID)
        {
            // Use the exact byte count so the stackalloc threshold reflects real UTF-8 sizes,
            // not the worst-case estimate from GetMaxByteCount.
            int byteCount = Encoding.UTF8.GetByteCount(userID);
            Span<byte> inputBuf = byteCount <= 256 ? stackalloc byte[byteCount] : new byte[byteCount];
            Encoding.UTF8.GetBytes(userID, inputBuf);

            // Stack-allocate the 32-byte SHA-256 output buffer and use the allocation-free TryHashData overload.
            // Convert the hash bytes to a 10-character string by taking the first 5 bytes (40 bits) of the hash.
            Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
            if (!SHA256.TryHashData(inputBuf, hash, out _))
                throw new InvalidOperationException("SHA256.TryHashData failed unexpectedly.");
            return Convert.ToHexStringLower(hash[..5]);
        }
        public string GetUniqueProjectDataID(ProjectData projectData)
        {
            StringBuilder filesListWithHash = new StringBuilder();
            foreach (ProjectFile file in projectData.ProjectFiles.Values)
            {
                filesListWithHash.Append($"{file.DataRelPath}\\{file.DataHash}");
            }
            string input = filesListWithHash.ToString();

            // Rent a byte[] from the pool sized to the exact UTF-8 byte count to avoid unnecessary pool pressure.
            int byteCount = Encoding.UTF8.GetByteCount(input);
            byte[] rented = ArrayPool<byte>.Shared.Rent(byteCount);
            try
            {
                int written = Encoding.UTF8.GetBytes(input, rented);

                // Stack-allocate the 32-byte SHA-256 output buffer.
                Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
                if (!SHA256.TryHashData(rented.AsSpan(0, written), hash, out _))
                    throw new InvalidOperationException("SHA256.TryHashData failed unexpectedly.");
                return Convert.ToHexString(hash);
            }
            finally
            {
                // Clear before returning to avoid leaving sensitive path data in the shared pool.
                ArrayPool<byte>.Shared.Return(rented, clearArray: true);
            }
        }
    }
}
