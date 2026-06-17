using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace ZeyWinAds.Editor
{
    /// <summary>
    /// Исправляет View.cs чтобы отключать звук игры когда открывается внешний браузер через Application.OpenURL.
    /// Звук должен быть ТОЛЬКО в WebView, а в игре выключен полностью когда офер активен.
    /// </summary>
    [InitializeOnLoad]
    public static class LegacyViewAudioPatcher
    {
        static LegacyViewAudioPatcher()
        {
            EditorApplication.delayCall += () => Apply(logWhenNoChanges: false);
        }

        [MenuItem("ZeyWinAds/Patch Legacy View Audio Muting", priority = 13)]
        public static void ApplyFromMenu()
        {
            Apply(logWhenNoChanges: true);
        }

        internal static bool Apply(bool logWhenNoChanges = true)
        {
            string assetsRoot = Application.dataPath;
            if (!Directory.Exists(assetsRoot))
                return false;

            bool modifiedAny = false;
            foreach (string path in Directory.GetFiles(assetsRoot, "View.cs", SearchOption.AllDirectories))
            {
                modifiedAny |= PatchView(path);
            }

            if (modifiedAny)
            {
                AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
            }
            else if (logWhenNoChanges)
            {
                Debug.Log("[ZeyWinAds] No legacy View.cs files needed audio muting patch.");
            }

            return modifiedAny;
        }

        private static bool PatchView(string fullPath)
        {
            string text = File.ReadAllText(fullPath);
            
            // Проверяем что это нужный View.cs
            if (!text.Contains("class View : MonoBehaviour") || !text.Contains("Application.OpenURL"))
                return false;

            // Если уже пропатчено - пропускаем
            if (text.Contains("MuteAllGameAudio") || text.Contains("UnmuteAllGameAudio"))
                return false;

            string patched = BuildPatchedView();
            
            File.WriteAllText(fullPath, patched, new UTF8Encoding(false));
            Debug.Log($"[ZeyWinAds] Patched View.cs audio muting in {ToAssetPath(fullPath)}.");
            return true;
        }

        private static string BuildPatchedView()
        {
            return @"using UnityEngine;

public class View : MonoBehaviour
{
    private static bool _audioWasPaused;

    public void Init(string url)
    {
        // Отключаем ВСЕ звуки Unity когда открываем внешний браузер
        MuteAllGameAudio();
        
        if (!string.IsNullOrEmpty(url))
            Application.OpenURL(url);

        Destroy(gameObject);
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        if (!hasFocus)
        {
            // Когда уходим в браузер - убеждаемся что звук выключен
            MuteAllGameAudio();
        }
        else
        {
            // Когда возвращаемся - включаем звук обратно
            UnmuteAllGameAudio();
        }
    }

    private void OnApplicationPause(bool pause)
    {
        if (pause)
        {
            // Пауза приложения - выключаем звук
            MuteAllGameAudio();
        }
        else
        {
            // Возвращаемся - включаем звук
            UnmuteAllGameAudio();
        }
    }

    private static void MuteAllGameAudio()
    {
        // Сохраняем текущее состояние AudioListener.pause
        _audioWasPaused = AudioListener.pause;
        
        // ОТКЛЮЧАЕМ ВСЕ ЗВУКИ
        AudioListener.pause = true;
        AudioListener.volume = 0f;
    }

    private static void UnmuteAllGameAudio()
    {
        // Восстанавливаем звук только если он не был на паузе до этого
        if (!_audioWasPaused)
        {
            AudioListener.pause = false;
            AudioListener.volume = 1f;
        }
    }
}
";
        }

        private static string ToAssetPath(string fullPath)
        {
            string normalized = fullPath.Replace('\\', '/');
            string assets = Application.dataPath.Replace('\\', '/');
            if (normalized.StartsWith(assets, StringComparison.Ordinal))
                return "Assets" + normalized.Substring(assets.Length);
            return normalized;
        }
    }
}
