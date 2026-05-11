#pragma warning disable CS0618  // V1 types used intentionally

using DeployAssistant.Filtering;
using DeployAssistant.Interfaces;
using DeployAssistant.Model;
using System;
using System.IO;
using System.Linq;
using Xunit;

namespace DeployAssistant.Tests.Filtering
{
    /// <summary>
    /// Behavioral tests for <see cref="ProjectScanner"/>.  The scanner walks a
    /// project root applying an <see cref="IIgnoreFilter"/> under a fixed scope,
    /// producing the set of paths a consumer should consider.  It replaces the
    /// scattered "GetFiles + .Except(excluded)" logic in FileManager.
    /// </summary>
    public class ProjectScannerTests : IDisposable
    {
        private readonly string _root;

        public ProjectScannerTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "DA_Scanner_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        public void Dispose()
        {
            try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
            catch (IOException) { }
        }

        [Fact]
        public void EnumerateFiles_ExcludesPathsMatchingIgnoreFilter()
        {
            File.WriteAllText(Path.Combine(_root, "keep.dll"), "ok");
            File.WriteAllText(Path.Combine(_root, "drop.deploy"), "skip");

            var ignoreData = new ProjectIgnoreData("Proj");
            IIgnoreFilter filter = IgnoreFilter.FromIgnoreData(ignoreData);
            var scanner = new ProjectScanner(filter);

            var files = scanner.EnumerateFiles(_root, IgnoreType.Deploy).ToList();

            Assert.Contains(files, f => f.EndsWith("keep.dll", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(files, f => f.EndsWith("drop.deploy", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void EnumerateDirectories_ExcludesIgnoredDirectories()
        {
            // Set up: a "keep" dir, plus an "en-US" dir which is Integration-scope ignored.
            Directory.CreateDirectory(Path.Combine(_root, "keep"));
            Directory.CreateDirectory(Path.Combine(_root, "en-US"));

            var ignoreData = new ProjectIgnoreData("Proj");
            IIgnoreFilter filter = IgnoreFilter.FromIgnoreData(ignoreData);
            var scanner = new ProjectScanner(filter);

            var dirs = scanner.EnumerateDirectories(_root, IgnoreType.Integration).ToList();

            Assert.Contains(dirs, d => d.EndsWith("keep", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(dirs, d => d.EndsWith("en-US", StringComparison.OrdinalIgnoreCase));
        }
    }
}
