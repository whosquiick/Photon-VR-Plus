#if PHOTONVR_PLAYFAB
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;

using Photon.Pun;
using PlayFab;
using PlayFab.ClientModels;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Photon.VR.PlayFab
{
    /// <summary>
    /// The one place to set up PlayFab, laid out like the usual gorilla-tag "Playfablogin" script: Title ID (stored
    /// encrypted like the Photon App IDs), login, special items, currency, bans, MOTD and the display name.
    /// Everything from <see cref="PhotonVRPlayFabAuth"/> works the same here; use one or the other, not both.
    /// Nothing runs per frame: it all happens on login and after purchases.
    /// </summary>
    public class PhotonVRPlayFabManager : PhotonVRPlayFabAuth
    {
        [Header("PLAYFAB")]
        [Tooltip("PlayFab Title ID. Stored encrypted; leave the Title ID in PlayFabSharedSettings empty so it isn't also stored in plain text.")]
        [SerializeField] private PhotonVRProtectedString titleId = new PhotonVRProtectedString();
        [Tooltip("Keep the Title ID in a session-keyed vault in memory (same as HardenMemory on the PhotonVR manager).")]
        public bool HardenTitleId = false;
        [Tooltip("Try the login again if it fails (2, 4, 8... seconds apart). Never retries a ban.")]
        public bool RetryLogin = false;
        public int MaxLoginRetries = 3;

        [Header("COSMETICS")]
        [Tooltip("Filled in after login.")]
        public string MyPlayFabID;
        [Tooltip("Catalog the items and purchases use. Empty = the title's primary catalog.")]
        public string CatalogName = "";
        [Tooltip("Turned on when the player owns an item whose ItemId is the object's name.")]
        public List<GameObject> specialitems = new List<GameObject>();
        [Tooltip("Turned off when the player owns an item whose ItemId is the object's name.")]
        public List<GameObject> disableitems = new List<GameObject>();

        [Header("CURRENCY")]
        [Tooltip("Virtual currency code from PlayFab > Economy > Currencies, e.g. HS.")]
        public string CurrencyCode = "";
        [Tooltip("Name shown in the text, e.g. Coins.")]
        public string CurrencyName = "Coins";
        [Tooltip("Shows \"You have N name\".")]
        public TextMeshPro currencyText;
        [Tooltip("Balance from the last refresh. Stays 0 with Hide Coins In Memory on; read Coins instead.")]
        public int coins;
        [Tooltip("Keep the balance masked in memory so memory scanners don't find it. The real balance is on PlayFab either way.")]
        public bool HideCoinsInMemory = false;

        [Header("BANNED")]
        [Tooltip("Optional. Scene loaded when the account is banned (must be in Build Settings). Empty = stay in this scene and show the banned items and texts.")]
        public string bannedscenename = "";
        [Tooltip("Turned on when the account is banned (after Photon disconnects).")]
        public List<GameObject> banneditems = new List<GameObject>();
        [Tooltip("Shows \"Time remaining: 613 hours\" or \"Time remaining: indefinitely\".")]
        public TextMeshPro banTimeRemainingText;
        [Tooltip("Shows the ban reason from PlayFab.")]
        public TextMeshPro banReasonText;

        [Header("TITLE DATA")]
        [Tooltip("Shows the title data key \"MOTD\" (PlayFab > Content > Title Data).")]
        public TextMeshPro MOTDText;

        [Header("PLAYER DATA")]
        [Tooltip("Shows the player's name.")]
        public TextMeshPro UserName;
        [Tooltip("New players get this name plus 4 random digits (PlayFab display names must be unique).")]
        public string StartingUsername = "";

        /// <summary>The active manager (null when none is in the scene).</summary>
        public static PhotonVRPlayFabManager Instance { get; private set; }

        /// <summary>Raised after the inventory and currency balances are (re)loaded.</summary>
        public static event Action InventoryChanged;

        /// <summary>Raised when PlayFab reports the account as banned (reason, hours left or -1 for indefinitely).</summary>
        public static event Action<string, int> Banned;

        /// <summary>True once PlayFab reported this account as banned.</summary>
        public static bool IsBanned { get; private set; }

        private static readonly Dictionary<string, int> currency = new Dictionary<string, int>();
        private static readonly List<ItemInstance> items = new List<ItemInstance>();
        private static readonly Dictionary<string, CatalogItem> catalog = new Dictionary<string, CatalogItem>();
        private static bool catalogLoaded;

        private PhotonVRIdVault vault;
        private int coinsMasked, coinsKey;
        private int loginAttempts;

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

        /// <summary>Balance of <see cref="CurrencyCode"/> (works with Hide Coins In Memory too).</summary>
        public int Coins => HideCoinsInMemory ? coinsMasked ^ coinsKey : coins;

        private void Reset()
        {
            UsePhotonCustomAuth = false;
            LoadOwnedCosmetics = true;
        }

        protected virtual void Awake()
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
            InventoryChanged += ShowInventory;
            PhotonVRManager.UsernameChanged += ShowName;
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            LoginSucceeded -= OnLogin;
            InventoryChanged -= ShowInventory;
            PhotonVRManager.UsernameChanged -= ShowName;
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            if (Instance == this) Instance = null;
            vault?.Clear();
        }

        protected override void ApplyTitleId()
        {
            string id = TitleId;
            // PlayFab needs the Title ID in its settings for every request, so it is plain text in memory from here on.
            if (!string.IsNullOrEmpty(id)) PlayFabSettings.staticSettings.TitleId = id;
        }

        protected override GetPlayerCombinedInfoRequestParams LoginInfo => new GetPlayerCombinedInfoRequestParams
        {
            GetPlayerProfile = true,
            ProfileConstraints = new PlayerProfileViewConstraints { ShowDisplayName = true }
        };

        protected override void OnLoginError(PlayFabError error)
        {
            if (HandleBan(error)) return;
            if (RetryLogin && loginAttempts < MaxLoginRetries)
            {
                float wait = Mathf.Min(30f, 2f * (1 << loginAttempts));
                loginAttempts++;
                PhotonVRLog.Warn($"PlayFab login failed ({error.ErrorMessage}), trying again in {wait:0}s.");
                StartCoroutine(RetryAfter(wait));
                return;
            }
            base.OnLoginError(error);
        }

        private IEnumerator RetryAfter(float seconds)
        {
            yield return new WaitForSecondsRealtime(seconds);
            RetryLastLogin();
        }

        private void OnLogin(string playFabId)
        {
            loginAttempts = 0;
            MyPlayFabID = playFabId;

            // Load Owned Cosmetics already refreshes the inventory on login; otherwise load it here for the items and coins.
            if (!LoadOwnedCosmetics) RefreshInventory();
            if (MOTDText != null) LoadMotd();
        }

        /// <summary>Same as the base login callback, plus the display name from the login result.</summary>
        public override void OnLoggedIn(LoginResult result)
        {
            base.OnLoggedIn(result);
            string displayName = result?.InfoResultPayload?.PlayerProfile?.DisplayName;
            if (!string.IsNullOrEmpty(displayName)) PhotonVRManager.TrySetUsername(displayName);
            else if (!string.IsNullOrEmpty(StartingUsername)) UpdateName(StartingUsername + UnityEngine.Random.Range(1000, 10000));
            ShowName(PhotonNetwork.NickName);
        }

        /// <summary>True once logged in to PlayFab.</summary>
        public static bool IsLoggedIn => LoggedIn;

        /// <summary>Reloads items, coins and the MOTD (e.g. after a server-side grant).</summary>
        public void RefreshAll()
        {
            RefreshInventory();
            if (MOTDText != null && LoggedIn) LoadMotd();
        }

        // Player data

        /// <summary>
        /// Changes the name: word filter first, then the Photon name and the PlayFab display name.
        /// Returns false when the filter refuses it.
        /// </summary>
        public bool UpdateName(string newName)
        {
            if (!PhotonVRManager.TrySetUsername(newName)) return false;
            // With Sync Display Name on, the name change event already pushes it.
            if (!SyncDisplayName) PushDisplayName(PhotonNetwork.NickName);
            return true;
        }

        private void ShowName(string playerName)
        {
            if (UserName != null) UserName.text = playerName;
        }

        private void LoadMotd()
        {
            PlayFabClientAPI.GetTitleData(new GetTitleDataRequest { Keys = new List<string> { "MOTD" } }, result =>
            {
                if (MOTDText != null && result.Data != null && result.Data.TryGetValue("MOTD", out string motd))
                    MOTDText.text = motd;
            }, e => ReportError("MOTD not loaded", e));
        }

        // Inventory and items

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

        /// <summary>
        /// Reloads items and currency balances with one GetUserInventory call. Also updates special items, the coin
        /// text and owned cosmetics (with Load Owned Cosmetics on).
        /// </summary>
        public static void RefreshInventory(Action done = null)
        {
            if (!PlayFabClientAPI.IsClientLoggedIn()) return;
            PlayFabClientAPI.GetUserInventory(new GetUserInventoryRequest(), result =>
            {
                items.Clear();
                string version = Instance != null ? Instance.CatalogName : "";
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
            }, e => ReportError("Inventory not loaded", e));
        }

        // Special items on when owned, disable items off when owned; the coin text and value.
        private void ShowInventory()
        {
            foreach (GameObject go in specialitems)
                if (go != null) go.SetActive(OwnsItem(go.name));
            foreach (GameObject go in disableitems)
                if (go != null) go.SetActive(!OwnsItem(go.name));

            int balance = GetBalance(CurrencyCode);
            if (HideCoinsInMemory)
            {
                coinsKey = UnityEngine.Random.Range(int.MinValue, int.MaxValue);
                coinsMasked = balance ^ coinsKey;
                coins = 0;
            }
            else coins = balance;
            if (currencyText != null) currencyText.text = $"You have {balance} {CurrencyName}";
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
            PlayFabClientAPI.GetCatalogItems(new GetCatalogItemsRequest { CatalogVersion = CatalogOrNull() }, result =>
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
                ReportError("Catalog not loaded", e);
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
            PlayFabClientAPI.PurchaseItem(new PurchaseItemRequest { ItemId = itemId, VirtualCurrency = currencyCode, Price = price, CatalogVersion = CatalogOrNull() },
                _ => RefreshInventory(() => done?.Invoke(true)),
                e =>
                {
                    ReportError($"Purchase of {itemId} failed", e);
                    done?.Invoke(false);
                });
        }

        /// <summary>Same as <see cref="PurchaseItem(string, int, Action{bool})"/>: buys with the manager's Currency Code.</summary>
        public static void BuyItem(string itemId, int price, Action<bool> done = null) => PurchaseItem(itemId, price, done);

        /// <summary>Buys a catalog item with the manager's Currency Code.</summary>
        public static void PurchaseItem(string itemId, int price, Action<bool> done = null) =>
            PurchaseItem(itemId, Instance != null ? Instance.CurrencyCode : "", price, done);

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
                    ReportError($"Consume of {itemId} failed", e);
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

        private static string CatalogOrNull() =>
            Instance != null && !string.IsNullOrEmpty(Instance.CatalogName) ? Instance.CatalogName : null;

        // Currency
        // There is deliberately no "add coins" call: anything the client can call, a cheater can call too.
        // Give currency from the server (CloudScript / Azure Functions / Server API) and call RefreshInventory after.

        /// <summary>All virtual currency balances from the last refresh (code -> amount).</summary>
        public static IReadOnlyDictionary<string, int> GetCurrency() => currency;

        /// <summary>Balance of one currency from the last refresh (0 when unknown).</summary>
        public static int GetBalance(string currencyCode) =>
            currencyCode != null && currency.TryGetValue(currencyCode, out int amount) ? amount : 0;

        /// <summary>"1,250 GC" style text for UI.</summary>
        public static string FormatBalance(string currencyCode) => GetBalance(currencyCode).ToString("N0") + " " + currencyCode;

        // Errors and bans

        private static void ReportError(string what, PlayFabError e)
        {
            if (HandleBan(e)) return;
            PhotonVRLog.Warn($"PlayFab: {what}: {e?.ErrorMessage}");
        }

        // A banned account: leave Photon, show the banned items and texts, then the banned scene if one is set.
        // Returns true when it was a ban.
        private static bool HandleBan(PlayFabError e)
        {
            // IP bans come back with their own message and the same ErrorDetails layout.
            bool ipBan = e?.ErrorMessage != null && e.ErrorMessage.Contains("IP making this request is currently banned");
            if (e == null || (e.Error != PlayFabErrorCode.AccountBanned && !ipBan)) return false;
            if (IsBanned) return true;
            IsBanned = true;

            ReadBan(e, out string reason, out int hours);
            PhotonVRLog.Warn($"PlayFab: this account is banned ({(hours < 0 ? "indefinitely" : hours + " hours")}).");
            if (PhotonNetwork.IsConnected) PhotonNetwork.Disconnect();
            if (Instance != null) Instance.ShowBan(reason, hours);
            Banned?.Invoke(reason, hours);

            string scene = Instance != null ? Instance.bannedscenename : "";
            if (!string.IsNullOrEmpty(scene) && SceneManager.GetActiveScene().name != scene) SceneManager.LoadScene(scene);
            return true;
        }

        private void ShowBan(string reason, int hours)
        {
            foreach (GameObject go in banneditems)
                if (go != null) go.SetActive(true);
            if (banTimeRemainingText != null)
                banTimeRemainingText.text = hours < 0 ? "Time remaining: indefinitely" : $"Time remaining: {hours} hours";
            if (banReasonText != null) banReasonText.text = reason;
        }

        // PlayFab puts bans in ErrorDetails as reason -> [expiry], where expiry is a UTC time or "Indefinite".
        // With several bans the longest one wins. No details, or a ban 10+ years out, counts as indefinitely.
        private static void ReadBan(PlayFabError e, out string reason, out int hours)
        {
            reason = "";
            hours = -1;
            if (e.ErrorDetails == null || e.ErrorDetails.Count == 0) return;

            hours = 0;
            foreach (var ban in e.ErrorDetails)
            {
                int left = HoursUntil(ban.Value != null && ban.Value.Count > 0 ? ban.Value[0] : null);
                bool longer = hours >= 0 && (left < 0 || left > hours);
                if (!longer) continue;
                hours = left;
                reason = ban.Key ?? "";
            }
        }

        private static int HoursUntil(string expiry)
        {
            if (string.IsNullOrEmpty(expiry) ||
                !DateTime.TryParse(expiry, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTime until))
                return -1;
            TimeSpan left = until - DateTime.UtcNow;
            if (left.TotalDays > 3650) return -1;
            return Math.Max(1, (int)Math.Ceiling(left.TotalHours));
        }
    }
}
#endif
