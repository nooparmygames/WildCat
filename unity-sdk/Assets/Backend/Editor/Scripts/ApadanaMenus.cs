using System.IO;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using System.Runtime.InteropServices;

public class ApadanaMenus
{
    [DllImport("kernel32.dll")]
    static extern bool CreateSymbolicLink(string lpSymlinkFileName, string lpTargetFileName, SymbolicLink dwFlags);

    enum SymbolicLink
    {
        File = 0,
        Directory = 1
    }

    [MenuItem("Apadana/Create Clone")]
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

    [MenuItem("Apadana/Manual")]
    private static void Manual()
    {
        Application.OpenURL("http://apadana-platform.ir/docs/articles/intro.html");
    }

    [MenuItem("Apadana/Documentation Home")]
    private static void Docs()
    {
        Application.OpenURL("http://apadana-platform.ir/docs");
    }

    [MenuItem("Apadana/API Reference")]
    private static void APIReference()
    {
        Application.OpenURL("http://apadana-platform.ir/docs/api/Apadana.ApadanaGamePlatform.html#Apadana_ApadanaGamePlatform");
        //http://apadana-platform.ir/docs/articles/history.html
    }

    [MenuItem("Apadana/Change Log")]
    private static void ChangeLog()
    {
        Application.OpenURL("http://apadana-platform.ir/docs/articles/history.html");
    }

    [MenuItem("Apadana/Technical Details")]
    private static void TechDetails()
    {
        //http://apadana-platform.ir/blog.html
        Application.OpenURL("http://apadana-platform.ir/docs/articles/tech.html");
    }

    [MenuItem("Apadana/Blog")]
    private static void Blog()
    {
        Application.OpenURL("http://apadana-platform.ir/blog.html");
    }
}
