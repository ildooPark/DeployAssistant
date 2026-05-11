using DeployAssistant.Interfaces;
using DeployAssistant.Model;
using System.IO;
using System.Text.RegularExpressions;

namespace DeployAssistant.Filtering
{
    public sealed class IgnoreFilter : IIgnoreFilter
    {
        private readonly ProjectIgnoreData _data;

        private IgnoreFilter(ProjectIgnoreData data)
        {
            _data = data;
        }

        public static IIgnoreFilter FromIgnoreData(ProjectIgnoreData data)
            => new IgnoreFilter(data);

        public bool Matches(string relativePath, ProjectDataType dataType, IgnoreType scope)
        {
            string fileName = Path.GetFileName(relativePath);
            string[] segments = relativePath.Split(new[] { '\\', '/' }, System.StringSplitOptions.RemoveEmptyEntries);

            foreach (RecordedFile entry in _data.IgnoreFileList)
            {
                if ((entry.IgnoreType & scope) == 0) continue;

                if (entry.DataType == ProjectDataType.File)
                {
                    // File entry matches the leaf name only.  A file entry never
                    // matches a directory query.
                    if (dataType == ProjectDataType.File && PatternMatches(entry.DataName, fileName))
                        return true;
                }
                else
                {
                    // Directory entry matches when any path segment matches the
                    // entry's name (covers both directory queries and files
                    // living inside an ignored subtree).
                    foreach (string segment in segments)
                    {
                        if (PatternMatches(entry.DataName, segment)) return true;
                    }
                }
            }
            return false;
        }

        private static bool PatternMatches(string pattern, string candidate)
        {
            if (!ContainsWildcard(pattern))
                return string.Equals(pattern, candidate, System.StringComparison.OrdinalIgnoreCase);

            string regex = "^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$";
            return Regex.IsMatch(candidate, regex, RegexOptions.IgnoreCase);
        }

        private static bool ContainsWildcard(string s)
            => s.IndexOf('*') >= 0 || s.IndexOf('?') >= 0;
    }
}
