using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace DeployAssistant.Tests.ViewModel
{
    /// <summary>
    /// A StaticResource key missing from either language file crashes the GUI at startup, and a
    /// missing DynamicResource key shows an empty label. Every S.* key the GUI references must
    /// exist in both Strings.ko-KR.xaml and Strings.en-US.xaml.
    /// </summary>
    public class LocalizationKeyTests
    {
        private static readonly Regex XamlKeyUse = new Regex(@"(?:Static|Dynamic)Resource\s+(S\.[\w.]+)", RegexOptions.Compiled);
        private static readonly Regex CodeKeyUse = new Regex(@"Loc\.T\(\s*""(S\.[\w.]+)""", RegexOptions.Compiled);
        private static readonly Regex KeyDefinition = new Regex(@"x:Key=""(S\.[\w.]+)""", RegexOptions.Compiled);

        private static string GuiProjectDir()
        {
            var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "DeployAssistant.sln")))
                dir = dir.Parent;
            Assert.True(dir != null, "repository root (DeployAssistant.sln) not found above the test output");
            return Path.Combine(dir!.FullName, "DeployAssistant");
        }

        private static HashSet<string> Defined(string file) =>
            new HashSet<string>(KeyDefinition.Matches(File.ReadAllText(file)).Cast<Match>().Select(m => m.Groups[1].Value));

        private static IEnumerable<string> Used(string guiDir)
        {
            IEnumerable<string> Files(string pattern) => Directory.EnumerateFiles(guiDir, pattern, SearchOption.AllDirectories)
                .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)
                            && !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar)
                            && !Path.GetFileName(f).StartsWith("Strings.", StringComparison.Ordinal));
            var xaml = Files("*.xaml").SelectMany(f => XamlKeyUse.Matches(File.ReadAllText(f)).Cast<Match>());
            var code = Files("*.cs").SelectMany(f => CodeKeyUse.Matches(File.ReadAllText(f)).Cast<Match>());
            return xaml.Concat(code).Select(m => m.Groups[1].Value).Distinct();
        }

        [Theory]
        [InlineData("Strings.ko-KR.xaml")]
        [InlineData("Strings.en-US.xaml")]
        public void EveryReferencedKey_IsDefined(string stringsFile)
        {
            string gui = GuiProjectDir();
            HashSet<string> defined = Defined(Path.Combine(gui, "View", stringsFile));

            var used = Used(gui).ToList();
            Assert.Contains("S.Compact.Enter", used); // the scan really reaches MainWindow.xaml
            var missing = used.Where(k => !defined.Contains(k)).OrderBy(k => k).ToList();

            Assert.True(missing.Count == 0, $"{stringsFile} is missing: {string.Join(", ", missing)}");
        }

        [Fact]
        public void BothLanguages_DefineTheSameKeys()
        {
            string view = Path.Combine(GuiProjectDir(), "View");
            HashSet<string> ko = Defined(Path.Combine(view, "Strings.ko-KR.xaml"));
            HashSet<string> en = Defined(Path.Combine(view, "Strings.en-US.xaml"));

            Assert.Empty(ko.Except(en));
            Assert.Empty(en.Except(ko));
        }
    }
}
