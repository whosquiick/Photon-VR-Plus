using UnityEngine;
using UnityEngine.Events;

namespace Photon.VR
{
    /// <summary>
    /// Optional marker for a cosmetic model under a slot container (e.g. Head/HeadCosmetics/VRTopHat).
    /// A cosmetic works without this component: its GameObject name is its ID, exactly like the original PhotonVR.
    /// Add it when you want a display name, an ID that differs from the object name, or equip/unequip events.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("PhotonVR/PhotonVR Cosmetic")]
    public class PhotonVRCosmetic : MonoBehaviour
    {
        [Tooltip("Cosmetic ID used by PhotonVRManager.SetCosmetic. Empty = the GameObject name.")]
        public string CosmeticId = "";
        [Tooltip("Slot this cosmetic belongs to (informational; used by editor validation).")]
        public string Slot = PhotonVRCosmeticSlots.Head;
        [Tooltip("Human readable name for shops / menus.")]
        public string DisplayName = "";

        /// <summary>Invoked when the cosmetic gets equipped (its GameObject is enabled).</summary>
        public UnityEvent OnEquipped = new UnityEvent();
        /// <summary>Invoked when the cosmetic gets unequipped (its GameObject is disabled).</summary>
        public UnityEvent OnUnequipped = new UnityEvent();

        /// <summary>The effective ID: <see cref="CosmeticId"/>, or the GameObject name when empty.</summary>
        public string Id => string.IsNullOrEmpty(CosmeticId) ? name : CosmeticId;

        /// <summary>The effective display name: <see cref="DisplayName"/>, or <see cref="Id"/>.</summary>
        public string Label => string.IsNullOrEmpty(DisplayName) ? Id : DisplayName;

        private void OnEnable() => OnEquipped?.Invoke();
        private void OnDisable() => OnUnequipped?.Invoke();
    }
}
