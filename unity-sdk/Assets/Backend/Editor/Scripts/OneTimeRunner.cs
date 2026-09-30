using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

public static class OneTimeRunner
{

    static OneTimeRunner()
    {
        if (EditorPrefs.HasKey("wildcat-run"))
        {
            EditorPrefs.SetInt("wildcat-run", 1);
            WildCatWindow.ShowWildCatWindow();
        }
    }
}
