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
            // Enumerating from a plain root on .NET Framework silently drops entries whose
            // absolute path exceeds 260 chars, so the walk itself must use the long-path form.
            foreach (string prefixed in Directory.GetFiles(Utils.PathCompat.ToNetFrameworkLongPath(root), "*", SearchOption.AllDirectories))
            {
                string fullPath = Utils.PathCompat.StripNetFrameworkLongPathPrefix(prefixed);
                string rel = MakeRelative(root, fullPath);
                if (_filter.Matches(rel, ProjectDataType.File, scope)) continue;
                yield return fullPath;
            }
        }

        public IEnumerable<string> EnumerateDirectories(string root, IgnoreType scope)
        {
            foreach (string prefixed in Directory.GetDirectories(Utils.PathCompat.ToNetFrameworkLongPath(root), "*", SearchOption.AllDirectories))
            {
                string fullPath = Utils.PathCompat.StripNetFrameworkLongPathPrefix(prefixed);
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
