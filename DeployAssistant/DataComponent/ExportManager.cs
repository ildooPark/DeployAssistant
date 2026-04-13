using DeployAssistant.Interfaces;
using DeployAssistant.Model;
using DeployAssistant.Utils;
using WPF = System.Windows;
using System.IO;
using System.IO.Compression;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace DeployAssistant.DataComponent
{
    public class ExportManager : IManager
    {
        private string? _currentProjectPath; 
        private Dictionary<string, ProjectFile>? _backupFilesDict;
        private FileHandlerTool _fileHandlerTool;
        public ExportManager() 
        {
            _fileHandlerTool = App.FileHandlerTool;
        }
        #region Manager Events
        /// <summary>
        /// string = Export Project Path
        /// </summary>
        public event Action<string>? ExportCompleteEventHandler;
        public event Action<MetaDataState>? ManagerStateEventHandler;
        #endregion
        public void Awake(){}
        public void ExportProjectVersionLog(ProjectData projectData)
        {
            string exportDstPath = GetExportProjectPath(projectData);
            string exportVersionLogPath = $"{exportDstPath}\\{projectData.UpdatedVersion}.VersionLog";
            bool exportResult = false;
            while (!exportResult)
            {
                if (!Directory.Exists(exportDstPath)) Directory.CreateDirectory(exportDstPath);
                exportResult = _fileHandlerTool.TrySerializeProjectData(projectData, exportVersionLogPath);
                if (!exportResult)
                {
                    var response = WPF.MessageBox.Show("Export Failed, Would you like to try again?", "Export Project Version Log", WPF.MessageBoxButton.YesNo);
                    if (response == WPF.MessageBoxResult.Yes)
                    {
                        continue;
                    }
                    else
                    {
                        WPF.MessageBox.Show("Export Canceled");
                        return;
                    }
                }
            }
            ExportCompleteEventHandler?.Invoke(exportDstPath);
        }
        public void ExportProject(ProjectData projectData)
        {
            if (_backupFilesDict == null)
            {
                ManagerStateEventHandler?.Invoke(MetaDataState.Idle);
                WPF.MessageBox.Show("Backup files are missing!, Make sure ProjectMetaData is Set");
                return;
            }
            ManagerStateEventHandler?.Invoke(MetaDataState.Exporting);
            bool exportResult = false;
            string? exportPath = null; 
            while (!exportResult)
            {
                exportResult = TryExportProject(projectData, out exportPath);
                if (!exportResult)
                {
                    var response = WPF.MessageBox.Show("Export Failed, Would you like to try again?", "Export Project", WPF.MessageBoxButton.YesNo);
                    if (response == WPF.MessageBoxResult.Yes)
                    {
                        continue;
                    }
                    else
                    {
                        WPF.MessageBox.Show("Export Canceled");
                        ManagerStateEventHandler?.Invoke(MetaDataState.Idle);
                        break;
                    }
                }
            }
            if (exportPath != null)
            {
                ManagerStateEventHandler?.Invoke(MetaDataState.Idle);
                ExportCompleteEventHandler?.Invoke(exportPath);
            }
        }
        private bool TryExportProject(ProjectData projectData, out string? exportPath)
        {
            try
            {
                if (_backupFilesDict == null)
                {
                    WPF.MessageBox.Show("Backup files are missing!, Make sure ProjectMetaData is Set");
                    exportPath = null; return false;
                }
                string exportDstPath = GetExportProjectPath(projectData);
                string exportZipPath = $"{exportDstPath}.zip";
                string exportProjDataPath = Path.Combine(exportDstPath, $"{projectData.UpdatedVersion}.VersionLog"); 
                int exportCount = 0;

                foreach (ProjectFile file in projectData.ProjectFiles.Values)
                {
                    if (file.DataType == ProjectDataType.Directory)
                    {

                        bool handleResult = _fileHandlerTool.HandleDirectory(null, Path.Combine(exportDstPath, file.DataRelPath), DataState.None);
                        if (!handleResult)
                        {
                            WPF.MessageBox.Show($"Export Failed! for file {file.DataName}!");
                            exportPath = null;  return false;
                        }
                        exportCount++;
                        continue;
                    }
                    if (!_backupFilesDict.TryGetValue(file.DataHash, out ProjectFile? backupFile))
                    {
                        WPF.MessageBox.Show($"Export Failed! for file {file.DataName}!");
                        exportPath = null; return false;
                    }
                    else
                    {
                        bool handleResult = _fileHandlerTool.HandleFile(backupFile.DataAbsPath, Path.Combine(exportDstPath, file.DataRelPath), DataState.None);
                        if (!handleResult)
                        {
                            WPF.MessageBox.Show($"Export Failed! for file {file.DataName}!");
                            exportPath = null; return false;
                        }
                        exportCount++;
                    }
                }
                if (exportCount <= 0)
                {
                    exportPath = null; 
                    return false;
                }
                _fileHandlerTool.TrySerializeProjectData(projectData, exportProjDataPath); 
                ZipFile.CreateFromDirectory(exportDstPath, exportZipPath);
                exportPath = Directory.GetParent(exportDstPath)?.ToString();
                return true; 
            }
            catch (Exception ex)
            {
                WPF.MessageBox.Show(ex.Message);
                exportPath = null; return false;
            }
        }
        public void ExportProjectFilesXLSX(ProjectData projectData, ICollection<ProjectFile> projectFiles)
        {
            string? exportPath; 
            if (projectFiles == null)
            {
                TryExportProjectFilesXLSX(projectData, out exportPath);
            }
            else
            {
                TryExportProjectFilesXLSX(projectData, projectFiles, out exportPath); 
            }
            ExportCompleteEventHandler?.Invoke(exportPath); 
        }

        private bool TryExportProjectFilesXLSX(ProjectData projectData, out string? exportPath)
        {
            var sortedProjectFiles = projectData.ProjectFiles.Values.ToList()
                .OrderBy(item => item.DataName).ToList();

            string xlsxFilePath = GetExportXLSXPath(projectData);
            string? xlsxFileDirPath = Path.GetDirectoryName(xlsxFilePath);
            if (!Directory.Exists(xlsxFilePath)) Directory.CreateDirectory(xlsxFilePath);
            try
            {
                using SpreadsheetDocument spreadsheetDocument = SpreadsheetDocument.Create(xlsxFilePath, SpreadsheetDocumentType.Workbook);
                // Add a WorkbookPart to the document
                WorkbookPart workbookPart = spreadsheetDocument.AddWorkbookPart();
                workbookPart.Workbook = new Workbook();

                // Add a WorksheetPart to the WorkbookPart
                WorksheetPart worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
                worksheetPart.Worksheet = new Worksheet(new SheetData());

                // Add Sheets to the Workbook
                Sheets sheets = spreadsheetDocument.WorkbookPart.Workbook.AppendChild(new Sheets());

                // Append a new worksheet and associate it with the workbook
                Sheet sheet = new Sheet() { Id = spreadsheetDocument.WorkbookPart.GetIdOfPart(worksheetPart), SheetId = 1, Name = "Project Files" };
                sheets.Append(sheet);

                // Get the SheetData
                SheetData sheetData = worksheetPart.Worksheet.GetFirstChild<SheetData>();

                // Add headers
                Row headerRow = new Row();
                headerRow.Append(new Cell(new InlineString(new Text("DataName"))));
                headerRow.Append(new Cell(new InlineString(new Text("DataType"))));
                headerRow.Append(new Cell(new InlineString(new Text("DataSize"))));
                headerRow.Append(new Cell(new InlineString(new Text("BuildVersion"))));
                headerRow.Append(new Cell(new InlineString(new Text("DeployedProjectVersion"))));
                headerRow.Append(new Cell(new InlineString(new Text("UpdatedTime"))));
                headerRow.Append(new Cell(new InlineString(new Text("DataState"))));
                headerRow.Append(new Cell(new InlineString(new Text("DataSrcPath"))));
                headerRow.Append(new Cell(new InlineString(new Text("DataRelPath"))));
                headerRow.Append(new Cell(new InlineString(new Text("DataHash"))));
                sheetData.AppendChild(headerRow);

                // Populate data
                foreach (var item in sortedProjectFiles)
                {
                    Row newRow = new Row();
                    newRow.Append(new Cell(new InlineString(new Text(item.DataName))));
                    newRow.Append(new Cell(new InlineString(new Text(item.DataType.ToString())))); // Assuming DataType is an enum
                    newRow.Append(new Cell(new InlineString(new Text(item.DataSize.ToString()))));
                    newRow.Append(new Cell(new InlineString(new Text(item.BuildVersion))));
                    newRow.Append(new Cell(new InlineString(new Text(item.DeployedProjectVersion))                      ));
                    newRow.Append(new Cell(new InlineString(new Text(item.UpdatedTime.ToString())))); // Convert to string as needed
                    newRow.Append(new Cell(new InlineString(new Text(item.DataState.ToString())))); // Assuming DataState is an enum
                    newRow.Append(new Cell(new InlineString(new Text(item.DataSrcPath))));
                    newRow.Append(new Cell(new InlineString(new Text(item.DataRelPath))));
                    newRow.Append(new Cell(new InlineString(new Text(item.DataHash))));
                    sheetData.AppendChild(newRow);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
                exportPath = null;
                return false; 
            }
            exportPath = xlsxFileDirPath; 
            return true; 
        }
        private bool TryExportProjectFilesXLSX(ProjectData projectData, ICollection<ProjectFile> projectFiles, out string? exportPath)
        {
            var sortedProjectFiles = projectFiles
                .OrderBy(item => item.DataName).ToList();

            string xlsxFilePath = GetExportXLSXPath(projectData);
            string? xlsxFileDirPath = Path.GetDirectoryName(xlsxFilePath); 
            if (!Directory.Exists(xlsxFileDirPath)) Directory.CreateDirectory(xlsxFileDirPath); 
            try
            {
                using SpreadsheetDocument spreadsheetDocument = SpreadsheetDocument.Create(xlsxFilePath, SpreadsheetDocumentType.Workbook);
                // Add a WorkbookPart to the document
                WorkbookPart workbookPart = spreadsheetDocument.AddWorkbookPart();
                workbookPart.Workbook = new Workbook();

                // Add a WorksheetPart to the WorkbookPart
                WorksheetPart worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
                worksheetPart.Worksheet = new Worksheet(new SheetData());

                // Add Sheets to the Workbook
                Sheets sheets = spreadsheetDocument.WorkbookPart.Workbook.AppendChild(new Sheets());

                // Append a new worksheet and associate it with the workbook
                Sheet sheet = new Sheet() { Id = spreadsheetDocument.WorkbookPart.GetIdOfPart(worksheetPart), SheetId = 1, Name = "Project Files" };
                sheets.Append(sheet);

                // Get the SheetData
                SheetData sheetData = worksheetPart.Worksheet.GetFirstChild<SheetData>();

                // Add headers
                Row headerRow = new Row();
                headerRow.Append(new Cell(new InlineString(new Text("DataName"))));
                headerRow.Append(new Cell(new InlineString(new Text("DataType"))));
                headerRow.Append(new Cell(new InlineString(new Text("DataSize"))));
                headerRow.Append(new Cell(new InlineString(new Text("BuildVersion"))));
                headerRow.Append(new Cell(new InlineString(new Text("DeployedProjectVersion"))));
                headerRow.Append(new Cell(new InlineString(new Text("UpdatedTime"))));
                headerRow.Append(new Cell(new InlineString(new Text("DataState"))));
                headerRow.Append(new Cell(new InlineString(new Text("DataSrcPath"))));
                headerRow.Append(new Cell(new InlineString(new Text("DataRelPath"))));
                headerRow.Append(new Cell(new InlineString(new Text("DataHash"))));
                sheetData.AppendChild(headerRow);

                // Populate data
                foreach (var item in sortedProjectFiles)
                {
                    Row newRow = new Row();
                    newRow.Append(new Cell(new InlineString(new Text(item.DataName))));
                    newRow.Append(new Cell(new InlineString(new Text(item.DataType.ToString())))); // Assuming DataType is an enum
                    newRow.Append(new Cell(new InlineString(new Text(item.DataSize.ToString()))));
                    newRow.Append(new Cell(new InlineString(new Text(item.BuildVersion ?? ""))));
                    newRow.Append(new Cell(new InlineString(new Text(item.DeployedProjectVersion ?? ""))));
                    newRow.Append(new Cell(new InlineString(new Text(item.UpdatedTime.ToString())))); // Convert to string as needed
                    newRow.Append(new Cell(new InlineString(new Text(item.DataState.ToString())))); // Assuming DataState is an enum
                    newRow.Append(new Cell(new InlineString(new Text(item.DataSrcPath))));
                    newRow.Append(new Cell(new InlineString(new Text(item.DataRelPath))));
                    newRow.Append(new Cell(new InlineString(new Text(item.DataHash ?? ""))));
                    sheetData.AppendChild(newRow);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
                exportPath = null;
                return false;
            }
            exportPath = xlsxFileDirPath;
            return true;
        }
        public void ExportProjectChanges(ProjectData projectData, List<ChangedFile> changes)
        {

        }
        private bool TryExportProjectChanges(ProjectData projectData, List<ChangedFile> changes, out string? exportPath)
        {
            exportPath = null;
            return false; 
        }
        private string GetExportXLSXPath(ProjectData projData)
        {
            return $"{_currentProjectPath}\\Export_XLSX\\{projData.UpdatedVersion}_ProjectFiles.xlsx"; 
        }
        private string GetExportProjectPath(ProjectData projectData)
        {
            return $"{_currentProjectPath}\\Export_{projectData.ProjectName}\\{projectData.UpdatedVersion}";
        }
        #region CallBacks From Parent Model 
        public void MetaDataManager_MetaDataLoadedCallBack(object metaDataObj)
        {
            if (metaDataObj is not ProjectMetaData projectMetaData) return;
            if (projectMetaData == null) return;
            this._backupFilesDict = projectMetaData.BackupFiles;
            this._currentProjectPath = projectMetaData.ProjectPath;
        }
        #endregion
    }
}
