using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

public class WildCatWindow : EditorWindow
{

    private static Texture2D logo;

    [MenuItem("WildCat/Window")]
    private static void ShowWindowFromWildCatMenu()
    {
        ShowWildCatWindow();
    }

    [MenuItem("Window/WildCat")]
    public static void ShowWildCatWindow()
    {
        EditorWindow.GetWindowWithRect<WildCatWindow>(new Rect(0, 0, 400, 500), true, "WildCat");
        logo = Resources.Load<Texture2D>("logo");
    }

    void OnGUI()
    {
        GUILayout.BeginVertical();
        GUILayout.BeginHorizontal();
        GUILayout.Space(80);
        GUILayout.Label(logo, GUILayout.Width(300), GUILayout.Height(300));
        GUILayout.EndHorizontal();
        GUILayout.Label(string.Format("Version: \t\t {0}.{1}.{2}" ,WildCat.BackendPlatform.majorVersion,WildCat.BackendPlatform.minorVersion,WildCat.BackendPlatform.hotFixVersion));
        GUILayout.Label("Release Date: \t" + WildCat.BackendPlatform.releaseDate.ToShortDateString());
        GUILayout.Label("Everything is more fun together");
        GUILayout.FlexibleSpace();
        GUILayout.BeginHorizontal();
        if(GUILayout.Button("Website"))
        {
            Application.OpenURL("https://github.com/nooparmygames/wildcat");
        }
        if (GUILayout.Button("Documentation"))
        {
            Application.OpenURL("https://github.com/nooparmygames/wildcat/tree/main/docs");
        }
        GUILayout.EndHorizontal();
        GUILayout.FlexibleSpace();
        GUILayout.EndVertical();
    }
}
