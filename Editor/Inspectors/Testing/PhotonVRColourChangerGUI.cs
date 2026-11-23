#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

using Photon.Pun;

namespace Photon.VR.Testing
{
    [CustomEditor(typeof(PhotonVRColourChanger))]
    public class PhotonVRColourChangerGUI : Editor
    {
        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();
            PhotonVRColourChanger manager = (PhotonVRColourChanger)target;
            using (new EditorGUI.DisabledScope(!Application.isPlaying || PhotonVRManager.Manager == null))
            {
                if (GUILayout.Button("Change"))
                    manager.ChangeColour(manager.Colour);
            }
            if (!PhotonNetwork.InRoom)
                GUILayout.Label("Not in a room (the change is saved and applied when you join)");
        }
    }
}
#endif
