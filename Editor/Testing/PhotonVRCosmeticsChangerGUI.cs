// Photon VR Plus - based on PhotonVR by fchb1239 (MIT). See LICENSE.md.
#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

using Photon.Pun;

namespace Photon.VR.Testing
{
    [CustomEditor(typeof(PhotonVRCosmeticsChanger))]
    public class PhotonVRCosmeticsChangerGUI : Editor
    {
        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();
            PhotonVRCosmeticsChanger manager = (PhotonVRCosmeticsChanger)target;
            using (new EditorGUI.DisabledScope(!Application.isPlaying || PhotonVRManager.Manager == null))
            {
                if (GUILayout.Button("Change"))
                    manager.ChangeCosmetics();
            }
            if (!PhotonNetwork.InRoom)
                GUILayout.Label("Not in a room (the change is saved and applied when you join)");
        }
    }
}
#endif
