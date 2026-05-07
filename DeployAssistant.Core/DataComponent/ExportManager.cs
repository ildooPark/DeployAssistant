using DeployAssistant.Interfaces;
using DeployAssistant.Model;
using DeployAssistant.Utils;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace DeployAssistant.DataComponent
{
    public class ExportManager
    {
        private string? _currentProjectPath; 
        private Dictionary<string, ProjectFile>? _backupFilesDict;
        private FileHandlerTool _fileHandlerTool;

        public ExportManager()
        {
            _fileHandlerTool = new FileHandlerTool();
        }
        #region Manager Events
        /// <summary>
        /// string = Export Project Path
        /// </summary>
        public event Action<string>? ExportCompleteEventHandler;
        public event Action<MetaDataState>? ManagerStateEventHandler;
        #endregion
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
                    Trace.TraceWarning("Export Canceled");
                    return;
                }
            }
            ExportCompleteEventHandler?.Invoke(exportDstPath);
        }
        public void ExportProject(ProjectData projectData)
        {
            if (_backupFilesDict == null)
            {
                ManagerStateEventHandler?.Invoke(MetaDataState.Idle);
                Trace.TraceWarning("Backup files are missing!, Make sure ProjectMetaData is Set");
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
                    Trace.TraceWarning("Export Canceled");
                    ManagerStateEventHandler?.Invoke(MetaDataState.Idle);
                    break;
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
                    Trace.TraceWarning("Backup files are missing!, Make sure ProjectMetaData is Set");
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
                            Trace.TraceError($"Export Failed! for file {file.DataName}!");
                            exportPath = null;  return false;
                        }
                        exportCount++;
                        continue;
                    }
                    if (!_backupFilesDict.TryGetValue(file.DataHash, out ProjectFile? backupFile))
                    {
                        Trace.TraceError($"Export Failed! for file {file.DataName}!");
                        exportPath = null; return false;
                    }
                    else
                    {
                        bool handleResult = _fileHandlerTool.HandleFile(backupFile.DataAbsPath, Path.Combine(exportDstPath, file.DataRelPath), DataState.None);
                        if (!handleResult)
                        {
                            Trace.TraceError($"Export Failed! for file {file.DataName}!");
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
                Trace.TraceError(ex.Message);
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
                Trace.TraceError(ex.Message);
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
                Trace.TraceError(ex.Message);
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

        /// <summary>
        /// Exports a diff-only sync package: a zip containing only the files referenced by
        /// <paramref name="selectedDiff"/> that exist in the current project (i.e. those with
        /// <see cref="DataState.Deleted"/> or <see cref="DataState.Modified"/> state, which
        /// represent files that a recipient must add or update to reach the current version)
        /// together with the project <c>.VersionLog</c>.
        /// <para>
        /// Files that are <see cref="DataState.Added"/> (present only in the external/baseline
        /// metafile, absent from the current project) are intentionally omitted from the zip
        /// because they no longer exist in the current version.
        /// </para>
        /// </summary>
        public void ExportDiffPackage(ProjectData currentProject, List<ChangedFile> selectedDiff)
        {
            if (_backupFilesDict == null)
            {
                ManagerStateEventHandler?.Invoke(MetaDataState.Idle);
                Trace.TraceWarning("ExportDiffPackage: backup files are missing. Make sure ProjectMetaData is set.");
                return;
            }
            ManagerStateEventHandler?.Invoke(MetaDataState.Exporting);
            bool result = TryExportDiffPackage(currentProject, selectedDiff, out string? exportPath);
            ManagerStateEventHandler?.Invoke(MetaDataState.Idle);
            if (result && exportPath != null)
                ExportCompleteEventHandler?.Invoke(exportPath);
        }

        private bool TryExportDiffPackage(ProjectData currentProject, List<ChangedFile> selectedDiff, out string? exportPath)
        {
            try
            {
                if (_backupFilesDict == null) { exportPath = null; return false; }

                string exportDstPath  = GetExportProjectPath(currentProject) + "_Diff";
                string exportZipPath  = exportDstPath + ".zip";
                string exportLogPath  = Path.Combine(exportDstPath, $"{currentProject.UpdatedVersion}.VersionLog");

                // Start with a clean staging directory.
                if (Directory.Exists(exportDstPath)) Directory.Delete(exportDstPath, true);
                Directory.CreateDirectory(exportDstPath);

                int exportCount = 0;
                foreach (ChangedFile diff in selectedDiff)
                {
                    // Directory entries: create the structure for directories that exist in the
                    // current version (i.e. those that are NOT Added-only-in-metafile).
                    if (diff.DstFile?.DataType == ProjectDataType.Directory)
                    {
                        if ((diff.DataState & DataState.Added) != 0)
                        {
                            // Directory exists only in the external metafile, not in current — skip.
                            continue;
                        }
                        _fileHandlerTool.HandleDirectory(null, Path.Combine(exportDstPath, diff.DstFile.DataRelPath), DataState.None);
                        exportCount++;
                        continue;
                    }

                    // Added items (in external metafile only, absent from current): skip —
                    // the recipient will handle deletion via the VersionLog.
                    if ((diff.DataState & DataState.Added) != 0) continue;

                    // Deleted and Modified items: export the current (DstFile) version.
                    ProjectFile? dstFile = diff.DstFile;
                    if (dstFile == null || dstFile.DataType == ProjectDataType.Directory) continue;

                    if (!_backupFilesDict.TryGetValue(dstFile.DataHash, out ProjectFile? backupFile))
                    {
                        Trace.TraceError($"ExportDiffPackage: backup not found for '{dstFile.DataName}' (hash: {dstFile.DataHash})");
                        exportPath = null;
                        return false;
                    }

                    bool ok = _fileHandlerTool.HandleFile(
                        backupFile.DataAbsPath,
                        Path.Combine(exportDstPath, dstFile.DataRelPath),
                        DataState.None);
                    if (!ok)
                    {
                        Trace.TraceError($"ExportDiffPackage: file copy failed for '{dstFile.DataName}'");
                        exportPath = null;
                        return false;
                    }
                    exportCount++;
                }

                // Include the VersionLog so the recipient can identify the target version.
                // A diff package that only contains deletions may legitimately have no copied files,
                // so success must depend on creating the VersionLog rather than exportCount.
                bool versionLogSerialized = _fileHandlerTool.TrySerializeProjectData(currentProject, exportLogPath);
                if (!versionLogSerialized || !File.Exists(exportLogPath))
                {
                    exportPath = null;
                    return false;
                }

                if (File.Exists(exportZipPath)) File.Delete(exportZipPath);
                ZipFile.CreateFromDirectory(exportDstPath, exportZipPath);

                exportPath = Directory.GetParent(exportDstPath)?.ToString();
                return true;
            }
            catch (Exception ex)
            {
                Trace.TraceError($"ExportDiffPackage exception: {ex.Message}");
                exportPath = null;
                return false;
            }
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
