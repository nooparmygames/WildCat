using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace WildCat.Sdk.Tests
{
    /// <summary>
    /// The package as Unity reads it, checked without Unity: package.json, the assembly
    /// definitions, a .meta file with a unique GUID for every asset (a package installed from git is
    /// read only, so Unity cannot create them), and the rules the engine-free assemblies follow.
    /// </summary>
    public class PackageLayoutTests
    {
        private const string MetaHint = "Run tools/unity-package-meta.sh (or open the project in Unity) to create missing .meta files.";

        private static readonly string RepoRoot = FindRepoRoot();
        private static readonly string UnityProject = Path.Combine(RepoRoot, "unity-sdk");
        private static readonly string Package = Path.Combine(UnityProject, "Packages", "com.noopsoftworks.wildcat");

        private static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (Directory.Exists(Path.Combine(dir.FullName, "unity-sdk", "Packages", "com.noopsoftworks.wildcat")))
                    return dir.FullName;
                dir = dir.Parent;
            }
            throw new DirectoryNotFoundException("The repository root was not found above " + AppContext.BaseDirectory);
        }

        private static string Relative(string path)
        {
            return Path.GetRelativePath(Package, path).Replace('\\', '/');
        }

        private static JsonElement ReadJson(string relativePath)
        {
            return JsonDocument.Parse(File.ReadAllText(Path.Combine(Package, relativePath))).RootElement;
        }

        /// <summary>Paths Unity imports: everything but folders ending in ~ (a sample's own files excepted) and hidden files.</summary>
        private static List<string> ImportedAssets()
        {
            return Directory.EnumerateFileSystemEntries(Package, "*", SearchOption.AllDirectories)
                .Select(Relative)
                .Where(p => !p.EndsWith(".meta", StringComparison.Ordinal))
                .Where(p => !p.Split('/').Any(part => part.StartsWith(".", StringComparison.Ordinal)))
                .Where(p => !p.StartsWith("Documentation~", StringComparison.Ordinal) && p != "Samples~")
                .OrderBy(p => p, StringComparer.Ordinal)
                .ToList();
        }

        private static string GuidOf(string metaPath)
        {
            var match = Regex.Match(File.ReadAllText(metaPath), @"^guid: ([0-9a-f]{32})\s*$", RegexOptions.Multiline);
            Assert.True(match.Success, $"{Relative(metaPath)} has no guid line.");
            return match.Groups[1].Value;
        }

        [Fact]
        public void PackageJsonDescribesAUnity6Package()
        {
            JsonElement package = ReadJson("package.json");
            Assert.Equal("com.noopsoftworks.wildcat", package.GetProperty("name").GetString());
            Assert.Equal("WildCat", package.GetProperty("displayName").GetString());
            Assert.Equal("6000.6", package.GetProperty("unity").GetString());
            Assert.Equal(WildCatVersion.Current, package.GetProperty("version").GetString());
            Assert.Matches(@"^\d+\.\d+\.\d+$", package.GetProperty("version").GetString());
            foreach (JsonElement sample in package.GetProperty("samples").EnumerateArray())
                Assert.True(Directory.Exists(Path.Combine(Package, sample.GetProperty("path").GetString())));
            foreach (string file in new[] { "README.md", "CHANGELOG.md", "LICENSE.md", "Documentation~/index.md" })
                Assert.True(File.Exists(Path.Combine(Package, file)), file);
            Assert.Contains("## [" + WildCatVersion.Current + "]", File.ReadAllText(Path.Combine(Package, "CHANGELOG.md")));
        }

        [Fact]
        public void AssemblyDefinitionsKeepTheEngineOutOfProtocolAndCore()
        {
            JsonElement protocol = ReadJson("Runtime/Protocol/WildCat.Protocol.asmdef");
            JsonElement core = ReadJson("Runtime/Core/WildCat.Core.asmdef");
            JsonElement unity = ReadJson("Runtime/Unity/WildCat.Unity.asmdef");
            JsonElement editor = ReadJson("Editor/WildCat.Editor.asmdef");
            JsonElement sample = ReadJson("Samples~/Basic/WildCat.Samples.Basic.asmdef");

            Assert.Equal("WildCat.Protocol", protocol.GetProperty("name").GetString());
            Assert.True(protocol.GetProperty("noEngineReferences").GetBoolean());
            Assert.Empty(protocol.GetProperty("references").EnumerateArray());

            Assert.Equal("WildCat.Core", core.GetProperty("name").GetString());
            Assert.True(core.GetProperty("noEngineReferences").GetBoolean());
            Assert.Equal(new[] { "WildCat.Protocol" }, References(core));

            Assert.Equal("WildCat.Unity", unity.GetProperty("name").GetString());
            Assert.Contains("WildCat.Core", References(unity));

            Assert.Equal("WildCat.Editor", editor.GetProperty("name").GetString());
            Assert.Equal(new[] { "Editor" }, editor.GetProperty("includePlatforms").EnumerateArray().Select(e => e.GetString()));
            Assert.Contains("WildCat.Unity", References(editor));

            Assert.Contains("WildCat.Unity", References(sample));
            Assert.False(sample.GetProperty("autoReferenced").GetBoolean());
        }

        private static string[] References(JsonElement asmdef)
        {
            return asmdef.GetProperty("references").EnumerateArray().Select(e => e.GetString()).ToArray();
        }

        [Fact]
        public void EveryAssetHasAMetaFileWithAUniqueGuid()
        {
            var assets = ImportedAssets();
            var missing = assets.Where(a => !File.Exists(Path.Combine(Package, a) + ".meta")).ToList();
            Assert.True(missing.Count == 0, $"Missing .meta files: {string.Join(", ", missing)}. {MetaHint}");

            var metas = Directory.EnumerateFiles(Package, "*.meta", SearchOption.AllDirectories).Select(Relative).ToList();
            var orphans = metas.Where(m => !File.Exists(Path.Combine(Package, m[..^5])) && !Directory.Exists(Path.Combine(Package, m[..^5]))).ToList();
            Assert.True(orphans.Count == 0, $".meta files without an asset: {string.Join(", ", orphans)}");

            var guids = metas.Select(m => (Meta: m, Guid: GuidOf(Path.Combine(Package, m)))).ToList();
            var duplicates = guids.GroupBy(g => g.Guid).Where(g => g.Count() > 1).Select(g => string.Join(" and ", g.Select(x => x.Meta))).ToList();
            Assert.True(duplicates.Count == 0, $"Assets sharing a GUID: {string.Join("; ", duplicates)}");

            foreach (var (meta, _) in guids.Where(g => g.Meta.EndsWith(".asmdef.meta", StringComparison.Ordinal)))
                Assert.Contains("AssemblyDefinitionImporter:", File.ReadAllText(Path.Combine(Package, meta)));
            Assert.Contains("PluginImporter:", File.ReadAllText(Path.Combine(Package, "Runtime/Unity/WebGL/WildCatWebSocket.jslib.meta")));
        }

        [Fact]
        public void TheSampleSettingsAssetMatchesTheSettingsScript()
        {
            string asset = File.ReadAllText(Path.Combine(Package, "Samples~/Basic/WildCatSettings.asset"));
            string scriptGuid = GuidOf(Path.Combine(Package, "Runtime/Unity/WildCatSettings.cs.meta"));
            Assert.Contains($"m_Script: {{fileID: 11500000, guid: {scriptGuid}, type: 3}}", asset);

            // Every serialized field of the asset is a [SerializeField] field of WildCatSettings, and back.
            string script = File.ReadAllText(Path.Combine(Package, "Runtime/Unity/WildCatSettings.cs"));
            var fields = Regex.Matches(script, @"\[SerializeField\]\s+private\s+[\w.]+\s+(\w+)")
                .Select(m => m.Groups[1].Value).OrderBy(f => f, StringComparer.Ordinal).ToList();
            var keys = asset.Split('\n').SkipWhile(l => !l.StartsWith("  m_EditorClassIdentifier:", StringComparison.Ordinal)).Skip(1)
                .Where(l => l.StartsWith("  ", StringComparison.Ordinal) && l.Contains(':'))
                .Select(l => l.Trim().Split(':')[0]).OrderBy(f => f, StringComparer.Ordinal).ToList();
            Assert.NotEmpty(fields);
            Assert.Equal(fields, keys);
            Assert.Contains("developerKey: 11111111-1111-1111-1111-111111111111", asset);
        }

        [Fact]
        public void TheEngineFreeSourcesDoNotTouchUnityOrNewerCSharp()
        {
            foreach (string folder in new[] { "Runtime/Core", "Runtime/Protocol" })
            {
                foreach (string file in Directory.EnumerateFiles(Path.Combine(Package, folder), "*.cs", SearchOption.AllDirectories))
                {
                    string text = File.ReadAllText(file);
                    Assert.DoesNotMatch(@"using\s+UnityEngine|UnityEngine\.\w+\s*\(", text);
                    Assert.DoesNotContain("#nullable", text);
                    Assert.DoesNotContain("System.Threading.Channels", text);
                    Assert.DoesNotMatch(@"\binit;", text);
                    Assert.DoesNotMatch(@"\brecord\s+(class|struct)?\s*\w+\s*[({]", text);
                }
            }
        }

        [Fact]
        public void TheTextFilesAreAscii()
        {
            var textExtensions = new[] { ".cs", ".md", ".json", ".asmdef", ".jslib", ".asset", ".meta" };
            var offenders = Directory.EnumerateFiles(Package, "*", SearchOption.AllDirectories)
                .Where(f => textExtensions.Contains(Path.GetExtension(f)))
                .Where(f => File.ReadAllBytes(f).Any(b => b > 127))
                .Select(Relative)
                .ToList();
            Assert.True(offenders.Count == 0, "Not plain ASCII: " + string.Join(", ", offenders));
        }

        [Fact]
        public void TheUnityProjectIsAUnity6ProjectWithTheEmbeddedPackage()
        {
            string version = File.ReadAllText(Path.Combine(UnityProject, "ProjectSettings", "ProjectVersion.txt"));
            Assert.Matches(@"m_EditorVersion: 6000\.6\.\d+f\d+", version);
            JsonElement manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(UnityProject, "Packages", "manifest.json"))).RootElement;
            var dependencies = manifest.GetProperty("dependencies").EnumerateObject().Select(p => p.Name).ToList();
            // Embedded packages (folders under Packages/) load without a manifest entry.
            Assert.DoesNotContain("com.noopsoftworks.wildcat", dependencies);
            Assert.DoesNotContain("com.unity.ide.vscode", dependencies);
            Assert.False(Directory.Exists(Path.Combine(UnityProject, "Assets", "Backend")), "The old Assets/Backend SDK is back.");
        }
    }
}
