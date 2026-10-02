using UnityEditor;
using UnityEngine;

namespace WildCat.EditorTools
{
    /// <summary>The About window: logo, SDK version and links.</summary>
    internal sealed class WildCatWindow : EditorWindow
    {
        private const string LogoPath = "Packages/com.noopsoftworks.wildcat/Editor/Images/logo.png";
        private const string RepositoryUrl = "https://github.com/nooparmygames/wildcat";

        private Texture2D logo;

        [MenuItem("WildCat/About", priority = 0)]
        private static void ShowFromWildCatMenu()
        {
            ShowWindow();
        }

        [MenuItem("Window/WildCat")]
        public static void ShowWindow()
        {
            GetWindowWithRect<WildCatWindow>(new Rect(0, 0, 400, 480), true, "WildCat");
        }

        private void OnEnable()
        {
            logo = AssetDatabase.LoadAssetAtPath<Texture2D>(LogoPath);
        }

        private void OnGUI()
        {
            GUILayout.BeginVertical();
            if (logo != null)
            {
                GUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                GUILayout.Label(logo, GUILayout.Width(300), GUILayout.Height(300));
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }
            GUILayout.Label("WildCat SDK " + WildCatVersion.Current, EditorStyles.boldLabel);
            GUILayout.Label("Everything is more fun together.");
            GUILayout.FlexibleSpace();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Website"))
                Application.OpenURL(RepositoryUrl);
            if (GUILayout.Button("Documentation"))
                Application.OpenURL(RepositoryUrl + "/blob/main/unity-sdk/Packages/com.noopsoftworks.wildcat/Documentation~/index.md");
            GUILayout.EndHorizontal();
            GUILayout.Space(8);
            GUILayout.EndVertical();
        }
    }
}
