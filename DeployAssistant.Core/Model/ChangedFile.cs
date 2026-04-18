using DeployAssistant.DataComponent;
using System.Text.Json.Serialization;

namespace DeployAssistant.Model
{
    public class ChangedFile
    {
        public ProjectFile? SrcFile { get; set; }
        public ProjectFile? DstFile { get; set;}
        public DataState DataState {  get; set; }
        public ChangedFile() { }
        public ChangedFile(ChangedFile srcChangedfile)
        {
            if (srcChangedfile.SrcFile != null)
            {
                SrcFile = new ProjectFile(srcChangedfile.SrcFile);
                SrcFile.IsDstFile = false; 
            }
            else
                SrcFile = null;
            if (srcChangedfile.DstFile != null)
            {
                DstFile = new ProjectFile(srcChangedfile.DstFile);
                DstFile.IsDstFile = true; 
            }
            else
                DstFile = null;
            DataState = srcChangedfile.DataState;
        }
        public ChangedFile(ProjectFile DstFile, DataState _DataState)
        {
            SrcFile = null;
            this.DstFile = DstFile;
            DstFile.IsDstFile = (_DataState & DataState.Overlapped) == 0 ? true : false; 
            DataState = _DataState;
        }
        public ChangedFile(ProjectFile SrcFile,  ProjectFile DstFile, DataState _DataState, bool RegisterChanges)
        {
            this.SrcFile = SrcFile;
            SrcFile.IsDstFile = false; 
            this.DstFile = DstFile;
            DstFile.IsDstFile = (_DataState & DataState.Overlapped) == 0 ? true : false;
            DataState = _DataState;
        }
        [JsonConstructor]
        public ChangedFile(ProjectFile SrcFile, ProjectFile DstFile, DataState DataState)
        {
            this.SrcFile = SrcFile;
            this.DstFile = DstFile;
            this.DataState = DataState;
        }
    }
}
