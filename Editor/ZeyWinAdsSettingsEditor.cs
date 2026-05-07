using System.IO;
using UnityEditor;
using UnityEngine;

namespace ZeyWinAds.Editor
{
    /// <summary>
    /// Menu item that creates the ZeyWinAdsSettings asset under
    /// Assets/Resources/ if missing, then selects it in the Inspector.
    /// The Inspector already renders the ScriptableObject fields automatically,
    /// so we just need a reliable way to create and find the asset.
    /// </summary>
    public static class ZeyWinAdsSettingsEditor
    {
        private const string AssetDir = "Assets/Resources";
        private const string AssetPath = "Assets/Resources/ZeyWinAdsSettings.asset";

        [MenuItem("ZeyWinAds/Settings", priority = 0)]
        public static void Open()
        {
            var settings = LoadOrCreate();
            Selection.activeObject = settings;
            EditorGUIUtility.PingObject(settings);
        }

        public static ZeyWinAdsSettings LoadOrCreate()
        {
            var existing = AssetDatabase.LoadAssetAtPath<ZeyWinAdsSettings>(AssetPath);
            if (existing != null)
                return existing;

            if (!Directory.Exists(AssetDir))
            {
                Directory.CreateDirectory(AssetDir);
                AssetDatabase.Refresh();
            }

            var asset = ScriptableObject.CreateInstance<ZeyWinAdsSettings>();
            AssetDatabase.CreateAsset(asset, AssetPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[ZeyWinAds] Created settings asset at {AssetPath}");
            return asset;
        }
    }
}
