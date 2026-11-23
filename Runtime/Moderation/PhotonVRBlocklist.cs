using System.Collections.Generic;
using UnityEngine;

namespace Photon.VR
{
    /// <summary>
    /// Editable word lists for <see cref="PhotonVRWordFilter"/>. Create one with
    /// Assets > Create > PhotonVR > Blocklist and save it as <c>Resources/PhotonVRBlocklist</c>.
    /// Without an asset the default lists in <c>Resources/PhotonVR/Filter</c> are used.
    /// </summary>
    [CreateAssetMenu(fileName = "PhotonVRBlocklist", menuName = "PhotonVR/Blocklist")]
    public class PhotonVRBlocklist : ScriptableObject
    {
        public const string ResourcesName = "PhotonVRBlocklist";
        public const string DefaultBlockedResource = "PhotonVR/Filter/DefaultBlocklist";
        public const string DefaultAllowedResource = "PhotonVR/Filter/DefaultAllowlist";

        [Tooltip("Start from the default lists shipped with the package. Turn off to use only your own lists.")]
        public bool IncludeDefaultLists = true;
        [Tooltip("Text files in the blocklist format (one entry per line).")]
        public List<TextAsset> BlockedLists = new List<TextAsset>();
        public List<TextAsset> AllowedLists = new List<TextAsset>();
        [Tooltip("Extra blocked entries: word, =wholeword, raw:1234.")]
        public List<string> ExtraBlocked = new List<string>();
        [Tooltip("Words that should never be blocked even though they contain a blocked word.")]
        public List<string> ExtraAllowed = new List<string>();

        public PhotonVRWordFilter Build()
        {
            var filter = new PhotonVRWordFilter(null);
            if (IncludeDefaultLists) AddDefaults(filter);
            foreach (TextAsset t in BlockedLists) if (t != null) filter.AddBlocked(Lines(t));
            foreach (TextAsset t in AllowedLists) if (t != null) filter.AddAllowed(Lines(t));
            filter.AddBlocked(ExtraBlocked);
            filter.AddAllowed(ExtraAllowed);
            return filter;
        }

        public static PhotonVRWordFilter BuildFilter()
        {
            PhotonVRBlocklist asset = Resources.Load<PhotonVRBlocklist>(ResourcesName);
            if (asset != null) return asset.Build();
            var filter = new PhotonVRWordFilter(null);
            AddDefaults(filter);
            return filter;
        }

        private static void AddDefaults(PhotonVRWordFilter filter)
        {
            TextAsset blocked = Resources.Load<TextAsset>(DefaultBlockedResource);
            TextAsset allowed = Resources.Load<TextAsset>(DefaultAllowedResource);
            if (blocked != null) filter.AddBlocked(Lines(blocked));
            else PhotonVRLog.Warn("Default blocklist not found at Resources/" + DefaultBlockedResource);
            if (allowed != null) filter.AddAllowed(Lines(allowed));
        }

        private static string[] Lines(TextAsset t) => t.text.Split('\n');
    }
}
