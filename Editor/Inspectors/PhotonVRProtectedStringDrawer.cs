#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

namespace Photon.VR
{
    // Shows an encrypted ID as a masked preview with Set / Clear. The typed value goes straight into
    // PhotonVRIdProtector and only the ciphertext + salt are written to the serialized object.
    [CustomPropertyDrawer(typeof(PhotonVRProtectedString))]
    public class PhotonVRProtectedStringDrawer : PropertyDrawer
    {
        private const float ButtonWidth = 46f;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            SerializedProperty cipher = property.FindPropertyRelative("cipher");
            SerializedProperty salt = property.FindPropertyRelative("salt");

            EditorGUI.BeginProperty(position, label, property);
            Rect field = EditorGUI.PrefixLabel(position, label);

            Rect preview = new Rect(field.x, field.y, field.width - ButtonWidth * 2 - 4, field.height);
            Rect setRect = new Rect(preview.xMax + 2, field.y, ButtonWidth, field.height);
            Rect clearRect = new Rect(setRect.xMax + 2, field.y, ButtonWidth, field.height);

            bool mixed = property.serializedObject.isEditingMultipleObjects;
            string text = mixed ? "—" : Mask(cipher.stringValue, salt.stringValue);
            using (new EditorGUI.DisabledScope(true))
                EditorGUI.TextField(preview, text);

            if (GUI.Button(setRect, "Set"))
            {
                string title = label.text;
                PhotonVRSecretPrompt.Show(title, value => Write(property, value));
            }

            using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(cipher.stringValue)))
            {
                if (GUI.Button(clearRect, "Clear"))
                    Write(property, null);
            }

            EditorGUI.EndProperty();
        }

        internal static void Write(SerializedProperty property, string value)
        {
            property.serializedObject.Update();
            SerializedProperty cipher = property.FindPropertyRelative("cipher");
            SerializedProperty salt = property.FindPropertyRelative("salt");

            if (string.IsNullOrEmpty(value))
            {
                cipher.stringValue = string.Empty;
                salt.stringValue = string.Empty;
            }
            else
            {
                string newSalt = PhotonVRIdProtector.NewSalt();
                salt.stringValue = newSalt;
                cipher.stringValue = PhotonVRIdProtector.Protect(value.Trim(), newSalt);
            }
            property.serializedObject.ApplyModifiedProperties();
        }

        // Decrypting on every repaint is wasteful, so remember the last result per ciphertext.
        private static readonly System.Collections.Generic.Dictionary<string, string> masks =
            new System.Collections.Generic.Dictionary<string, string>();

        internal static string Mask(string cipher, string salt)
        {
            if (string.IsNullOrEmpty(cipher)) return "(not set)";
            if (masks.TryGetValue(cipher, out string cached)) return cached;
            string mask = PhotonVRIdProtector.IsFromOtherProject(cipher)
                ? "(saved with another project key - set it again)"
                : PhotonVRProtectedString.Mask(PhotonVRIdProtector.Unprotect(cipher, salt), true);
            if (masks.Count > 32) masks.Clear();
            masks[cipher] = mask;
            return mask;
        }
    }

    // Small modal-ish window with a password field, used by the drawer.
    internal class PhotonVRSecretPrompt : EditorWindow
    {
        private string value = "";
        private System.Action<string> onSubmit;
        private bool focused;

        public static void Show(string title, System.Action<string> submit)
        {
            var window = CreateInstance<PhotonVRSecretPrompt>();
            window.titleContent = new GUIContent("Set " + title);
            window.onSubmit = submit;
            window.minSize = window.maxSize = new Vector2(360, 82);
            window.ShowUtility();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Paste the ID. It is encrypted before it is saved.", EditorStyles.wordWrappedMiniLabel);
            GUI.SetNextControlName("secret");
            value = EditorGUILayout.PasswordField(value);
            if (!focused)
            {
                EditorGUI.FocusTextInControl("secret");
                focused = true;
            }

            bool enter = Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Return;
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Cancel", GUILayout.Width(80))) Close();
                if (GUILayout.Button("Save", GUILayout.Width(80)) || enter)
                {
                    onSubmit?.Invoke(value);
                    value = "";
                    Close();
                }
            }
        }

        private void OnDestroy() => value = "";
    }
}
#endif
