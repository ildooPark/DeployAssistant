using DeployAssistant.Model;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using WPF = System.Windows;

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
                WPF.MessageBox.Show("Failed to Initialize MD5");
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
            string dstHashString = BitConverter.ToString(srcHashBytes).Replace("-", "");
            result = (srcHashString, dstHashString);
            return srcHashString == dstHashString;
        }
        public string GetFileMD5CheckSum(string projectPath, string srcFileRelPath)
        {
            byte[] srcHashBytes;
            string srcFileFullPath = Path.Combine(projectPath, srcFileRelPath);
            using MD5 md5 = MD5.Create();
            if (md5 == null)
            {
                WPF.MessageBox.Show($"Failed to Initialize MD5 for file {srcFileRelPath}");
                return "";
            }
            using (var srcStream = File.OpenRead(srcFileFullPath))
            {
                srcHashBytes = md5.ComputeHash(srcStream);
            }
            md5.Dispose();
            return BitConverter.ToString(srcHashBytes).Replace("-", "");
        }
        public async Task GetFileMD5CheckSumAsync(ProjectFile file)
        {
            try
            {
                byte[] srcHashBytes;
                using MD5 md5 = MD5.Create();
                if (md5 == null)
                {
                    WPF.MessageBox.Show("Failed to Initialize MD5 Async");
                    return;
                }
                using (var srcStream = File.OpenRead(file.DataAbsPath))
                {
                    srcHashBytes = await md5.ComputeHashAsync(srcStream);
                }
                string resultHash = BitConverter.ToString(srcHashBytes).Replace("-", "");
                file.DataHash = resultHash;
                md5.Dispose();
            }
            catch (Exception ex)
            {
                WPF.MessageBox.Show($"Error occured {ex.Message} \nwhile Computing hash async by this file {file.DataName}");
            }
        }
        public void GetFileMD5CheckSum(ProjectFile file)
        {
            try
            {
                byte[] srcHashBytes;
                using MD5 md5 = MD5.Create();
                if (md5 == null)
                {
                    WPF.MessageBox.Show("Failed to Initialize MD5");
                    return;
                }
                using (var srcStream = File.OpenRead(file.DataAbsPath))
                {
                    srcHashBytes = md5.ComputeHash(srcStream);
                }
                string resultHash = BitConverter.ToString(srcHashBytes).Replace("-", "");
                file.DataHash = resultHash;
                md5.Dispose();
            }
            catch (Exception ex)
            {
                WPF.MessageBox.Show($"Error occured {ex.Message} \nwhile Computing hash by this file {file.DataName}");
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
                    WPF.MessageBox.Show("Failed to Initialize MD5 Async");
                    return null;
                }
                using (var srcStream = File.OpenRead(fileFullPath))
                {
                    srcHashBytes = await md5.ComputeHashAsync(srcStream);
                }
                string resultHash = BitConverter.ToString(srcHashBytes).Replace("-", "");
                md5.Dispose();
                return resultHash;
            }
            catch (Exception ex)
            {
                WPF.MessageBox.Show($"Error occured {ex.Message} \nwhile Computing hash async by this file {Path.GetFileName(fileFullPath)}");
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
            using SHA256 sha256 = SHA256.Create();
            if (sha256 == null)
            {
                WPF.MessageBox.Show($"Failed to Initialize MD5 for ProjectData Hash {projectData.ProjectName}");
                return "";
            }
            byte[] filesByte = Encoding.UTF8.GetBytes((string) filesListWithHash.ToString());
            filesListWithHash.Clear();
            for (int i = 0; i < filesByte.Length; i++)
            {
                filesListWithHash.Append(filesByte[i].ToString("x2")); // "x2" formats the byte as a hexadecimal string
            }
            return filesListWithHash.ToString();
        }
    }
}
