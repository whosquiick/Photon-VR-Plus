#if UNITY_EDITOR
using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Photon.VR
{
    // Makes sure the project has its own random ID key (Assets/PhotonVR-ProjectKey) and loads it in the editor.
    // The generated script registers the same key in play mode and in builds.
    [InitializeOnLoad]
    internal static class PhotonVRProjectKeyEditor
    {
        internal const string Folder = "Assets/PhotonVR-ProjectKey";
        internal const string ScriptPath = Folder + "/PhotonVRProjectKey.cs";
        private const string AsmdefPath = Folder + "/PhotonVR.ProjectKey.asmdef";
        private const int KeyLength = 32;

        static PhotonVRProjectKeyEditor()
        {
            // Load synchronously so OnValidate calls during this reload already see the key.
            string path = FindKeyScript();
            if (path == null)
                path = CreateKeyScript();
            if (path != null && !Load(path))
                PhotonVRLog.Warn("Couldn't read " + path + ". Delete it to make a new key (you'll need to set your App IDs again).");

            EditorApplication.delayCall += UpgradeSettingsAssets;
        }

        internal static string FindKeyScript()
        {
            if (File.Exists(ScriptPath)) return ScriptPath;
            // The user may have moved the folder.
            try
            {
                string[] found = Directory.GetFiles("Assets", "PhotonVRProjectKey.cs", SearchOption.AllDirectories);
                return found.Length > 0 ? found[0].Replace('\\', '/') : null;
            }
            catch (IOException) { return null; }
        }

        private static bool Load(string path)
        {
            string text = File.ReadAllText(path);
            byte[] a = ParseBytes(text, "A");
            byte[] b = ParseBytes(text, "B");
            if (a == null || b == null || a.Length != b.Length) return false;
            PhotonVRIdProtector.UseProjectKey(a, b);
            return true;
        }

        private static byte[] ParseBytes(string text, string field)
        {
            Match m = Regex.Match(text, @"byte\[\]\s+" + field + @"\s*=\s*\{([^}]*)\}");
            if (!m.Success) return null;
            string[] parts = m.Groups[1].Value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            var bytes = new byte[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                string p = parts[i].Trim();
                if (p.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) p = p.Substring(2);
                if (!byte.TryParse(p, System.Globalization.NumberStyles.HexNumber, null, out bytes[i])) return null;
            }
            return bytes;
        }

        private static string CreateKeyScript()
        {
            try
            {
                Directory.CreateDirectory(Folder);
                byte[] a = PhotonVRIdProtector.RandomBytes(KeyLength);
                byte[] b = PhotonVRIdProtector.RandomBytes(KeyLength);

                var sb = new StringBuilder();
                sb.AppendLine("// Made by Photon VR Plus. This is your project's App ID key.");
                sb.AppendLine("// Keep it in version control so teammates can read the saved IDs, and don't post it anywhere.");
                sb.AppendLine("// Deleting it makes a new key, and then you'll have to set your App IDs again.");
                sb.AppendLine("namespace Photon.VR.Generated");
                sb.AppendLine("{");
                sb.AppendLine("    internal static class PhotonVRProjectKey");
                sb.AppendLine("    {");
                sb.AppendLine("        private static readonly byte[] A = { " + Hex(a) + " };");
                sb.AppendLine("        private static readonly byte[] B = { " + Hex(b) + " };");
                sb.AppendLine();
                sb.AppendLine("        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]");
                sb.AppendLine("        private static void Register() => Photon.VR.PhotonVRIdProtector.UseProjectKey(A, B);");
                sb.AppendLine("    }");
                sb.AppendLine("}");
                File.WriteAllText(ScriptPath, sb.ToString());

                if (!File.Exists(AsmdefPath))
                    File.WriteAllText(AsmdefPath,
                        "{\n    \"name\": \"PhotonVR.ProjectKey\",\n    \"references\": [ \"PhotonVR\" ],\n    \"autoReferenced\": false\n}\n");

                EditorApplication.delayCall += () => AssetDatabase.Refresh();
                Debug.Log("[PhotonVR] Made a project key at " + ScriptPath + ". Commit it with your project.");
                return ScriptPath;
            }
            catch (Exception e)
            {
                PhotonVRLog.Warn("Couldn't create the project key (" + e.Message + "). App IDs will use the shared package key.");
                return null;
            }
        }

        private static string Hex(byte[] bytes)
        {
            var sb = new StringBuilder(bytes.Length * 6);
            for (int i = 0; i < bytes.Length; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append("0x").Append(bytes[i].ToString("x2"));
            }
            return sb.ToString();
        }

        // Re-save settings assets made before 1.3.0 so they use the project key.
        private static void UpgradeSettingsAssets()
        {
            if (!PhotonVRIdProtector.HasProjectKey) return;
            foreach (string guid in AssetDatabase.FindAssets("t:PhotonVRSettings"))
            {
                var settings = AssetDatabase.LoadAssetAtPath<PhotonVRSettings>(AssetDatabase.GUIDToAssetPath(guid));
                if (settings == null || !settings.UpgradeIds()) continue;
                EditorUtility.SetDirty(settings);
                AssetDatabase.SaveAssets();
            }
        }
    }

    // Stops a build that would ship IDs it can't read.
    internal class PhotonVRProjectKeyBuildCheck : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            if (PhotonVRIdProtector.HasProjectKey && PhotonVRProjectKeyEditor.FindKeyScript() == null)
                throw new BuildFailedException("[PhotonVR] " + PhotonVRProjectKeyEditor.ScriptPath +
                    " is missing, so the build couldn't read your App IDs. Restart Unity to make a new key, then set the IDs again.");
        }
    }
}
#endif
