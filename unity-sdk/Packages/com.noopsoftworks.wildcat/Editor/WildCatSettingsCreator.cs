using System.IO;
using UnityEditor;
using UnityEngine;
using WildCat.Unity;

namespace WildCat.EditorTools
{
    /// <summary>Menu items that set up a project and a scene for WildCat.</summary>
    internal static class WildCatSettingsCreator
    {
        private const string DefaultFolder = "Assets/WildCat";
        private const string DefaultPath = DefaultFolder + "/WildCatSettings.asset";

        /// <summary>Selects the project's WildCatSettings asset, creating Assets/WildCat/WildCatSettings.asset when there is none.</summary>
        [MenuItem("WildCat/Create Or Select Settings", priority = 20)]
        private static void CreateOrSelectSettings()
        {
            WildCatSettings settings = FindSettings() ?? CreateSettings();
            Selection.activeObject = settings;
            EditorGUIUtility.PingObject(settings);
        }

        /// <summary>Adds a "WildCat" object with a WildCat Client component, using the project's settings asset.</summary>
        [MenuItem("WildCat/Add Client To Scene", priority = 21)]
        private static void AddClientToScene()
        {
            WildCatSettings settings = FindSettings() ?? CreateSettings();
            GameObject host = new GameObject("WildCat");
            Undo.RegisterCreatedObjectUndo(host, "Add WildCat Client");
            WildCatBehaviour behaviour = Undo.AddComponent<WildCatBehaviour>(host);
            SerializedObject serialized = new SerializedObject(behaviour);
            serialized.FindProperty("settings").objectReferenceValue = settings;
            serialized.ApplyModifiedProperties();
            Selection.activeGameObject = host;
        }

        /// <summary>The first WildCatSettings asset in the project (samples included), or null.</summary>
        internal static WildCatSettings FindSettings()
        {
            foreach (string guid in AssetDatabase.FindAssets("t:" + typeof(WildCatSettings).Name))
            {
                WildCatSettings settings = AssetDatabase.LoadAssetAtPath<WildCatSettings>(AssetDatabase.GUIDToAssetPath(guid));
                if (settings != null)
                    return settings;
            }
            return null;
        }

        private static WildCatSettings CreateSettings()
        {
            if (!AssetDatabase.IsValidFolder(DefaultFolder))
                AssetDatabase.CreateFolder(Path.GetDirectoryName(DefaultFolder), Path.GetFileName(DefaultFolder));
            WildCatSettings settings = ScriptableObject.CreateInstance<WildCatSettings>();
            AssetDatabase.CreateAsset(settings, AssetDatabase.GenerateUniqueAssetPath(DefaultPath));
            AssetDatabase.SaveAssets();
            Debug.Log("[WildCat] Created " + AssetDatabase.GetAssetPath(settings) + ". Fill in the server URL, developer key and game key.");
            return settings;
        }
    }
}
