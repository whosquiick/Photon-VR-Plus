using System.Collections.Generic;
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

        [Header("Tint")]
        [Tooltip("Renderers coloured by PhotonVRManager.SetCosmeticTint. Empty = not tintable.")]
        public List<Renderer> TintRenderers = new List<Renderer>();
        [Tooltip("Material slots to tint on each renderer. Empty = all.")]
        public List<int> TintMaterialIndices = new List<int>();

        /// <summary>Invoked when the cosmetic gets equipped (its GameObject is enabled).</summary>
        public UnityEvent OnEquipped = new UnityEvent();
        /// <summary>Invoked when the cosmetic gets unequipped (its GameObject is disabled).</summary>
        public UnityEvent OnUnequipped = new UnityEvent();

        /// <summary>The effective ID: <see cref="CosmeticId"/>, or the GameObject name when empty.</summary>
        public string Id => string.IsNullOrEmpty(CosmeticId) ? name : CosmeticId;

        /// <summary>The effective display name: <see cref="DisplayName"/>, or <see cref="Id"/>.</summary>
        public string Label => string.IsNullOrEmpty(DisplayName) ? Id : DisplayName;

        // Looked up on first use, not in a static initializer: Unity can load MonoBehaviours off the main thread.
        private static int colorId = -1, baseColorId = -1;
        private static int ColorId => colorId != -1 ? colorId : (colorId = Shader.PropertyToID("_Color"));
        private static int BaseColorId => baseColorId != -1 ? baseColorId : (baseColorId = Shader.PropertyToID("_BaseColor"));
        private MaterialPropertyBlock block;

        /// <summary>Applies a tint (null removes it). Uses property blocks, so shared materials stay untouched.</summary>
        public void ApplyTint(Color? tint)
        {
            if (TintRenderers == null) return;
            if (block == null) block = new MaterialPropertyBlock();
            foreach (Renderer r in TintRenderers)
            {
                if (r == null) continue;
                int count = r.sharedMaterials.Length;
                for (int i = 0; i < count; i++)
                {
                    if (TintMaterialIndices != null && TintMaterialIndices.Count > 0 && !TintMaterialIndices.Contains(i)) continue;
                    if (tint.HasValue)
                    {
                        r.GetPropertyBlock(block, i);
                        block.SetColor(ColorId, tint.Value);
                        block.SetColor(BaseColorId, tint.Value);
                        r.SetPropertyBlock(block, i);
                    }
                    else
                    {
                        r.SetPropertyBlock(null, i);
                    }
                }
            }
        }

        private void OnEnable() => OnEquipped?.Invoke();
        private void OnDisable() => OnUnequipped?.Invoke();
    }
}
