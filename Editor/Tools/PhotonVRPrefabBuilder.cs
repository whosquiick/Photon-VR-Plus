#if UNITY_EDITOR
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

using Photon.Pun;
using Photon.Voice.PUN;
using Photon.Voice.Unity;
using Photon.VR.Player;

using TMPro;

namespace Photon.VR
{
    // Tools > PhotonVR > Prefabs: rebuilds the player and manager prefabs from scratch in your Assets.
    public static class PhotonVRPrefabBuilder
    {
        public const string ManagerPrefabPath = PhotonVRSetupMenu.DefaultRoot + "/Prefabs/PhotonVRManagerCustom.prefab";

        [MenuItem("Tools/PhotonVR/Prefabs/Create Player Prefab", false, 10)]
        public static GameObject CreatePlayerPrefab()
        {
            // Overwrite the existing Resources/PhotonVR/Player if it is in Assets (avoids duplicate Resources paths);
            // if it lives in a read-only package, write PlayerCustom and point the spawners at it.
            string path = PhotonVRSetupMenu.PlayerPrefabPath;
            bool usePackageSafePath = IsDefaultPrefabInPackage();
            GameObject existingDefault = Resources.Load<GameObject>("PhotonVR/Player");
            if (usePackageSafePath)
                path = PhotonVRSetupMenu.CustomPlayerPrefabPath;
            else if (existingDefault != null)
                path = AssetDatabase.GetAssetPath(existingDefault);
            if (File.Exists(path) && !EditorUtility.DisplayDialog("PhotonVR", path + " already exists. Overwrite it?", "Overwrite", "Cancel"))
                return AssetDatabase.LoadAssetAtPath<GameObject>(path);

            PhotonVREditorUtil.EnsureFolder(Path.GetDirectoryName(path).Replace('\\', '/'));

            GameObject root = BuildPlayer();
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path, out bool ok);
            UnityEngine.Object.DestroyImmediate(root);

            if (ok)
            {
                if (usePackageSafePath)
                    PointSpawnersAt(PhotonVRSetupMenu.CustomPlayerResourcesName);
                PhotonVRLog.Info("Player prefab created at " + path + ". Add cosmetics as children of the *Cosmetics objects (name = cosmetic ID).");
                EditorGUIUtility.PingObject(prefab);
            }
            else
                PhotonVRLog.Error("Failed to save " + path);
            return prefab;
        }

        [MenuItem("Tools/PhotonVR/Prefabs/Copy Default Player Prefab To Assets", false, 11)]
        public static void CopyDefaultPlayerPrefab()
        {
            GameObject source = Resources.Load<GameObject>("PhotonVR/Player");
            if (source == null)
            {
                CreatePlayerPrefab();
                return;
            }
            string sourcePath = AssetDatabase.GetAssetPath(source);
            if (!sourcePath.StartsWith("Packages/"))
            {
                PhotonVRLog.Info("The default player prefab is already in your Assets: " + sourcePath + " - edit it directly.");
                EditorGUIUtility.PingObject(source);
                return;
            }
            PhotonVREditorUtil.EnsureFolder(Path.GetDirectoryName(PhotonVRSetupMenu.CustomPlayerPrefabPath).Replace('\\', '/'));
            if (AssetDatabase.CopyAsset(sourcePath, PhotonVRSetupMenu.CustomPlayerPrefabPath))
            {
                PointSpawnersAt(PhotonVRSetupMenu.CustomPlayerResourcesName);
                PhotonVRLog.Info("Copied to " + PhotonVRSetupMenu.CustomPlayerPrefabPath + " and set PlayerSpawner.PrefabLocation = " + PhotonVRSetupMenu.CustomPlayerResourcesName);
                EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<GameObject>(PhotonVRSetupMenu.CustomPlayerPrefabPath));
            }
        }

        private static bool IsDefaultPrefabInPackage()
        {
            GameObject existing = Resources.Load<GameObject>("PhotonVR/Player");
            return existing != null && AssetDatabase.GetAssetPath(existing).StartsWith("Packages/");
        }

        private static void PointSpawnersAt(string resourcesName)
        {
            foreach (PlayerSpawner spawner in UnityEngine.Object.FindObjectsOfType<PlayerSpawner>())
            {
                Undo.RecordObject(spawner, "PhotonVR prefab location");
                spawner.PrefabLocation = resourcesName;
                EditorUtility.SetDirty(spawner);
                EditorSceneManager.MarkSceneDirty(spawner.gameObject.scene);
            }
        }

        // Builds the networked player hierarchy in the open scene (not saved).
        public static GameObject BuildPlayer()
        {
            Material mat = PhotonVREditorUtil.FindAsset<Material>("PlayerMaterial t:Material") ?? new Material(Shader.Find("Standard"));
            GameObject headModel = PhotonVREditorUtil.FindAsset<GameObject>("VRHead t:Model");
            GameObject bodyModel = PhotonVREditorUtil.FindAsset<GameObject>("VRBody t:Model");
            GameObject handModel = PhotonVREditorUtil.FindAsset<GameObject>("VRHand t:Model");

            var root = new GameObject("Player");
            PhotonView view = root.AddComponent<PhotonView>();
            PhotonVRPlayer player = root.AddComponent<PhotonVRPlayer>();
            PhotonVRPoseSync poseSync = root.AddComponent<PhotonVRPoseSync>();
            root.AddComponent<PhotonVoiceView>();
            PhotonVRPlayerVoice playerVoice = root.AddComponent<PhotonVRPlayerVoice>();

            // Head
            Transform head = NewChild("Head", root.transform);
            MeshRenderer headRenderer = AddModel(headModel, PrimitiveType.Sphere, new Vector3(0.25f, 0.25f, 0.25f), head, mat);
            Transform headCos = NewChild("HeadCosmetics", head);
            Transform faceCos = NewChild("FaceCosmetics", head);
            AddSampleCosmetic("VRTopHat", headCos);
            AddSampleCosmetic("VRSunglasses", faceCos);

            Transform speaker = NewChild("Speaker", head);
            var source = speaker.gameObject.AddComponent<AudioSource>();
            source.spatialBlend = 1f;
            source.playOnAwake = true;
            source.rolloffMode = AudioRolloffMode.Logarithmic;
            source.maxDistance = 50f;
            speaker.gameObject.AddComponent<Speaker>();

            // Body
            Transform body = NewChild("Body", root.transform);
            body.localPosition = new Vector3(0, -0.35f, 0);
            MeshRenderer bodyRenderer = AddModel(bodyModel, PrimitiveType.Capsule, new Vector3(0.3f, 0.25f, 0.3f), body, mat);
            Transform bodyCos = NewChild("BodyCosmetics", body);
            var bodyFollow = body.gameObject.AddComponent<PhotonVRPlayerBody>();
            bodyFollow.Head = head;
            bodyFollow.Offset = -0.35f;

            // Hands
            Transform left = NewChild("LeftHand", root.transform);
            MeshRenderer leftRenderer = AddModel(handModel, PrimitiveType.Sphere, new Vector3(0.08f, 0.08f, 0.08f), left, mat);
            Transform leftCos = NewChild("LeftHandCosmetics", left);
            Transform right = NewChild("RightHand", root.transform);
            MeshRenderer rightRenderer = AddModel(handModel, PrimitiveType.Sphere, new Vector3(0.08f, 0.08f, 0.08f), right, mat);
            if (handModel != null && rightRenderer != null)
            {
                // mirror the hand model for the right side
                Vector3 s = rightRenderer.transform.localScale;
                rightRenderer.transform.localScale = new Vector3(-Mathf.Abs(s.x), s.y, s.z);
            }
            Transform rightCos = NewChild("RightHandCosmetics", right);

            // Name tag
            Transform nameRoot = NewChild("PlayerName", root.transform);
            nameRoot.localPosition = new Vector3(0, 0.17f, 0);
            var nameFollow = nameRoot.gameObject.AddComponent<PhotonVRPlayerName>();
            nameFollow.Head = head;
            nameFollow.Offset = 0.15f;
            var textGo = new GameObject("Text (TMP)", typeof(RectTransform));
            textGo.transform.SetParent(nameRoot, false);
            textGo.transform.localRotation = Quaternion.Euler(0, 180, 0);
            var tmp = textGo.AddComponent<TextMeshPro>();
            tmp.text = "PlayerName";
            tmp.fontSize = 0.7f;
            tmp.alignment = TextAlignmentOptions.Center;
            ((RectTransform)textGo.transform).sizeDelta = new Vector2(20, 5);

            // Wire up PhotonVRPlayer (field names identical to the original PhotonVR)
            player.Head = head;
            player.Body = body;
            player.LeftHand = left;
            player.RightHand = right;
            player.NameText = tmp;
            player.HideLocalPlayer = false;
            player.ColourObjects = new[] { headRenderer, bodyRenderer, leftRenderer, rightRenderer }.Where(r => r != null).ToList();
            player.CosmeticSlots = new System.Collections.Generic.List<PhotonVRPlayer.CosmeticSlot>
            {
                new PhotonVRPlayer.CosmeticSlot { SlotName = PhotonVRCosmeticSlots.Head, Object = headCos },
                new PhotonVRPlayer.CosmeticSlot { SlotName = PhotonVRCosmeticSlots.Face, Object = faceCos },
                new PhotonVRPlayer.CosmeticSlot { SlotName = PhotonVRCosmeticSlots.Body, Object = bodyCos },
                new PhotonVRPlayer.CosmeticSlot { SlotName = PhotonVRCosmeticSlots.LeftHand, Object = leftCos },
                new PhotonVRPlayer.CosmeticSlot { SlotName = PhotonVRCosmeticSlots.RightHand, Object = rightCos },
            };

            poseSync.Head = head;
            poseSync.LeftHand = left;
            poseSync.RightHand = right;

            playerVoice.SpeakerParent = head;

            // The PhotonView only observes the pose sync; everything else goes through properties / RPCs.
            view.Synchronization = ViewSynchronization.UnreliableOnChange;
            view.observableSearch = PhotonView.ObservableSearch.Manual;
            view.ObservedComponents = new System.Collections.Generic.List<Component> { poseSync };
            return root;
        }

        private static Transform NewChild(string name, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        private static MeshRenderer AddModel(GameObject model, PrimitiveType fallback, Vector3 fallbackScale, Transform parent, Material mat)
        {
            GameObject go;
            if (model != null)
            {
                go = (GameObject)PrefabUtility.InstantiatePrefab(model);
                go.transform.SetParent(parent, false);
            }
            else
            {
                go = GameObject.CreatePrimitive(fallback);
                go.name = "Model";
                go.transform.SetParent(parent, false);
                go.transform.localScale = fallbackScale;
            }
            // No colliders on the network model - they would hit your own rig's hands.
            foreach (Collider c in go.GetComponentsInChildren<Collider>(true))
                UnityEngine.Object.DestroyImmediate(c);

            MeshRenderer r = go.GetComponentInChildren<MeshRenderer>();
            if (r != null && mat != null)
                r.sharedMaterial = mat;
            return r;
        }

        private static void AddSampleCosmetic(string modelName, Transform slot)
        {
            GameObject model = PhotonVREditorUtil.FindAsset<GameObject>(modelName + " t:Model");
            if (model == null) return;
            var go = (GameObject)PrefabUtility.InstantiatePrefab(model);
            go.name = modelName; // the object name IS the cosmetic ID
            go.transform.SetParent(slot, false);
            foreach (Collider c in go.GetComponentsInChildren<Collider>(true))
                UnityEngine.Object.DestroyImmediate(c);
            go.SetActive(false);
        }

        [MenuItem("Tools/PhotonVR/Prefabs/Create Manager Prefab", false, 12)]
        public static GameObject CreateManagerPrefab()
        {
            string path = ManagerPrefabPath;
            if (File.Exists(path) && !EditorUtility.DisplayDialog("PhotonVR", path + " already exists. Overwrite it?", "Overwrite", "Cancel"))
                return AssetDatabase.LoadAssetAtPath<GameObject>(path);

            PhotonVREditorUtil.EnsureFolder(Path.GetDirectoryName(path).Replace('\\', '/'));
            GameObject root = BuildManager();
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path, out bool ok);
            Object.DestroyImmediate(root);
            if (ok)
            {
                PhotonVRLog.Info("Manager prefab created at " + path + ".");
                EditorGUIUtility.PingObject(prefab);
            }
            return prefab;
        }

        // Manager + voice client + spawner in one drop-in hierarchy (not saved).
        public static GameObject BuildManager()
        {
            var root = new GameObject("PhotonVRManager");
            PhotonVRManager manager = root.AddComponent<PhotonVRManager>();
            manager.Settings = PhotonVRSettings.LoadDefault();

            var voiceGo = new GameObject("Voice");
            voiceGo.transform.SetParent(root.transform, false);
            System.Type client = PhotonVREditorUtil.FindType("Photon.Voice.PUN.PunVoiceClient") ?? PhotonVREditorUtil.FindType("Photon.Voice.PUN.PhotonVoiceNetwork");
            if (client != null)
            {
                var vc = (VoiceConnection)voiceGo.AddComponent(client);
                // The manager root already survives scene loads; a child calling DontDestroyOnLoad would only warn.
                var so = new SerializedObject(vc);
                SerializedProperty ddol = so.FindProperty("ApplyDontDestroyOnLoad");
                if (ddol != null) { ddol.boolValue = false; so.ApplyModifiedPropertiesWithoutUndo(); }
            }
            Recorder recorder = voiceGo.AddComponent<Recorder>();
            PhotonVRVoice voice = voiceGo.AddComponent<PhotonVRVoice>();
            voice.Recorder = recorder;
            VoiceConnection connection = voiceGo.GetComponent<VoiceConnection>();
            if (connection != null) connection.PrimaryRecorder = recorder;

            var spawnerGo = new GameObject("PlayerSpawner");
            spawnerGo.transform.SetParent(root.transform, false);
            spawnerGo.AddComponent<PlayerSpawner>();
            return root;
        }
    }
}
#endif
