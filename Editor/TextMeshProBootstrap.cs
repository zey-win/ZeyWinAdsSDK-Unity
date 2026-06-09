using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;

namespace ZeyWinAds.Editor
{
    [InitializeOnLoad]
    public static class TextMeshProBootstrap
    {
        private const string MarkerKey = "ZeyWinAds_TextMeshProBootstrap_Done_v3";
        private const string TextMeshProPackage = "com.unity.textmeshpro";
        private const string TextMeshProVersion = "3.0.9";
        private const string UguiPackage = "com.unity.ugui";
        private const string UguiVersion = "1.0.0";
        private const string TmpSettingsPath = "Assets/TextMesh Pro/Resources/TMP Settings.asset";
        private const string TmpDefaultFontPath = "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset";
        private const string TmpDefaultFallbackFontPath = "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF - Fallback.asset";
        private const string TmpDefaultFontSourcePath = "Assets/TextMesh Pro/Fonts/LiberationSans.ttf";
        private const string TmpDefaultSpritePath = "Assets/TextMesh Pro/Resources/Sprite Assets/Default Sprite Asset.asset";
        private const string TmpDefaultStyleSheetPath = "Assets/TextMesh Pro/Resources/Style Sheets/Default Style Sheet.asset";
        private const string TmpExamplesPath = "Assets/TextMesh Pro/Examples & Extras";
        private const string TmpExamplesScriptsPath = "Assets/TextMesh Pro/Examples & Extras/Scripts";
        private const string TmpExamplesAsmdefPath = "Assets/TextMesh Pro/Examples & Extras/Scripts/ZeyWinTmpExamplesDisabled.asmdef";
        private const string TmpExamplesCompileDefine = "ZEYWIN_TMP_EXAMPLES_ENABLED";
        private const int MinimumExamplesFileCount = 50;
        private static readonly string[] PreferredTmpFontAssetPaths =
        {
            "Assets/Fonts/Roboto-VariableFont_wdth,wght SDF.asset",
            "Assets/Fonts/Roboto-Regular SDF.asset",
            "Assets/Fonts/NotoSans-Regular SDF.asset",
            "Assets/TextMesh Pro/Examples & Extras/Resources/Fonts & Materials/Roboto-Bold SDF.asset"
        };
        private static readonly string[] RequiredEssentialAssetPaths =
        {
            TmpSettingsPath,
            TmpDefaultFontPath,
            TmpDefaultFontSourcePath,
            TmpDefaultSpritePath,
            TmpDefaultStyleSheetPath
        };
        private static readonly string[] RequiredTmpShaderNames =
        {
            "TextMeshPro/Mobile/Distance Field",
            "TextMeshPro/Mobile/Distance Field Overlay",
            "TextMeshPro/Mobile/Distance Field - Masking",
            "TextMeshPro/Distance Field",
            "TextMeshPro/Sprite"
        };

        static TextMeshProBootstrap()
        {
            EditorApplication.delayCall += RunBootstrap;
        }

        [MenuItem("ZeyWinAds/Install TextMesh Pro Assets", priority = 2)]
        public static void InstallFromMenu()
        {
            EnsureInstalledAndConfigured(force: true);
        }

        internal static bool EnsureInstalledAndConfigured(bool force = false)
        {
            bool manifestChanged = false;
            manifestChanged |= EnsureManifestDependency(UguiPackage, UguiVersion);
            manifestChanged |= EnsureManifestDependency(TextMeshProPackage, TextMeshProVersion);

            if (manifestChanged)
            {
                AssetDatabase.Refresh();
                Debug.Log("[ZeyWinAds] Added Unity UI/TextMeshPro package dependencies. Unity will resolve them on reload.");
                return false;
            }

            bool essentialsMissing = !HasEssentialResources() || !HasUsableDefaultFontAsset();
            bool examplesMissing = !HasExamplesAndExtras();

            if (force || essentialsMissing || examplesMissing)
            {
                bool importEssentials = force || essentialsMissing;
                bool importExamples = force || examplesMissing;
                ImportTextMeshProResources(importEssentials, importExamples);
                DisableExamplesScriptsByDefault();
                AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);

                if (force || !HasEssentialResources() || !HasUsableDefaultFontAsset())
                    ExtractUnityPackage(FindTmpUnityPackage("TMP Essential Resources.unitypackage"), "TMP Essential Resources");

                if (force || !HasExamplesAndExtras())
                {
                    ExtractUnityPackage(FindTmpUnityPackage("TMP Examples & Extras.unitypackage"), "TMP Examples & Extras");
                    DisableExamplesScriptsByDefault();
                }

                AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
            }

            DisableExamplesScriptsByDefault();
            ConfigureTextMeshProSettings();
            RepairTextMeshProFontAssets();
            ConfigureTextMeshProShaders();
            AssetDatabase.SaveAssets();

            bool ready = HasEssentialResources() && HasExamplesAndExtras() && HasExamplesCompilationGuard();
            Debug.Log($"[ZeyWinAds] TextMesh Pro assets ready: essentials={HasEssentialResources()}, examples={HasExamplesAndExtras()}, examplesGuard={HasExamplesCompilationGuard()}");
            return ready;
        }

        private static void RunBootstrap()
        {
            string marker = Application.dataPath + "::" + MarkerKey;
            if (EditorPrefs.GetBool(marker, false) && HasEssentialResources() && HasUsableDefaultFontAsset() && HasExamplesAndExtras() && HasExamplesCompilationGuard())
                return;

            try
            {
                if (EnsureInstalledAndConfigured())
                    EditorPrefs.SetBool(marker, true);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ZeyWinAds] TextMesh Pro bootstrap failed: {e.Message}");
            }
        }

        private static bool HasEssentialResources()
        {
            foreach (string assetPath in RequiredEssentialAssetPaths)
            {
                if (!File.Exists(assetPath))
                    return false;
            }

            return true;
        }

        private static bool HasUsableDefaultFontAsset()
        {
            var fontAsset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(TmpDefaultFontPath);
            if (fontAsset == null)
                return false;

            var serialized = new SerializedObject(fontAsset);
            var material = serialized.FindProperty("material") ?? serialized.FindProperty("m_Material");
            if (material != null && material.objectReferenceValue == null)
                return false;

            var atlasTextures = serialized.FindProperty("m_AtlasTextures");
            if (atlasTextures != null && atlasTextures.isArray && atlasTextures.arraySize == 0)
                return false;

            var sourceFont = serialized.FindProperty("m_SourceFontFile");
            if (sourceFont != null && sourceFont.objectReferenceValue == null && File.Exists(TmpDefaultFontSourcePath))
                return false;

            return true;
        }

        private static bool HasExamplesAndExtras()
        {
            if (!Directory.Exists(TmpExamplesPath))
                return false;

            try
            {
                return Directory.GetFiles(TmpExamplesPath, "*", SearchOption.AllDirectories).Length >= MinimumExamplesFileCount;
            }
            catch
            {
                return false;
            }
        }

        private static bool HasExamplesCompilationGuard()
        {
            return !Directory.Exists(TmpExamplesScriptsPath) || File.Exists(TmpExamplesAsmdefPath);
        }

        private static bool EnsureManifestDependency(string packageName, string version)
        {
            string manifestPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Packages", "manifest.json"));
            if (!File.Exists(manifestPath))
                return false;

            string manifest = File.ReadAllText(manifestPath);
            if (manifest.Contains($"\"{packageName}\""))
                return false;

            int depsIdx = manifest.IndexOf("\"dependencies\"", StringComparison.Ordinal);
            if (depsIdx < 0)
                return false;

            int braceIdx = manifest.IndexOf('{', depsIdx);
            if (braceIdx < 0)
                return false;

            manifest = manifest.Insert(braceIdx + 1, $"\n    \"{packageName}\": \"{version}\",");
            File.WriteAllText(manifestPath, manifest, new UTF8Encoding(false));
            return true;
        }

        private static void ImportTextMeshProResources(bool importEssentials, bool importExamples)
        {
            string essentials = FindTmpUnityPackage("TMP Essential Resources.unitypackage");
            string examples = FindTmpUnityPackage("TMP Examples & Extras.unitypackage");

            if (importEssentials && !string.IsNullOrEmpty(essentials))
                AssetDatabase.ImportPackage(essentials, false);

            if (importExamples && !string.IsNullOrEmpty(examples))
            {
                AssetDatabase.ImportPackage(examples, false);
                DisableExamplesScriptsByDefault();
            }
        }

        private static void DisableExamplesScriptsByDefault()
        {
            if (!Directory.Exists(TmpExamplesScriptsPath))
                return;

            string asmdef =
                "{\n" +
                "  \"name\": \"ZeyWin.TextMeshPro.Examples.Disabled\",\n" +
                "  \"rootNamespace\": \"\",\n" +
                "  \"references\": [],\n" +
                "  \"includePlatforms\": [],\n" +
                "  \"excludePlatforms\": [],\n" +
                "  \"allowUnsafeCode\": false,\n" +
                "  \"overrideReferences\": false,\n" +
                "  \"precompiledReferences\": [],\n" +
                "  \"autoReferenced\": true,\n" +
                $"  \"defineConstraints\": [\"{TmpExamplesCompileDefine}\"],\n" +
                "  \"versionDefines\": [],\n" +
                "  \"noEngineReferences\": false\n" +
                "}\n";

            if (File.Exists(TmpExamplesAsmdefPath) && File.ReadAllText(TmpExamplesAsmdefPath) == asmdef)
                return;

            Directory.CreateDirectory(TmpExamplesScriptsPath);
            File.WriteAllText(TmpExamplesAsmdefPath, asmdef, new UTF8Encoding(false));
            Debug.Log($"[ZeyWinAds] TextMesh Pro Examples scripts compile only when {TmpExamplesCompileDefine} is defined.");
        }

        private static string FindTmpUnityPackage(string fileName)
        {
            string contents = EditorApplication.applicationContentsPath;
            string[] candidates =
            {
                Path.Combine(contents, "Resources", "PackageManager", "BuiltInPackages", "com.unity.ugui", "Package Resources", fileName),
                Path.Combine(contents, "Resources", "PackageManager", "BuiltInPackages", "com.unity.textmeshpro", "Package Resources", fileName),
                Path.GetFullPath(Path.Combine("Packages", "com.unity.ugui", "Package Resources", fileName)),
                Path.GetFullPath(Path.Combine("Packages", "com.unity.textmeshpro", "Package Resources", fileName))
            };

            foreach (string candidate in candidates)
            {
                if (File.Exists(candidate))
                    return candidate;
            }

            string packageCacheRoot = Path.GetFullPath("Library/PackageCache");
            if (Directory.Exists(packageCacheRoot))
            {
                string packageResource = FindPackageCacheResource(packageCacheRoot, "com.unity.ugui@*", fileName);
                if (!string.IsNullOrEmpty(packageResource))
                    return packageResource;

                packageResource = FindPackageCacheResource(packageCacheRoot, "com.unity.textmeshpro@*", fileName);
                if (!string.IsNullOrEmpty(packageResource))
                    return packageResource;
            }

            Debug.LogWarning($"[ZeyWinAds] Could not find {fileName} in Unity package resources.");
            return null;
        }

        private static string FindPackageCacheResource(string packageCacheRoot, string directoryPattern, string fileName)
        {
            foreach (string directory in Directory.GetDirectories(packageCacheRoot, directoryPattern, SearchOption.TopDirectoryOnly))
            {
                string candidate = Path.Combine(directory, "Package Resources", fileName);
                if (File.Exists(candidate))
                    return candidate;
            }

            return null;
        }

        private static void ConfigureTextMeshProSettings()
        {
            var settings = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(TmpSettingsPath);
            if (settings == null)
                return;

            var serialized = new SerializedObject(settings);
            bool changed = false;

            changed |= SetStringIfDifferent(serialized, "m_defaultFontAssetPath", "Fonts & Materials/");
            changed |= SetStringIfDifferent(serialized, "m_defaultSpriteAssetPath", "Sprite Assets/");
            changed |= SetStringIfDifferent(serialized, "m_defaultStyleSheetPath", "Style Sheets/");
            changed |= SetObjectReference(serialized, "m_defaultFontAsset", GetPreferredTmpFontAssetPath(), overwriteExisting: true);
            changed |= SetObjectReference(serialized, "m_defaultSpriteAsset", TmpDefaultSpritePath, overwriteExisting: true);
            changed |= SetObjectReference(serialized, "m_defaultStyleSheet", TmpDefaultStyleSheetPath, overwriteExisting: false);

            var fallbacks = serialized.FindProperty("m_fallbackFontAssets");
            changed |= AddFallbackFont(fallbacks, "Assets/Fonts/Roboto-VariableFont_wdth,wght SDF.asset");
            changed |= AddFallbackFont(fallbacks, "Assets/Fonts/Roboto-Regular SDF.asset");
            changed |= AddFallbackFont(fallbacks, "Assets/Fonts/NotoSans-Regular SDF.asset");
            changed |= AddFallbackFont(fallbacks, "Assets/TextMesh Pro/Examples & Extras/Resources/Fonts & Materials/Roboto-Bold SDF.asset");

            if (changed)
            {
                serialized.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(settings);
            }
        }

        private static string GetPreferredTmpFontAssetPath()
        {
            foreach (string fontPath in PreferredTmpFontAssetPaths)
            {
                if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(fontPath) != null)
                    return fontPath;
            }

            return TmpDefaultFontPath;
        }

        private static void RepairTextMeshProFontAssets()
        {
            int repaired = 0;
            if (RepairTextMeshProFontAsset(TmpDefaultFontPath))
                repaired++;
            if (RepairTextMeshProFontAsset(TmpDefaultFallbackFontPath))
                repaired++;

            if (repaired > 0)
                Debug.Log($"[ZeyWinAds] TextMesh Pro standard font assets repaired: {repaired}");
        }

        private static bool RepairTextMeshProFontAsset(string fontAssetPath)
        {
            var fontAsset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(fontAssetPath);
            if (fontAsset == null)
                return false;

            bool changed = false;
            var serialized = new SerializedObject(fontAsset);

            var sourceFont = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(TmpDefaultFontSourcePath);
            if (sourceFont != null)
            {
                changed |= SetObjectReference(serialized, "m_SourceFontFile", TmpDefaultFontSourcePath, overwriteExisting: true);
                changed |= SetObjectReference(serialized, "m_SourceFontFile_EditorRef", TmpDefaultFontSourcePath, overwriteExisting: true);

                string sourceGuid = AssetDatabase.AssetPathToGUID(TmpDefaultFontSourcePath);
                if (!string.IsNullOrEmpty(sourceGuid))
                    changed |= SetStringIfDifferent(serialized, "m_SourceFontFileGUID", sourceGuid);
            }

            var materialProperty = serialized.FindProperty("material") ?? serialized.FindProperty("m_Material");
            if (materialProperty != null && materialProperty.objectReferenceValue is Material material)
            {
                Shader mobileDistanceField = Shader.Find("TextMeshPro/Mobile/Distance Field");
                if (mobileDistanceField != null && material.shader != mobileDistanceField)
                {
                    material.shader = mobileDistanceField;
                    EditorUtility.SetDirty(material);
                    changed = true;
                }
            }

            if (!changed)
                return false;

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(fontAsset);
            return true;
        }

        private static void ConfigureTextMeshProShaders()
        {
            Shader mobileDistanceField = Shader.Find("TextMeshPro/Mobile/Distance Field");
            int materialsUpdated = 0;
            if (mobileDistanceField != null)
                materialsUpdated = RetargetTextMeshProMaterials(mobileDistanceField);

            int shadersIncluded = EnsureAlwaysIncludedShaders();
            if (materialsUpdated > 0 || shadersIncluded > 0)
                Debug.Log($"[ZeyWinAds] TextMesh Pro shader configuration applied. materialsUpdated={materialsUpdated}, alwaysIncludedAdded={shadersIncluded}");
        }

        private static int RetargetTextMeshProMaterials(Shader targetShader)
        {
            int updated = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:Material"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null || material.shader == null)
                    continue;

                if (!material.shader.name.StartsWith("TextMeshPro/", StringComparison.Ordinal))
                    continue;

                if (material.shader == targetShader)
                    continue;

                material.shader = targetShader;
                EditorUtility.SetDirty(material);
                updated++;
            }

            return updated;
        }

        private static int EnsureAlwaysIncludedShaders()
        {
            var graphicsSettings = GraphicsSettings.GetGraphicsSettings();
            if (graphicsSettings == null)
                return 0;

            var serialized = new SerializedObject(graphicsSettings);
            var shaders = serialized.FindProperty("m_AlwaysIncludedShaders");
            if (shaders == null || !shaders.isArray)
                return 0;

            int added = 0;
            foreach (string shaderName in RequiredTmpShaderNames)
            {
                Shader shader = Shader.Find(shaderName);
                if (shader == null || ContainsObjectReference(shaders, shader))
                    continue;

                shaders.InsertArrayElementAtIndex(shaders.arraySize);
                shaders.GetArrayElementAtIndex(shaders.arraySize - 1).objectReferenceValue = shader;
                added++;
            }

            if (added > 0)
            {
                serialized.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(graphicsSettings);
            }

            return added;
        }

        private static bool ContainsObjectReference(SerializedProperty array, UnityEngine.Object value)
        {
            for (int i = 0; i < array.arraySize; i++)
            {
                if (array.GetArrayElementAtIndex(i).objectReferenceValue == value)
                    return true;
            }

            return false;
        }

        private static bool SetStringIfDifferent(SerializedObject serialized, string propertyName, string value)
        {
            var property = serialized.FindProperty(propertyName);
            if (property == null || property.stringValue == value)
                return false;

            property.stringValue = value;
            return true;
        }

        private static bool SetObjectReference(SerializedObject serialized, string propertyName, string assetPath, bool overwriteExisting)
        {
            var property = serialized.FindProperty(propertyName);
            if (property == null)
                return false;

            var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);
            if (asset == null)
                return false;

            if (!overwriteExisting && property.objectReferenceValue != null)
                return false;

            if (property.objectReferenceValue == asset)
                return false;

            property.objectReferenceValue = asset;
            return true;
        }

        private static bool AddFallbackFont(SerializedProperty array, string assetPath)
        {
            if (array == null || !array.isArray)
                return false;

            var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);
            if (asset == null)
                return false;

            for (int i = 0; i < array.arraySize; i++)
            {
                if (array.GetArrayElementAtIndex(i).objectReferenceValue == asset)
                    return false;
            }

            array.InsertArrayElementAtIndex(array.arraySize);
            array.GetArrayElementAtIndex(array.arraySize - 1).objectReferenceValue = asset;
            return true;
        }

        private static void ExtractUnityPackage(string packagePath, string label)
        {
            if (string.IsNullOrEmpty(packagePath) || !File.Exists(packagePath))
                return;

            var records = new Dictionary<string, UnityPackageRecord>(StringComparer.Ordinal);
            using (var file = File.OpenRead(packagePath))
            using (var gzip = new GZipStream(file, CompressionMode.Decompress))
            {
                while (TryReadTarEntry(gzip, out string entryName, out byte[] data))
                {
                    string normalized = NormalizeTarEntryName(entryName);
                    if (string.IsNullOrEmpty(normalized))
                        continue;

                    string[] parts = normalized.Split('/');
                    if (parts.Length < 2)
                        continue;

                    string id = parts[0];
                    string kind = parts[1];
                    if (!records.TryGetValue(id, out var record))
                    {
                        record = new UnityPackageRecord();
                        records[id] = record;
                    }

                    if (kind == "pathname")
                        record.PathName = Encoding.UTF8.GetString(data).Trim('\0', '\r', '\n', ' ');
                    else if (kind == "asset")
                        record.Asset = data;
                    else if (kind == "asset.meta")
                        record.Meta = data;
                }
            }

            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            int written = 0;
            foreach (var record in records.Values)
            {
                string assetPath = NormalizeAssetPath(record.PathName);
                if (string.IsNullOrEmpty(assetPath))
                    continue;

                string fullPath = Path.GetFullPath(Path.Combine(projectRoot, assetPath));
                if (!fullPath.StartsWith(projectRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                    continue;

                if (record.Asset != null)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
                    File.WriteAllBytes(fullPath, record.Asset);
                    written++;
                }
                else
                {
                    Directory.CreateDirectory(fullPath);
                }

                if (record.Meta != null)
                    File.WriteAllBytes(fullPath + ".meta", record.Meta);
            }

            Debug.Log($"[ZeyWinAds] Extracted {label} fallback files: {written}");
        }

        private static bool TryReadTarEntry(Stream stream, out string entryName, out byte[] data)
        {
            entryName = null;
            data = null;

            byte[] header = new byte[512];
            int read = ReadExactly(stream, header, 0, header.Length);
            if (read == 0)
                return false;
            if (read < header.Length || IsZeroBlock(header))
                return false;

            entryName = Encoding.UTF8.GetString(header, 0, 100).Trim('\0');
            long size = ParseOctal(header, 124, 12);
            if (size < 0 || size > int.MaxValue)
                throw new InvalidDataException("Unsupported unitypackage entry size.");

            data = new byte[size];
            ReadExactly(stream, data, 0, data.Length);

            long padding = (512 - (size % 512)) % 512;
            if (padding > 0)
            {
                byte[] skip = new byte[padding];
                ReadExactly(stream, skip, 0, skip.Length);
            }

            return true;
        }

        private static int ReadExactly(Stream stream, byte[] buffer, int offset, int count)
        {
            int total = 0;
            while (total < count)
            {
                int read = stream.Read(buffer, offset + total, count - total);
                if (read == 0)
                    break;
                total += read;
            }

            return total;
        }

        private static bool IsZeroBlock(byte[] block)
        {
            for (int i = 0; i < block.Length; i++)
            {
                if (block[i] != 0)
                    return false;
            }

            return true;
        }

        private static long ParseOctal(byte[] buffer, int offset, int count)
        {
            long value = 0;
            for (int i = offset; i < offset + count; i++)
            {
                byte b = buffer[i];
                if (b == 0 || b == (byte)' ')
                    continue;
                if (b < '0' || b > '7')
                    break;
                value = (value * 8) + (b - '0');
            }

            return value;
        }

        private static string NormalizeTarEntryName(string entryName)
        {
            if (string.IsNullOrEmpty(entryName))
                return null;

            entryName = entryName.Replace('\\', '/').TrimStart('/');
            if (entryName.StartsWith("./", StringComparison.Ordinal))
                entryName = entryName.Substring(2);

            return entryName;
        }

        private static string NormalizeAssetPath(string pathName)
        {
            if (string.IsNullOrEmpty(pathName))
                return null;

            string path = pathName.Replace('\\', '/').TrimStart('/');
            if (!path.StartsWith("Assets/", StringComparison.Ordinal) || path.Contains("../"))
                return null;

            return path;
        }

        private sealed class UnityPackageRecord
        {
            public string PathName;
            public byte[] Asset;
            public byte[] Meta;
        }
    }

    public sealed class TextMeshProBuildPreprocessor : IPreprocessBuildWithReport
    {
        public int callbackOrder => -10000;

        public void OnPreprocessBuild(BuildReport report)
        {
            TextMeshProBootstrap.EnsureInstalledAndConfigured();
        }
    }
}
