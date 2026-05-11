using DeployAssistant.Interfaces;
using DeployAssistant.Model;
using System.Collections.Generic;
using System.IO;

namespace DeployAssistant.Filtering
{
    /// <summary>
    /// Walks a project root, applying an <see cref="IIgnoreFilter"/> at a fixed
    /// scope.  Yields only the paths the consumer should see.
    /// </summary>
    public sealed class ProjectScanner
    {
        private readonly IIgnoreFilter _filter;

        public ProjectScanner(IIgnoreFilter filter)
        {
            _filter = filter;
        }

        public IEnumerable<string> EnumerateFiles(string root, IgnoreType scope)
        {
            foreach (string fullPath in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
            {
                string rel = MakeRelative(root, fullPath);
                if (_filter.Matches(rel, ProjectDataType.File, scope)) continue;
                yield return fullPath;
            }
        }

        public IEnumerable<string> EnumerateDirectories(string root, IgnoreType scope)
        {
            foreach (string fullPath in Directory.GetDirectories(root, "*", SearchOption.AllDirectories))
            {
                string rel = MakeRelative(root, fullPath);
                if (_filter.Matches(rel, ProjectDataType.Directory, scope)) continue;
                yield return fullPath;
            }
        }

        private static string MakeRelative(string root, string fullPath)
        {
            string r = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (fullPath.StartsWith(r + Path.DirectorySeparatorChar, System.StringComparison.OrdinalIgnoreCase))
                return fullPath.Substring(r.Length + 1);
            if (fullPath.StartsWith(r + Path.AltDirectorySeparatorChar, System.StringComparison.OrdinalIgnoreCase))
                return fullPath.Substring(r.Length + 1);
            return fullPath;
        }
    }
}
