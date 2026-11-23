#if PHOTONVR_PLAYFAB
using System;
using System.Collections.Generic;

using PlayFab;
using PlayFab.ClientModels;
using UnityEngine;

namespace Photon.VR.PlayFab
{
    /// <summary>
    /// The one place to set up PlayFab: Title ID (stored encrypted like the Photon App IDs), login, and the small
    /// economy helpers below (currency, items, purchases). Everything from <see cref="PhotonVRPlayFabAuth"/> works the
    /// same here; use one or the other, not both.
    /// </summary>
    public class PhotonVRPlayFabManager : PhotonVRPlayFabAuth
    {
        [Header("PlayFab")]
        [Tooltip("PlayFab Title ID. Stored encrypted; leave the Title ID in PlayFabSharedSettings empty so it isn't also stored in plain text.")]
        [SerializeField] private PhotonVRProtectedString titleId = new PhotonVRProtectedString();
        [Tooltip("Keep the Title ID in a session-keyed vault in memory (same as HardenMemory on the PhotonVR manager).")]
        public bool HardenTitleId = false;
        [Tooltip("Catalog version for items and purchases. Empty = the title's primary catalog.")]
        public string CatalogVersion = "";

        /// <summary>The active manager (null when none is in the scene).</summary>
        public static PhotonVRPlayFabManager Instance { get; private set; }

        /// <summary>Raised after the inventory and currency balances are (re)loaded.</summary>
        public static event Action InventoryChanged;

        private static readonly Dictionary<string, int> currency = new Dictionary<string, int>();
        private static readonly List<ItemInstance> items = new List<ItemInstance>();
        private static readonly Dictionary<string, CatalogItem> catalog = new Dictionary<string, CatalogItem>();
        private static bool catalogLoaded;

        private PhotonVRIdVault vault;

        /// <summary>The Title ID (decrypted on demand). Setting it re-encrypts.</summary>
        public string TitleId
        {
            get => vault != null ? vault.Reveal(PhotonVRAppIdKind.PlayFabTitle) : titleId.Get();
            set
            {
                if (vault != null) vault.Store(PhotonVRAppIdKind.PlayFabTitle, value);
                if (vault == null || Application.isEditor) titleId.Set(value);
            }
        }

        private void Reset() => UsePhotonCustomAuth = false;

        private void Awake()
        {
            Instance = this;
            if (!HardenTitleId || !titleId.HasValue) return;
            vault = new PhotonVRIdVault();
            vault.Store(PhotonVRAppIdKind.PlayFabTitle, titleId.GetBytes());
#if !UNITY_EDITOR
            titleId.ForgetInMemory();
#endif
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            LoginSucceeded += OnLogin;
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            LoginSucceeded -= OnLogin;
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            if (Instance == this) Instance = null;
            vault?.Clear();
        }

        // Load Owned Cosmetics already refreshes the inventory on login; otherwise load it here for the balances.
        private void OnLogin(string playFabId)
        {
            if (!LoadOwnedCosmetics) RefreshInventory();
        }

        protected override void ApplyTitleId()
        {
            string id = TitleId;
            // PlayFab needs the Title ID in its settings for every request, so it is plain text in memory from here on.
            if (!string.IsNullOrEmpty(id)) PlayFabSettings.staticSettings.TitleId = id;
        }

        // ---- Inventory and items ----

        /// <summary>Item IDs the player owns (from the last <see cref="RefreshInventory"/>).</summary>
        public static IEnumerable<string> OwnedItemIds
        {
            get
            {
                foreach (ItemInstance item in items)
                    if (item != null && !string.IsNullOrEmpty(item.ItemId)) yield return item.ItemId;
            }
        }

        /// <summary>True when the player has at least one of this catalog item.</summary>
        public static bool OwnsItem(string itemId) => FindItem(itemId) != null;

        /// <summary>Reloads inventory and currency balances (GetUserInventory). Also updates owned cosmetics if Load Owned Cosmetics is on.</summary>
        public static void RefreshInventory(Action done = null)
        {
            if (!PlayFabClientAPI.IsClientLoggedIn()) return;
            PlayFabClientAPI.GetUserInventory(new GetUserInventoryRequest(), result =>
            {
                items.Clear();
                string version = Instance != null ? Instance.CatalogVersion : "";
                if (result.Inventory != null)
                    foreach (ItemInstance item in result.Inventory)
                        if (item != null && (string.IsNullOrEmpty(version) || item.CatalogVersion == version))
                            items.Add(item);
                currency.Clear();
                if (result.VirtualCurrency != null)
                    foreach (var kv in result.VirtualCurrency) currency[kv.Key] = kv.Value;

                if (Instance != null && Instance.LoadOwnedCosmetics) Instance.ApplyOwnedCosmetics();
                done?.Invoke();
                InventoryChanged?.Invoke();
            }, e => PhotonVRLog.Warn("PlayFab inventory not loaded: " + e.ErrorMessage));
        }

        /// <summary>Looks up a catalog item (loads the catalog once). Calls back with null when it doesn't exist.</summary>
        public static void GetCatalogItem(string itemId, Action<CatalogItem> done)
        {
            if (catalogLoaded)
            {
                catalog.TryGetValue(itemId ?? "", out CatalogItem cached);
                done?.Invoke(cached);
                return;
            }
            if (!PlayFabClientAPI.IsClientLoggedIn()) { done?.Invoke(null); return; }
            string version = Instance != null && !string.IsNullOrEmpty(Instance.CatalogVersion) ? Instance.CatalogVersion : null;
            PlayFabClientAPI.GetCatalogItems(new GetCatalogItemsRequest { CatalogVersion = version }, result =>
            {
                catalog.Clear();
                if (result.Catalog != null)
                    foreach (CatalogItem item in result.Catalog)
                        if (item != null && item.ItemId != null) catalog[item.ItemId] = item;
                catalogLoaded = true;
                catalog.TryGetValue(itemId ?? "", out CatalogItem found);
                done?.Invoke(found);
            }, e =>
            {
                PhotonVRLog.Warn("PlayFab catalog not loaded: " + e.ErrorMessage);
                done?.Invoke(null);
            });
        }

        /// <summary>
        /// Buys a catalog item with virtual currency (PurchaseItem). <paramref name="price"/> must match the catalog
        /// price; PlayFab checks it and the balance on its side. Refreshes the inventory on success.
        /// </summary>
        public static void PurchaseItem(string itemId, string currencyCode, int price, Action<bool> done = null)
        {
            if (!PlayFabClientAPI.IsClientLoggedIn() || string.IsNullOrEmpty(itemId)) { done?.Invoke(false); return; }
            string version = Instance != null && !string.IsNullOrEmpty(Instance.CatalogVersion) ? Instance.CatalogVersion : null;
            PlayFabClientAPI.PurchaseItem(new PurchaseItemRequest { ItemId = itemId, VirtualCurrency = currencyCode, Price = price, CatalogVersion = version },
                _ => RefreshInventory(() => done?.Invoke(true)),
                e =>
                {
                    PhotonVRLog.Warn($"PlayFab purchase of {itemId} failed: {e.ErrorMessage}");
                    done?.Invoke(false);
                });
        }

        /// <summary>Buys the catalog item for a cosmetic (Cosmetic Item Prefix + ID) and marks the cosmetic owned.</summary>
        public static void PurchaseCosmetic(string cosmeticId, string currencyCode, int price, Action<bool> done = null)
        {
            string prefix = Instance != null ? Instance.CosmeticItemPrefix ?? "" : "";
            PurchaseItem(prefix + cosmeticId, currencyCode, price, ok =>
            {
                if (ok) PhotonVRManager.GrantCosmetic(cosmeticId);
                done?.Invoke(ok);
            });
        }

        /// <summary>Uses up <paramref name="count"/> of a consumable item (ConsumeItem).</summary>
        public static void ConsumeItem(string itemId, int count = 1, Action<bool> done = null)
        {
            ItemInstance item = FindItem(itemId);
            if (item == null || !PlayFabClientAPI.IsClientLoggedIn()) { done?.Invoke(false); return; }
            PlayFabClientAPI.ConsumeItem(new ConsumeItemRequest { ItemInstanceId = item.ItemInstanceId, ConsumeCount = count },
                _ => RefreshInventory(() => done?.Invoke(true)),
                e =>
                {
                    PhotonVRLog.Warn($"PlayFab consume of {itemId} failed: {e.ErrorMessage}");
                    done?.Invoke(false);
                });
        }

        private static ItemInstance FindItem(string itemId)
        {
            if (string.IsNullOrEmpty(itemId)) return null;
            foreach (ItemInstance item in items)
                if (item != null && item.ItemId == itemId) return item;
            return null;
        }

        // ---- Currency ----

        /// <summary>All virtual currency balances from the last refresh (code -> amount).</summary>
        public static IReadOnlyDictionary<string, int> GetCurrency() => currency;

        /// <summary>Balance of one currency from the last refresh (0 when unknown).</summary>
        public static int GetBalance(string currencyCode) =>
            currencyCode != null && currency.TryGetValue(currencyCode, out int amount) ? amount : 0;

        /// <summary>"1,250 GC" style text for UI.</summary>
        public static string FormatBalance(string currencyCode) => GetBalance(currencyCode).ToString("N0") + " " + currencyCode;

        /// <summary>
        /// Spends currency from the client (SubtractUserVirtualCurrency). PlayFab blocks this unless the title's API
        /// policy allows it; prefer <see cref="PurchaseItem"/> or a CloudScript function.
        /// </summary>
        public static void SpendCurrency(string currencyCode, int amount, Action<bool> done = null)
        {
            if (!PlayFabClientAPI.IsClientLoggedIn() || amount <= 0) { done?.Invoke(false); return; }
            PlayFabClientAPI.SubtractUserVirtualCurrency(new SubtractUserVirtualCurrencyRequest { VirtualCurrency = currencyCode, Amount = amount },
                r =>
                {
                    currency[r.VirtualCurrency] = r.Balance;
                    done?.Invoke(true);
                    InventoryChanged?.Invoke();
                },
                e =>
                {
                    PhotonVRLog.Warn("PlayFab spend failed: " + e.ErrorMessage);
                    done?.Invoke(false);
                });
        }

        /// <summary>
        /// Adds currency from the client (AddUserVirtualCurrency). Disabled by PlayFab by default, and it should stay
        /// that way in a real game: anyone can call it. Grant currency from the server (CloudScript, Server API) instead.
        /// Useful for testing.
        /// </summary>
        public static void GrantCurrency(string currencyCode, int amount, Action<bool> done = null)
        {
            if (!PlayFabClientAPI.IsClientLoggedIn() || amount <= 0) { done?.Invoke(false); return; }
            PlayFabClientAPI.AddUserVirtualCurrency(new AddUserVirtualCurrencyRequest { VirtualCurrency = currencyCode, Amount = amount },
                r =>
                {
                    currency[r.VirtualCurrency] = r.Balance;
                    done?.Invoke(true);
                    InventoryChanged?.Invoke();
                },
                e =>
                {
                    PhotonVRLog.Warn("PlayFab grant failed (client grants are off unless allowed in the title's API policy): " + e.ErrorMessage);
                    done?.Invoke(false);
                });
        }
    }
}
#endif
