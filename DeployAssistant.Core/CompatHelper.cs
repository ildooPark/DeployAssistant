using System;
using System.Collections.Generic;
using System.IO;

namespace DeployAssistant.Utils
{
    /// <summary>
    /// Compatibility helpers that back-port APIs not available in .NET Standard 2.0.
    /// </summary>
    public static class PathCompat
    {
        private static readonly bool IsNetFrameworkRuntime =
            System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription
                .StartsWith(".NET Framework", StringComparison.Ordinal);

        /// <summary>
        /// .NET Framework blocks paths over 260 chars unless the process opts in to the
        /// post-4.6.2 path handling. Call once, before any file I/O. No-op on modern .NET.
        /// </summary>
        public static void EnableNetFrameworkLongPaths()
        {
            if (!IsNetFrameworkRuntime) return;
            AppContext.SetSwitch("Switch.System.IO.UseLegacyPathHandling", false);
            AppContext.SetSwitch("Switch.System.IO.BlockLongPaths", false);
        }

        /// <summary>
        /// On .NET Framework, reroutes an absolute path through the <c>\\?\</c> form so
        /// files beyond 260 chars stay reachable. Returns the path unchanged on modern .NET,
        /// for relative paths, and for already-prefixed paths.
        /// Requires <see cref="EnableNetFrameworkLongPaths"/> to have run first.
        /// </summary>
        public static string ToNetFrameworkLongPath(string path)
        {
            if (!IsNetFrameworkRuntime || string.IsNullOrEmpty(path)) return path;
            if (path.StartsWith(@"\\?\", StringComparison.Ordinal)) return path;
            // \\?\ skips normalization, so hand it only clean absolute paths.
            if (path.IndexOf('/') >= 0 || path.Contains(@"\..") || path.Contains(@"\.\"))
                path = Path.GetFullPath(path);
            if (path.StartsWith(@"\\", StringComparison.Ordinal))
                return @"\\?\UNC\" + path.Substring(2);
            if (path.Length >= 3 && path[1] == ':' && path[2] == '\\')
                return @"\\?\" + path;
            return path;
        }

        /// <summary>
        /// Undoes <see cref="ToNetFrameworkLongPath"/> so enumeration results and stored
        /// paths keep their ordinary form.
        /// </summary>
        public static string StripNetFrameworkLongPathPrefix(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;
            if (path.StartsWith(@"\\?\UNC\", StringComparison.Ordinal)) return @"\\" + path.Substring(8);
            if (path.StartsWith(@"\\?\", StringComparison.Ordinal)) return path.Substring(4);
            return path;
        }

        /// <summary>
        /// Returns a relative path from <paramref name="relativeTo"/> to <paramref name="path"/>.
        /// Equivalent to <c>Path.GetRelativePath</c> which is only available from .NET Standard 2.1+.
        /// </summary>
        public static string GetRelativePath(string relativeTo, string path)
        {
            if (relativeTo == null) throw new ArgumentNullException(nameof(relativeTo));
            if (path == null) throw new ArgumentNullException(nameof(path));

            relativeTo = Path.GetFullPath(relativeTo);
            path = Path.GetFullPath(path);

            if (string.Equals(relativeTo, path, StringComparison.OrdinalIgnoreCase))
                return ".";

            // Ensure the base ends with a separator so the Uri treats it as a directory
            if (!relativeTo.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                && !relativeTo.EndsWith(Path.AltDirectorySeparatorChar.ToString(), StringComparison.Ordinal))
            {
                relativeTo += Path.DirectorySeparatorChar;
            }

            Uri fromUri = new Uri(relativeTo, UriKind.Absolute);
            Uri toUri = new Uri(path, UriKind.Absolute);

            Uri relativeUri = fromUri.MakeRelativeUri(toUri);
            string result = Uri.UnescapeDataString(relativeUri.ToString())
                               .Replace('/', Path.DirectorySeparatorChar);

            return string.IsNullOrEmpty(result) ? "." : result;
        }
    }

    /// <summary>
    /// Extension methods for <see cref="Dictionary{TKey,TValue}"/> that back-port
    /// <c>TryAdd</c>, which is not part of .NET Standard 2.0.
    /// </summary>
    internal static class DictionaryCompat
    {
        /// <summary>
        /// Tries to add the specified key and value to the dictionary.
        /// Returns <c>false</c> (without throwing) when the key already exists.
        /// </summary>
        public static bool TryAdd<TKey, TValue>(
            this Dictionary<TKey, TValue> dict, TKey key, TValue value)
        {
            if (dict.ContainsKey(key)) return false;
            dict.Add(key, value);
            return true;
        }

        /// <summary>
        /// Rebuilds a string-keyed dictionary with <see cref="StringComparer.OrdinalIgnoreCase"/>,
        /// last-entry-wins on case-variant duplicates. Windows paths are case-insensitive and the
        /// shipped 3.6.1 kept every ProjectFiles/BackupFiles dictionary case-insensitive; an
        /// ordinal store can emit case-duplicate keys that 3.6.1 then fails to load at all.
        /// </summary>
        public static Dictionary<string, TValue> WithOrdinalIgnoreCaseKeys<TValue>(this Dictionary<string, TValue>? source)
        {
            var result = new Dictionary<string, TValue>(source?.Count ?? 0, StringComparer.OrdinalIgnoreCase);
            if (source != null)
                foreach (KeyValuePair<string, TValue> pair in source)
                    result[pair.Key] = pair.Value;
            return result;
        }
    }
}
