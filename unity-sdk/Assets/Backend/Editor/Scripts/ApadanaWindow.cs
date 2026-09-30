using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

public class ApadanaWindow : EditorWindow
{

    private static Texture2D logo;

    [MenuItem("Apadana/Window")]
    private static void ShowWindowFromApadanaMenu()
    {
        ShowApadanaWindow();
    }

    [MenuItem("Window/Apadana")]
    public static void ShowApadanaWindow()
    {
        EditorWindow.GetWindowWithRect<ApadanaWindow>(new Rect(0, 0, 400, 500), true, "Apadana");
        logo = Resources.Load<Texture2D>("logo");
    }

    void OnGUI()
    {
        GUILayout.BeginVertical();
        GUILayout.BeginHorizontal();
        GUILayout.Space(80);
        GUILayout.Label(logo, GUILayout.Width(300), GUILayout.Height(300));
        GUILayout.EndHorizontal();
        GUILayout.Label(string.Format("Version: \t\t {0}.{1}.{2}" ,Backend.BackendPlatform.majorVersion,Backend.BackendPlatform.minorVersion,Backend.BackendPlatform.hotFixVersion));
        GUILayout.Label("Release Date: \t" + Backend.BackendPlatform.releaseDate.ToShortDateString());
        GUILayout.Label("Everything is more fun together");
        GUILayout.FlexibleSpace();
        GUILayout.BeginHorizontal();
        if(GUILayout.Button("Website"))
        {
            Application.OpenURL("http://apadana-platform.ir");
        }
        if (GUILayout.Button("Developer Panel"))
        {
            Application.OpenURL("http://dev.apadana-platform.ir");
        }
        if (GUILayout.Button("Documentation"))
        {
            Application.OpenURL("http://apadana-platform.ir/docs");
        }
        GUILayout.EndHorizontal();
        GUILayout.FlexibleSpace();
        GUILayout.EndVertical();
    }
}
