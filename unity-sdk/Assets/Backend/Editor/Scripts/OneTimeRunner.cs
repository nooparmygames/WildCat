using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

public static class OneTimeRunner
{

    static OneTimeRunner()
    {
        if (EditorPrefs.HasKey("apadana-run"))
        {
            EditorPrefs.SetInt("apadana-run", 1);
            ApadanaWindow.ShowApadanaWindow();
        }
    }
}
