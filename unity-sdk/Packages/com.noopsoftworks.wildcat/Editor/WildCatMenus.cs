using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace WildCat.EditorTools
{
    /// <summary>The WildCat menu: documentation links and the project clone helper.</summary>
    internal static class WildCatMenus
    {
        private const string RepositoryUrl = "https://github.com/nooparmygames/wildcat";

        [MenuItem("WildCat/Documentation", priority = 100)]
        private static void OpenDocumentation()
        {
            Application.OpenURL(RepositoryUrl + "/blob/main/unity-sdk/Packages/com.noopsoftworks.wildcat/Documentation~/index.md");
        }

        [MenuItem("WildCat/Change Log", priority = 101)]
        private static void OpenChangeLog()
        {
            Application.OpenURL(RepositoryUrl + "/blob/main/unity-sdk/Packages/com.noopsoftworks.wildcat/CHANGELOG.md");
        }

        /// <summary>
        /// Makes a sibling folder "&lt;project&gt;-clone" whose Assets, Packages and ProjectSettings
        /// link to this project's, so a second editor can run the same game as a second player.
        /// Uses directory junctions on Windows and symbolic links elsewhere.
        /// </summary>
        [MenuItem("WildCat/Create Clone", priority = 200)]
        private static void CreateClone()
        {
            string project = Path.GetDirectoryName(Application.dataPath);
            string clone = project + "-clone";
            Directory.CreateDirectory(clone);
            foreach (string folder in new[] { "Assets", "Packages", "ProjectSettings" })
            {
                string target = Path.Combine(project, folder);
                string link = Path.Combine(clone, folder);
                if (Directory.Exists(link))
                    continue;
                if (!Link(link, target))
                {
                    Debug.LogError("[WildCat] Could not link " + link + " to " + target + ".");
                    return;
                }
            }
            Debug.Log("[WildCat] Created " + clone + ". Open it in Unity Hub (Add project from disk) to run a second player.");
            EditorUtility.RevealInFinder(clone);
        }

        private static bool Link(string link, string target)
        {
            ProcessStartInfo start = Application.platform == RuntimePlatform.WindowsEditor
                ? new ProcessStartInfo("cmd.exe", "/c mklink /J \"" + link + "\" \"" + target + "\"")
                : new ProcessStartInfo("ln", "-s \"" + target + "\" \"" + link + "\"");
            start.UseShellExecute = false;
            start.CreateNoWindow = true;
            using (Process process = Process.Start(start))
            {
                process.WaitForExit();
                return process.ExitCode == 0;
            }
        }
    }
}
