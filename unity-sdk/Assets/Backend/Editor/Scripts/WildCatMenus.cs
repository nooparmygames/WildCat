using System.IO;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using System.Runtime.InteropServices;

public class WildCatMenus
{
    [DllImport("kernel32.dll")]
    static extern bool CreateSymbolicLink(string lpSymlinkFileName, string lpTargetFileName, SymbolicLink dwFlags);

    enum SymbolicLink
    {
        File = 0,
        Directory = 1
    }

    [MenuItem("WildCat/Create Clone")]
    private static void MakeLink()
    {
        string projectFolder = Application.dataPath.Substring(0, Application.dataPath.LastIndexOf("/"));
        string clone = projectFolder + "-clone";
        string assetsFolder = projectFolder + "/Assets";
        string assetsClone = clone + "/Assets";
        string projectSettingsFolder = projectFolder + "/ProjectSettings";
        string projectSettingsClone = clone+ "/ProjectSettings";
        if (!Directory.Exists(clone))
        {
            Directory.CreateDirectory(clone);
        }
        CreateSymbolicLink(assetsClone, assetsFolder, SymbolicLink.Directory);
        CreateSymbolicLink(projectSettingsClone, projectSettingsFolder, SymbolicLink.Directory);
    }

    [MenuItem("WildCat/Documentation")]
    private static void Docs()
    {
        Application.OpenURL("https://github.com/nooparmygames/wildcat/tree/main/docs");
    }

    [MenuItem("WildCat/Change Log")]
    private static void ChangeLog()
    {
        Application.OpenURL("https://github.com/nooparmygames/wildcat/releases");
    }
}
