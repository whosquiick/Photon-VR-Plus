using System;
using System.Collections.Generic;
using UnityEngine;

namespace Photon.VR.Saving
{
    /// <summary>What PhotonVR saves for the local player.</summary>
    [Serializable]
    public class PhotonVRSaveData
    {
        public string Username;
        public Color Colour;
        public bool HasColour;
        public Dictionary<string, string> Cosmetics;
        public Dictionary<string, string> CosmeticTints;
    }

    /// <summary>
    /// A place to keep player data (a backend, PlayFab, a cloud save). Implement it and call
    /// <see cref="PhotonVRSaveSystem.Register"/>. Callbacks may arrive on a later frame.
    /// </summary>
    public interface IPhotonVRSaveProvider
    {
        string Name { get; }
        /// <summary>Loads the player's data; call <paramref name="done"/> with null when there is nothing saved.</summary>
        void Load(Action<PhotonVRSaveData> done);
        void Save(PhotonVRSaveData data);
    }

    /// <summary>
    /// Saving for name, colour and cosmetics. PlayerPrefs is always written (same keys as the original PhotonVR);
    /// registered providers are loaded at startup (the newest answer wins) and saved a short moment after every change,
    /// so rapid cosmetic changes turn into one backend call.
    /// </summary>
    public static class PhotonVRSaveSystem
    {
        /// <summary>Seconds to wait after a change before saving to providers.</summary>
        public static float SaveDelay = 2f;

        public static event Action<PhotonVRSaveData> Loaded;

        private static readonly List<IPhotonVRSaveProvider> providers = new List<IPhotonVRSaveProvider>();
        private static float saveAt = -1f;

        public static IReadOnlyList<IPhotonVRSaveProvider> Providers => providers;

        /// <summary>Adds a provider. With <paramref name="loadNow"/>, its data is loaded and applied right away.</summary>
        public static void Register(IPhotonVRSaveProvider provider, bool loadNow = true)
        {
            if (provider == null || providers.Contains(provider)) return;
            providers.Add(provider);
            if (loadNow) Load(provider);
        }

        public static void Unregister(IPhotonVRSaveProvider provider) => providers.Remove(provider);

        public static void LoadLocalPlayer()
        {
            foreach (IPhotonVRSaveProvider p in providers.ToArray())
                Load(p);
        }

        /// <summary>Queues a save to every provider (PlayerPrefs is already written by the caller).</summary>
        public static void SaveLocalPlayer()
        {
            if (providers.Count > 0)
                saveAt = Time.realtimeSinceStartup + SaveDelay;
        }

        /// <summary>Saves to every provider immediately (e.g. from OnApplicationPause).</summary>
        public static void Flush()
        {
            saveAt = -1f;
            if (PhotonVRManager.Manager == null) return;
            PhotonVRSaveData data = PhotonVRManager.Manager.CaptureSaveData();
            foreach (IPhotonVRSaveProvider p in providers)
            {
                try { p.Save(data); }
                catch (Exception e) { PhotonVRLog.Error($"Save provider {p.Name} failed: {e.Message}"); }
            }
        }

        internal static void Tick()
        {
            if (saveAt > 0f && Time.realtimeSinceStartup >= saveAt)
                Flush();
        }

        private static void Load(IPhotonVRSaveProvider provider)
        {
            try
            {
                provider.Load(data =>
                {
                    if (data == null || PhotonVRManager.Manager == null) return;
                    PhotonVRManager.Manager.ApplySaveData(data);
                    Loaded?.Invoke(data);
                });
            }
            catch (Exception e)
            {
                PhotonVRLog.Error($"Save provider {provider.Name} failed to load: {e.Message}");
            }
        }
    }

    /// <summary>Stores the save data as one JSON string in PlayerPrefs (an example provider; PhotonVR already uses PlayerPrefs itself).</summary>
    public class PhotonVRPlayerPrefsSaveProvider : IPhotonVRSaveProvider
    {
        private readonly string key;
        public PhotonVRPlayerPrefsSaveProvider(string key = "PhotonVRSave") { this.key = key; }
        public string Name => "PlayerPrefs";

        public void Load(Action<PhotonVRSaveData> done) => done(PhotonVRSaveJson.FromJson(PlayerPrefs.GetString(key, "")));

        public void Save(PhotonVRSaveData data)
        {
            PlayerPrefs.SetString(key, PhotonVRSaveJson.ToJson(data));
            PlayerPrefs.Save();
        }
    }

    /// <summary>JSON helpers for save data (JsonUtility cannot serialise dictionaries, so they are flattened).</summary>
    public static class PhotonVRSaveJson
    {
        [Serializable]
        private class Flat
        {
            public string Username;
            public Color Colour;
            public bool HasColour;
            public List<string> CosmeticKeys = new List<string>();
            public List<string> CosmeticValues = new List<string>();
            public List<string> TintKeys = new List<string>();
            public List<string> TintValues = new List<string>();
        }

        public static string ToJson(PhotonVRSaveData data)
        {
            if (data == null) return "";
            var f = new Flat { Username = data.Username, Colour = data.Colour, HasColour = data.HasColour };
            if (data.Cosmetics != null)
                foreach (var kv in data.Cosmetics) { f.CosmeticKeys.Add(kv.Key); f.CosmeticValues.Add(kv.Value); }
            if (data.CosmeticTints != null)
                foreach (var kv in data.CosmeticTints) { f.TintKeys.Add(kv.Key); f.TintValues.Add(kv.Value); }
            return JsonUtility.ToJson(f);
        }

        public static PhotonVRSaveData FromJson(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            Flat f;
            try { f = JsonUtility.FromJson<Flat>(json); }
            catch (Exception) { return null; }
            if (f == null) return null;
            var data = new PhotonVRSaveData
            {
                Username = f.Username, Colour = f.Colour, HasColour = f.HasColour,
                Cosmetics = new Dictionary<string, string>(), CosmeticTints = new Dictionary<string, string>()
            };
            for (int i = 0; i < Math.Min(f.CosmeticKeys.Count, f.CosmeticValues.Count); i++) data.Cosmetics[f.CosmeticKeys[i]] = f.CosmeticValues[i];
            for (int i = 0; i < Math.Min(f.TintKeys.Count, f.TintValues.Count); i++) data.CosmeticTints[f.TintKeys[i]] = f.TintValues[i];
            return data;
        }
    }
}
