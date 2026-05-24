using DeployAssistant.Model;
using System.Buffers;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

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
            using MD5 md5 = MD5.Create();
            if (md5 == null)
            {
                Trace.TraceError("Failed to Initialize MD5");
                result = (null, null);
                return false;
            }
            using (var srcStream = File.OpenRead(srcFile))
            {
                srcHashBytes = md5.ComputeHash(srcStream);
            }
            using (var dstStream = File.OpenRead(dstFile))
            {
                dstHashBytes = md5.ComputeHash(dstStream);
            }
            string srcHashString = BitConverter.ToString(srcHashBytes).Replace("-", "");
            string dstHashString = BitConverter.ToString(dstHashBytes).Replace("-", "");
            result = (srcHashString, dstHashString);
            return srcHashString == dstHashString;
        }
        /// <summary>
        /// Reads <paramref name="srcFileRelPath"/> (relative to <paramref name="projectPath"/>)
        /// and returns its MD5 hex digest.  Retries up to <paramref name="maxRetries"/> times
        /// with <paramref name="retryDelayMs"/> milliseconds between attempts when the file
        /// can't be opened (locked, transient IO).  Returns <c>""</c> if all attempts fail
        /// — callers (FileManager integrity-check loops) use the empty string as the
        /// fallback sentinel to engage metadata-only verification.
        /// </summary>
        /// <remarks>
        /// Negative <paramref name="maxRetries"/> or <paramref name="retryDelayMs"/> values
        /// are clamped to zero — a negative input behaves as "no retry / no delay".
        /// </remarks>
        public string GetFileMD5CheckSum(string projectPath, string srcFileRelPath, int maxRetries = 3, int retryDelayMs = 200)
        {
            if (maxRetries < 0) maxRetries = 0;
            if (retryDelayMs < 0) retryDelayMs = 0;
            string srcFileFullPath = Path.Combine(projectPath, srcFileRelPath);
            int totalAttempts = 1 + maxRetries;
            for (int attempt = 0; attempt < totalAttempts; attempt++)
            {
                try
                {
                    byte[] srcHashBytes;
                    using MD5 md5 = MD5.Create();
                    if (md5 == null)
                    {
                        Trace.TraceError($"Failed to Initialize MD5 for file {srcFileRelPath}");
                        return "";
                    }
                    using (var srcStream = File.OpenRead(srcFileFullPath))
                    {
                        srcHashBytes = md5.ComputeHash(srcStream);
                    }
                    return BitConverter.ToString(srcHashBytes).Replace("-", "");
                }
                catch (Exception ex)
                {
                    bool isLastAttempt = attempt == totalAttempts - 1;
                    if (isLastAttempt)
                    {
                        Trace.TraceWarning($"Hash failed for '{srcFileRelPath}' under '{projectPath}' after {totalAttempts} attempt(s): {ex.GetType().Name}: {ex.Message}");
                        return "";
                    }
                    if (retryDelayMs > 0)
                    {
                        Thread.Sleep(retryDelayMs);
                    }
                }
            }
            return "";  // unreachable: the loop above is guaranteed to return (totalAttempts >= 1 after clamping)
        }
        public async Task GetFileMD5CheckSumAsync(ProjectFile file)
        {
            try
            {
                byte[] srcHashBytes;
                using MD5 md5 = MD5.Create();
                if (md5 == null)
                {
                    Trace.TraceError("Failed to Initialize MD5 Async");
                    return;
                }
                using (var srcStream = File.OpenRead(file.DataAbsPath))
                {
                    srcHashBytes = await Task.Run(() => md5.ComputeHash(srcStream));
                }
                string resultHash = BitConverter.ToString(srcHashBytes).Replace("-", "");
                file.DataHash = resultHash;
                md5.Dispose();
            }
            catch (Exception ex)
            {
                Trace.TraceError($"Error occured {ex.Message} \nwhile Computing hash async by this file {file.DataName}");
            }
        }
        /// <summary>
        /// Computes the MD5 hex digest of <paramref name="file"/>'s on-disk content and
        /// assigns it to <c>file.DataHash</c>.  Retries up to <paramref name="maxRetries"/>
        /// times with <paramref name="retryDelayMs"/> milliseconds between attempts when
        /// the file can't be opened.  Leaves <c>DataHash</c> unchanged (typically empty,
        /// per the caller's pre-clear convention) if all attempts fail.
        /// </summary>
        /// <remarks>
        /// Negative <paramref name="maxRetries"/> or <paramref name="retryDelayMs"/> values
        /// are clamped to zero — a negative input behaves as "no retry / no delay".
        /// </remarks>
        public void GetFileMD5CheckSum(ProjectFile file, int maxRetries = 3, int retryDelayMs = 200)
        {
            if (maxRetries < 0) maxRetries = 0;
            if (retryDelayMs < 0) retryDelayMs = 0;
            int totalAttempts = 1 + maxRetries;
            for (int attempt = 0; attempt < totalAttempts; attempt++)
            {
                try
                {
                    byte[] srcHashBytes;
                    using MD5 md5 = MD5.Create();
                    if (md5 == null)
                    {
                        Trace.TraceError("Failed to Initialize MD5");
                        return;
                    }
                    using (var srcStream = File.OpenRead(file.DataAbsPath))
                    {
                        srcHashBytes = md5.ComputeHash(srcStream);
                    }
                    file.DataHash = BitConverter.ToString(srcHashBytes).Replace("-", "");
                    return;
                }
                catch (Exception ex)
                {
                    bool isLastAttempt = attempt == totalAttempts - 1;
                    if (isLastAttempt)
                    {
                        Trace.TraceError($"Error occured {ex.Message} \nwhile Computing hash by this file {file.DataName} (after {totalAttempts} attempt(s))");
                        return;
                    }
                    if (retryDelayMs > 0)
                    {
                        Thread.Sleep(retryDelayMs);
                    }
                }
            }
        }
        public async Task<string?> GetFileMD5CheckSumAsync(string fileFullPath)
        {
            try
            {
                byte[] srcHashBytes;
                using MD5 md5 = MD5.Create();
                if (md5 == null)
                {
                    Trace.TraceError("Failed to Initialize MD5 Async");
                    return null;
                }
                using (var srcStream = File.OpenRead(fileFullPath))
                {
                    srcHashBytes = await Task.Run(() => md5.ComputeHash(srcStream));
                }
                string resultHash = BitConverter.ToString(srcHashBytes).Replace("-", "");
                md5.Dispose();
                return resultHash;
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
            using (SHA256 sha256Hash = SHA256.Create())
            {
                byte[] bytes = sha256Hash.ComputeHash(Encoding.UTF8.GetBytes(userID));

                // Convert the hash bytes to a 10-character string by taking the first 5 bytes (40 bits) of the hash
                StringBuilder builder = new StringBuilder();
                for (int i = 0; i < 5; i++)
                {
                    builder.Append(bytes[i].ToString("x2"));
                }
                return builder.ToString();
            }
        }
        public string GetUniqueProjectDataID(ProjectData projectData)
        {
            StringBuilder filesListWithHash = new StringBuilder();
            foreach (ProjectFile file in projectData.ProjectFiles.Values)
            {
                filesListWithHash.Append($"{file.DataRelPath}\\{file.DataHash}");
            }
            string inputStr = filesListWithHash.ToString();
            using SHA256 sha256 = SHA256.Create();
            if (sha256 == null)
            {
                Trace.TraceError($"Failed to Initialize SHA256 for ProjectData Hash {projectData.ProjectName}");
                return "";
            }
            // Rent a byte[] from the pool for UTF-8 encoding to avoid a one-off heap allocation.
            int byteCount = Encoding.UTF8.GetByteCount(inputStr);
            byte[] rented = ArrayPool<byte>.Shared.Rent(byteCount);
            try
            {
                int written = Encoding.UTF8.GetBytes(inputStr, 0, inputStr.Length, rented, 0);
                byte[] hashBytes = sha256.ComputeHash(rented, 0, written);
                return BitConverter.ToString(hashBytes).Replace("-", "");
            }
            finally
            {
                // Clear before returning to avoid leaving sensitive path data in the shared pool.
                ArrayPool<byte>.Shared.Return(rented, clearArray: true);
            }
        }
    }
}
