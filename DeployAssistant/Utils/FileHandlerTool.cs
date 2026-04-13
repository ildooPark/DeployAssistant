using DeployAssistant.DataComponent;
using DeployAssistant.Interfaces;
using DeployAssistant.Model;
using System.IO;
using System.Text;
using System.Text.Json;

namespace DeployAssistant.Utils
{
    public class FileHandlerTool
    {
        public bool TrySerializeProjectData(ProjectData data, string filePath)
        {
            try
            {
                var jsonData = JsonSerializer.Serialize(data);
                var base64EncodedData = Convert.ToBase64String(Encoding.UTF8.GetBytes(jsonData));
                File.WriteAllText(filePath, base64EncodedData);
                return true; 
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error serializing ProjectData: " + ex.Message);
                return false;
            }
        }
        public bool TryDeserializeProjectData(string filePath, out ProjectData? projectData)
        {
            try
            {
                var jsonDataBase64 = File.ReadAllText(filePath);
                var jsonDataBytes = Convert.FromBase64String(jsonDataBase64);
                string jsonString = System.Text.Encoding.UTF8.GetString(jsonDataBytes);
                ProjectData? data = JsonSerializer.Deserialize<ProjectData>(jsonString);
                if (data != null)
                {
                    projectData = data;
                    return true; 
                }
                projectData = null;
                return false; 
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error deserializing ProjectData: " + ex.Message);
                projectData = null; 
                return false;
            }
        }
        public bool TrySerializeProjectMetaData(ProjectMetaData data, string filePath)
        {
            try
            {
                var jsonData = JsonSerializer.Serialize(data);
                var base64EncodedData = Convert.ToBase64String(Encoding.UTF8.GetBytes(jsonData));
                File.WriteAllText(filePath, base64EncodedData);
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error serializing ProjectMetaData: " + ex.Message);
                return false; 
            }
        }
        public bool TryDeserializeProjectMetaData(string filePath, out ProjectMetaData? projectMetaData)
        {
            try
            {
                var jsonDataBase64 = File.ReadAllText(filePath);
                var jsonDataBytes = Convert.FromBase64String(jsonDataBase64);
                string jsonData = System.Text.Encoding.UTF8.GetString(jsonDataBytes);
                ProjectMetaData? data = JsonSerializer.Deserialize<ProjectMetaData>(jsonData);
                if (data != null)
                {
                    projectMetaData = data;
                    return true;
                }
                else
                    projectMetaData = null;
                return false; 
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error deserializing ProjectMetaData: " + ex.Message);
                projectMetaData = null; 
                return false;
            }
        }
        public bool TrySerializeJsonData<T>(string filePath, in T? serializingObject)
        {
            try
            {
                var jsonOption = new JsonSerializerOptions { WriteIndented = true };
                var jsonData = JsonSerializer.Serialize(serializingObject, jsonOption);
                File.WriteAllText(filePath, jsonData);
                return true; 
            }
            catch (Exception ex)
            {
                return false;
            }
        }
        public bool TryDeserializeJsonData<T>(string filePath, out T? serializingObject)
        {
            try
            {
                var jsonDataBytes = File.ReadAllBytes(filePath);
                T? serializingObj = JsonSerializer.Deserialize<T>(jsonDataBytes);
                if (serializingObj != null)
                {
                    serializingObject = serializingObj;
                    return true;
                }
                else
                {
                    serializingObject = default;
                    return false;
                }
            }
            catch (Exception ex)
            {
                serializingObject = default;
                return false;
            }
        }
        public bool TryApplyFileChanges(List<ChangedFile> Changes)
        {
            if (Changes == null) return false;
            try
            {
                foreach (ChangedFile file in Changes)
                {
                    if ((file.DataState & DataState.IntegrityChecked) != 0) continue;
                    bool result = HandleData(file.SrcFile, file.DstFile, file.DataState);
                    if (!result) return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Couldn't Process File Changes : {ex.Message}");
                return false;
            }
        }
        public bool HandleData(IProjectData dstData, DataState state)
        {
            bool result;
            if (dstData.DataType == ProjectDataType.File)
            {
                result = HandleFile(null, dstData.DataAbsPath, state);
            }
            else
            {
                result = HandleDirectory(null, dstData.DataAbsPath, state);
            }
            return result; 
        }
        public bool HandleData(IProjectData? srcData, IProjectData dstData, DataState state)
        {
            bool result;
            if (dstData.DataType == ProjectDataType.File)
            {
                result = HandleFile(srcData?.DataAbsPath, dstData.DataAbsPath, state);
            }
            else
            {
                result = HandleDirectory(srcData?.DataAbsPath, dstData.DataAbsPath, state);
            }
            return result;
        }
        public bool HandleData(string? srcPath, string dstPath, ProjectDataType type, DataState state)
        {
            bool result; 
            if (type == ProjectDataType.File)
            {
                result = HandleFile(srcPath, dstPath, state);
            }
            else
            {
                result = HandleDirectory(srcPath, dstPath, state);
            }
            return result;
        }
        public void HandleData(string? srcPath, IProjectData dstData, DataState state)
        {
            if (dstData.DataType == ProjectDataType.File)
            {
                HandleFile(srcPath, dstData.DataAbsPath, state);
            }
            else
            {
                HandleDirectory(srcPath, dstData.DataAbsPath, state);
            }
        }
        public bool HandleDirectory(string? srcPath, string dstPath, DataState state)
        {
            try
            {
                if ((state & DataState.Deleted) != 0)
                {
                    if (Directory.Exists(dstPath))
                        Directory.Delete(dstPath, true);
                }
                else
                {
                    if (!Directory.Exists(dstPath))
                        Directory.CreateDirectory(dstPath);
                }
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message); return false; 
            }
        }
        public bool HandleFile(string? srcPath, string dstPath, DataState state)
        {
            try
            {
                if ((state & DataState.Deleted) != 0)
                {
                    if (File.Exists(dstPath))
                        File.Delete(dstPath);
                    return true; 
                }
                if (srcPath == null)
                {
                    MessageBox.Show($"Source File is null while File Handle state is {state.ToString()}");
                    return false;
                }
                if ((state & DataState.Added) != 0)
                {
                    if (!Directory.Exists(Path.GetDirectoryName(dstPath))) 
                        Directory.CreateDirectory(Path.GetDirectoryName(dstPath));
                    if (!File.Exists(dstPath))
                        File.Copy(srcPath, dstPath, true);
                }
                else
                {
                    if (!Directory.Exists(Path.GetDirectoryName(dstPath)))
                        Directory.CreateDirectory(Path.GetDirectoryName(dstPath));
                    if (srcPath == dstPath)
                    {
                        //MessageBox.Show($"Source File and Dst File path is same for {state.ToString()}, {dstPath}");
                        return false; 
                    }
                    File.Copy(srcPath, dstPath, true);
                }
                return true; 
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message); return false; 
            }
        }

        public bool MoveFile(string? srcPath, string dstPath)
        {
            try
            {
                if (!Directory.Exists(Path.GetDirectoryName(dstPath)))
                    Directory.CreateDirectory(Path.GetDirectoryName(dstPath));
                if (srcPath != dstPath)
                    File.Move(srcPath, dstPath, true); 
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Couldn't Move File {ex.Message}");
                return false; 
            }
        }
    }
}