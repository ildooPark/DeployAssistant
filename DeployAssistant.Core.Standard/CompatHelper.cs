using System;
using System.Collections.Generic;
using System.IO;

namespace DeployAssistant.Utils
{
    /// <summary>
    /// Compatibility helpers that back-port APIs not available in .NET Standard 2.0.
    /// </summary>
    internal static class PathCompat
    {
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
    }
}
