// Photon VR Plus - editor tooling. Based on PhotonVR by fchb1239 (MIT). See LICENSE.md.
#if UNITY_EDITOR
using System;
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
    /// <summary>
    /// Tools > PhotonVR menu:
    ///  - Setup Scene: manager + spawner + voice objects, rig auto-assigned.
    ///  - Create Player Prefab: builds Resources/PhotonVR/Player with head/body/hands and the 5 cosmetic slots.
    ///  - Create Settings Asset: Resources/PhotonVRSettings for App ID / Voice App ID / region / version.
    ///  - Validate Setup: logs everything that is missing.
    /// </summary>
    public static class PhotonVRSetupMenu
    {
        public const string DefaultRoot = "Assets/PhotonVR";
        public const string PlayerPrefabPath = DefaultRoot + "/Resources/PhotonVR/Player.prefab";
        /// <summary>Used when the default prefab lives in a read-only UPM package.</summary>
        public const string CustomPlayerPrefabPath = DefaultRoot + "/Resources/PhotonVR/PlayerCustom.prefab";
        public const string CustomPlayerResourcesName = "PhotonVR/PlayerCustom";
        public const string SettingsPath = DefaultRoot + "/Resources/PhotonVRSettings.asset";

        // ---------------------------------------------------------------- Scene

        [MenuItem("Tools/PhotonVR/Setup Scene", false, 0)]
        public static void SetupScene()
        {
            // Manager
            PhotonVRManager manager = UnityEngine.Object.FindObjectOfType<PhotonVRManager>();
            if (manager == null)
            {
                var go = new GameObject("PhotonVRManager");
                Undo.RegisterCreatedObjectUndo(go, "Create PhotonVRManager");
                manager = go.AddComponent<PhotonVRManager>();
                if (string.IsNullOrEmpty(manager.AppId))
                    manager.AppId = PhotonNetwork.PhotonServerSettings != null ? PhotonNetwork.PhotonServerSettings.AppSettings.AppIdRealtime : "";
                if (string.IsNullOrEmpty(manager.VoiceAppId))
                    manager.VoiceAppId = PhotonNetwork.PhotonServerSettings != null ? PhotonNetwork.PhotonServerSettings.AppSettings.AppIdVoice : "";
            }
            Undo.RecordObject(manager, "Setup PhotonVRManager");
            if (manager.Settings == null)
                manager.Settings = AssetDatabase.LoadAssetAtPath<PhotonVRSettings>(SettingsPath);
            manager.CheckDefaultValues();
            if (manager.Head == null && Camera.main != null)
                manager.Head = Camera.main.transform;
            EditorUtility.SetDirty(manager);

            // Spawner
            if (UnityEngine.Object.FindObjectOfType<PlayerSpawner>() == null)
            {
                var go = new GameObject("PhotonVRPlayerSpawner");
                Undo.RegisterCreatedObjectUndo(go, "Create PlayerSpawner");
                go.AddComponent<PlayerSpawner>();
            }

            // Voice
            if (UnityEngine.Object.FindObjectOfType<PhotonVRVoice>() == null)
            {
                VoiceConnection existing = UnityEngine.Object.FindObjectsOfType<VoiceConnection>()
                    .FirstOrDefault(v => v.GetType().Name == "PunVoiceClient" || v.GetType().Name == "PhotonVoiceNetwork");
                GameObject go = existing != null ? existing.gameObject : new GameObject("PhotonVRVoice");
                if (existing == null)
                {
                    Undo.RegisterCreatedObjectUndo(go, "Create PhotonVRVoice");
                    Type client = FindType("Photon.Voice.PUN.PunVoiceClient") ?? FindType("Photon.Voice.PUN.PhotonVoiceNetwork");
                    if (client != null)
                        Undo.AddComponent(go, client);
                    else
                        Debug.LogWarning("[PhotonVR] Photon Voice PUN client type not found - is Photon Voice 2 imported?");
                }
                Recorder recorder = go.GetComponent<Recorder>() ?? Undo.AddComponent<Recorder>(go);
                PhotonVRVoice voice = Undo.AddComponent<PhotonVRVoice>(go);
                voice.Recorder = recorder;
                VoiceConnection vc = go.GetComponent<VoiceConnection>();
                if (vc != null) vc.PrimaryRecorder = recorder;
            }

            // Player prefab
            if (Resources.Load<GameObject>("PhotonVR/Player") == null)
            {
                if (EditorUtility.DisplayDialog("PhotonVR", "No player prefab found at Resources/PhotonVR/Player. Create one now?", "Create", "Skip"))
                    CreatePlayerPrefab();
            }

            EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);
            Selection.activeObject = manager.gameObject;
            ValidateSetup();
        }

        // ---------------------------------------------------------------- Settings

        [MenuItem("Tools/PhotonVR/Create Settings Asset", false, 20)]
        public static PhotonVRSettings CreateSettingsAsset()
        {
            var existing = PhotonVRSettings.LoadDefault();
            if (existing != null)
            {
                Selection.activeObject = existing;
                EditorGUIUtility.PingObject(existing);
                return existing;
            }

            EnsureFolder(Path.GetDirectoryName(SettingsPath).Replace('\\', '/'));
            var settings = ScriptableObject.CreateInstance<PhotonVRSettings>();
            if (PhotonNetwork.PhotonServerSettings != null)
            {
                settings.AppId = PhotonNetwork.PhotonServerSettings.AppSettings.AppIdRealtime;
                settings.VoiceAppId = PhotonNetwork.PhotonServerSettings.AppSettings.AppIdVoice;
            }
            AssetDatabase.CreateAsset(settings, SettingsPath);
            AssetDatabase.SaveAssets();
            Selection.activeObject = settings;
            Debug.Log("[PhotonVR] Created " + SettingsPath + " - enter your App ID and Voice App ID there.");
            return settings;
        }

        // ---------------------------------------------------------------- Player prefab

        [MenuItem("Tools/PhotonVR/Create Player Prefab", false, 10)]
        public static GameObject CreatePlayerPrefab()
        {
            // Overwrite the existing Resources/PhotonVR/Player if it is in Assets (avoids duplicate Resources paths);
            // if it lives in a read-only package, write PlayerCustom and point the spawners at it.
            string path = PlayerPrefabPath;
            bool usePackageSafePath = IsDefaultPrefabInPackage();
            GameObject existingDefault = Resources.Load<GameObject>("PhotonVR/Player");
            if (usePackageSafePath)
                path = CustomPlayerPrefabPath;
            else if (existingDefault != null)
                path = AssetDatabase.GetAssetPath(existingDefault);
            if (File.Exists(path) && !EditorUtility.DisplayDialog("PhotonVR", path + " already exists. Overwrite it?", "Overwrite", "Cancel"))
                return AssetDatabase.LoadAssetAtPath<GameObject>(path);

            EnsureFolder(Path.GetDirectoryName(path).Replace('\\', '/'));

            GameObject root = BuildPlayer();
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path, out bool ok);
            UnityEngine.Object.DestroyImmediate(root);

            if (ok)
            {
                if (usePackageSafePath)
                    PointSpawnersAt(CustomPlayerResourcesName);
                Debug.Log("[PhotonVR] Player prefab created at " + path + ". Add cosmetics as children of the *Cosmetics objects (name = cosmetic ID).");
                EditorGUIUtility.PingObject(prefab);
            }
            else
                Debug.LogError("[PhotonVR] Failed to save " + path);
            return prefab;
        }

        [MenuItem("Tools/PhotonVR/Copy Default Player Prefab To Assets", false, 11)]
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
                Debug.Log("[PhotonVR] The default player prefab is already in your Assets: " + sourcePath + " - edit it directly.");
                EditorGUIUtility.PingObject(source);
                return;
            }
            EnsureFolder(Path.GetDirectoryName(CustomPlayerPrefabPath).Replace('\\', '/'));
            if (AssetDatabase.CopyAsset(sourcePath, CustomPlayerPrefabPath))
            {
                PointSpawnersAt(CustomPlayerResourcesName);
                Debug.Log("[PhotonVR] Copied to " + CustomPlayerPrefabPath + " and set PlayerSpawner.PrefabLocation = " + CustomPlayerResourcesName);
                EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<GameObject>(CustomPlayerPrefabPath));
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

        /// <summary>Builds the networked player hierarchy in the open scene (not saved).</summary>
        public static GameObject BuildPlayer()
        {
            Material mat = FindAsset<Material>("PlayerMaterial t:Material") ?? new Material(Shader.Find("Standard"));
            GameObject headModel = FindAsset<GameObject>("VRHead t:Model");
            GameObject bodyModel = FindAsset<GameObject>("VRBody t:Model");
            GameObject handModel = FindAsset<GameObject>("VRHand t:Model");

            var root = new GameObject("Player");
            PhotonView view = root.AddComponent<PhotonView>();
            PhotonVRPlayer player = root.AddComponent<PhotonVRPlayer>();
            root.AddComponent<PhotonVoiceView>();

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

            // PhotonView observes PhotonVRPlayer (head + hands with interpolation)
            view.Synchronization = ViewSynchronization.UnreliableOnChange;
            view.observableSearch = PhotonView.ObservableSearch.Manual;
            view.ObservedComponents = new System.Collections.Generic.List<Component> { player };
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
            GameObject model = FindAsset<GameObject>(modelName + " t:Model");
            if (model == null) return;
            var go = (GameObject)PrefabUtility.InstantiatePrefab(model);
            go.name = modelName; // the object name IS the cosmetic ID
            go.transform.SetParent(slot, false);
            foreach (Collider c in go.GetComponentsInChildren<Collider>(true))
                UnityEngine.Object.DestroyImmediate(c);
            go.SetActive(false);
        }

        // ---------------------------------------------------------------- Validation

        [MenuItem("Tools/PhotonVR/Validate Setup", false, 30)]
        public static void ValidateSetup()
        {
            int problems = 0;
            void Problem(string msg) { problems++; Debug.LogWarning("[PhotonVR] " + msg); }

            var manager = UnityEngine.Object.FindObjectOfType<PhotonVRManager>();
            if (manager == null) Problem("No PhotonVRManager in the scene.");
            else
            {
                bool appId = !string.IsNullOrEmpty(manager.AppId) || (manager.Settings != null && !string.IsNullOrEmpty(manager.Settings.AppId));
                bool voiceId = !string.IsNullOrEmpty(manager.VoiceAppId) || (manager.Settings != null && !string.IsNullOrEmpty(manager.Settings.VoiceAppId));
                if (!appId) Problem("AppId is empty (PhotonVRManager or PhotonVRSettings).");
                if (!voiceId) Problem("VoiceAppId is empty - voice will not connect.");
                if (manager.Head == null && Camera.main == null) Problem("No Head assigned and no camera tagged MainCamera.");
                if (manager.LeftHand == null) Problem("LeftHand is not assigned on PhotonVRManager.");
                if (manager.RightHand == null) Problem("RightHand is not assigned on PhotonVRManager.");
            }
            if (UnityEngine.Object.FindObjectOfType<PlayerSpawner>() == null) Problem("No PlayerSpawner in the scene.");
            if (UnityEngine.Object.FindObjectOfType<PhotonVRVoice>() == null) Problem("No PhotonVRVoice in the scene (no microphone transmission).");

            var prefabs = Resources.LoadAll<GameObject>("PhotonVR").Where(g => g.name == "Player" && g.GetComponent<PhotonVRPlayer>() != null).ToArray();
            if (prefabs.Length == 0) Problem("No Resources/PhotonVR/Player prefab (Tools > PhotonVR > Create Player Prefab).");
            if (prefabs.Length > 1) Problem("More than one Resources/PhotonVR/Player prefab - Unity picks one at random. Delete the one you do not use:\n" +
                                            string.Join("\n", prefabs.Select(AssetDatabase.GetAssetPath)));
            foreach (var p in prefabs)
            {
                var player = p.GetComponent<PhotonVRPlayer>();
                foreach (string slot in PhotonVRCosmeticSlots.All)
                    if (player.CosmeticSlots == null || !player.CosmeticSlots.Any(s => s != null && s.SlotName == slot && s.Object != null))
                        Problem($"{AssetDatabase.GetAssetPath(p)}: cosmetic slot '{slot}' is missing or has no Object.");
            }

            if (problems == 0) Debug.Log("[PhotonVR] Setup looks good.");
        }

        // ---------------------------------------------------------------- helpers

        private static T FindAsset<T>(string filter) where T : UnityEngine.Object
        {
            string name = filter.Split(' ')[0];
            foreach (string guid in AssetDatabase.FindAssets(filter))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileNameWithoutExtension(path) == name)
                {
                    var asset = AssetDatabase.LoadAssetAtPath<T>(path);
                    if (asset != null) return asset;
                }
            }
            return null;
        }

        private static Type FindType(string fullName)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type t = asm.GetType(fullName, false);
                if (t != null) return t;
            }
            return null;
        }

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }
    }
}
#endif
