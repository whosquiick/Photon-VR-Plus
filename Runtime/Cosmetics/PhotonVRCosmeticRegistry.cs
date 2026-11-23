using System;
using System.Collections.Generic;
using UnityEngine;

namespace Photon.VR
{
    /// <summary>
    /// Cosmetics that are spawned on demand instead of living in the player prefab. Put entries in a registry asset
    /// (Assets > Create > PhotonVR > Cosmetic Registry, saved as <c>Resources/PhotonVRCosmeticRegistry</c>) or call
    /// <see cref="Register"/> at runtime, e.g. after downloading an asset bundle. When a player equips an ID that is
    /// not a child of the slot, the registry prefab is instantiated under the slot and named after the ID.
    /// </summary>
    [CreateAssetMenu(fileName = "PhotonVRCosmeticRegistry", menuName = "PhotonVR/Cosmetic Registry")]
    public class PhotonVRCosmeticRegistry : ScriptableObject
    {
        public const string ResourcesName = "PhotonVRCosmeticRegistry";

        [Serializable]
        public class Entry
        {
            public string Id;
            public string Slot = PhotonVRCosmeticSlots.Head;
            public string DisplayName;
            public GameObject Prefab;
            public Vector3 LocalPosition;
            public Vector3 LocalEulerAngles;
            public Vector3 LocalScale = Vector3.one;
        }

        public List<Entry> Cosmetics = new List<Entry>();

        private static readonly Dictionary<(string, string), Entry> runtime = new Dictionary<(string, string), Entry>();
        private static bool loadedDefault;

        public static event Action<Entry> Registered;

        public static void Register(Entry entry)
        {
            if (entry == null || string.IsNullOrEmpty(entry.Id) || entry.Prefab == null) return;
            runtime[(entry.Slot ?? "", entry.Id)] = entry;
            Registered?.Invoke(entry);
        }

        public static void Register(string slot, string id, GameObject prefab) =>
            Register(new Entry { Slot = slot, Id = id, Prefab = prefab });

        public static void Unregister(string slot, string id) => runtime.Remove((slot ?? "", id));

        public void RegisterAll()
        {
            foreach (Entry e in Cosmetics) Register(e);
        }

        public static bool TryGet(string slot, string id, out Entry entry)
        {
            LoadDefault();
            return runtime.TryGetValue((slot ?? "", id ?? ""), out entry);
        }

        /// <summary>Every registered cosmetic for a slot (for shop menus).</summary>
        public static List<Entry> GetForSlot(string slot)
        {
            LoadDefault();
            var list = new List<Entry>();
            foreach (var kv in runtime)
                if (kv.Key.Item1 == slot) list.Add(kv.Value);
            return list;
        }

        /// <summary>Instantiates a registered cosmetic under <paramref name="container"/> if it is not there yet.</summary>
        public static Transform EnsureLoaded(Transform container, string slot, string id)
        {
            if (container == null || string.IsNullOrEmpty(id)) return null;
            foreach (Transform child in container)
                if (PhotonVRCosmeticUtility.Matches(child, id)) return child;
            if (!TryGet(slot, id, out Entry entry)) return null;

            GameObject go = Instantiate(entry.Prefab, container, false);
            go.name = id;
            go.SetActive(false);
            go.transform.localPosition = entry.LocalPosition;
            go.transform.localEulerAngles = entry.LocalEulerAngles;
            go.transform.localScale = entry.LocalScale;
            // Network avatars should not collide with your own rig.
            foreach (Collider c in go.GetComponentsInChildren<Collider>(true)) c.enabled = false;
            return go.transform;
        }

        private static void LoadDefault()
        {
            if (loadedDefault) return;
            loadedDefault = true;
            var asset = Resources.Load<PhotonVRCosmeticRegistry>(ResourcesName);
            if (asset != null) asset.RegisterAll();
        }
    }
}
