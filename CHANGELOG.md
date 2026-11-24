# Changelog

## [1.2.4] - 2026-11-24
A pass over Runtime vs Editor, security and setup. Nothing in the public API, serialized fields, RPC names, property keys
or GUIDs changed.

### Fixed
- **Player builds with PlayFab on:** `PhotonVRPlayFabManager` called an internal Runtime method that's only compiled
  outside the editor, so projects with `PHOTONVR_PLAYFAB` worked in Play mode but failed to build a player. The Runtime
  assembly now lets `PhotonVR.PlayFab` see it.
- **Enter Play Mode Options (domain reload off):** statics kept the old manager, voice instance, players, profiles,
  security state, save providers and event listeners between play sessions, so the second Play could act on destroyed
  objects or fire events twice. Every class with static state now resets itself when play starts. Same for the PlayFab
  adapter.
- `FindObjectOfType` / `FindObjectsOfType` are obsolete on Unity 2023.1+ and Unity 6. The package now uses
  `FindFirstObjectByType` / `FindObjectsByType` there, so no more obsolete warnings (the older call is still used on 2020/2021/2022).
- The scripting define helpers no longer use the obsolete `BuildTargetGroup` overloads on 2021.2+.
- A saved name from PlayerPrefs (from an older version or edited by hand) is now checked by the word filter and length
  limit before it's used.
- Small null checks in `OnJoinedRoom`, `OnPlayerLeftRoom` and when PhotonServerSettings has no AppSettings.

### Security
- Name changes: setting the same name again does nothing, and fast bursts are merged and sent at most about once a
  second after a few quick ones. Before, a script setting the name every frame sent a network update every frame.
- `RPCRefreshPlayerValues` is rate limited per sender, since anyone in the room can send it.
- Fake kick and master handoff events are rate limited before they're reported, so spamming them can't flood the log
  or your `Authority`. Kick reasons from the master are clipped to 64 characters.
- PlayFab device login: on platforms without a device ID (`"n/a"`, e.g. WebGL) everyone logged in as the same PlayFab
  account. It now uses a random ID saved on that device instead.
- PlayFab custom auth stops with a clear error when there's no Photon App ID, instead of asking PlayFab for a token
  for an empty app.
- README: more about what client-side checks can't stop (kicks, session bans, rate limits).

### Setup
- New **Tools > PhotonVR > Check Dependencies**. It's in its own small editor assembly with no references, so when
  PUN 2, Photon Voice 2 or TextMeshPro are missing you get one clear message and a dialog with the Asset Store links
  instead of only compile errors. It also offers to turn off `PHOTONVR_PLAYFAB` if the PlayFab SDK was removed.
- **Setup Scene** now creates `Resources/PhotonVRSettings` when it's missing, asks for the App IDs at the end and opens
  the App ID window, then shows what's left to fix in a dialog.
- **Validate Setup** shows a summary dialog and also checks that TMP Essential Resources are imported.
- **Set App IDs** warns when what you pasted doesn't look like a Photon App ID, and trims spaces.
- README: a short numbered quickstart for git URL, UPM zip and .unitypackage installs, with the dependencies listed.

## [1.2.3] - 2026-11-23
Runtime scripts no longer touch UnityEditor, so Unity treats them as runtime-only.

### Fixed
- `PhotonVRSettings`, `PhotonVRManager` and `PhotonVRNetworkObject` were calling Editor APIs
  (`SetDirty`, `delayCall`, and so on) from Runtime scripts, so Unity registered them as both Editor
  and Runtime. Those edit-mode saves and component hooks now live in the Editor assembly. Player
  builds and editor behaviour are unchanged.

## [1.2.2] - 2026-11-23
Network Object, and removed emotes and the edit-mode tests, as requested.

### Added
- `PhotonVRNetworkObject` (Add Component > PhotonVR > Network Object):
  - Adds and links a PhotonView, a PhotonTransformView and, with a Rigidbody, a PhotonRigidbodyView.
  - Sets defaults (Unreliable On Change, Takeover ownership, teleport when far) that you can change on the component.
  - Optional take-ownership-on-touch, plus `TakeOwnership` / `GiveToMaster`.

### Fixed
- The "get_realtimeSinceStartup is not allowed to be called from a MonoBehaviour constructor" error from
  `PhotonVRPoseSync`. The rate limiter no longer reads Unity's clock when it's created; it starts on first use.
- Cosmetic tint shader IDs are looked up on first use instead of in a static initializer, for the same reason.

### Removed
- `PhotonVREmotes` (its `Emote` list, `PlayLocal`, `Play`, the `EmotePlayed` event and its RPC). It wasn't on any
  prefab or sample. If a scene still has it, the component shows as a missing script; just remove it.
- `Tests/` (edit-mode tests and their assembly definition). The Lobby and Playground samples stay.

## [1.2.1] - 2026-11-23
Small additions for building on the package. Nothing changes unless you use them.

### Added
- `PhotonVR` static class: connect, join/leave rooms, name, colour, cosmetics, players, mic/player mute and kick, plus
  the common events (Connected, JoinedRoom, LeftRoom, Disconnected, PlayerJoined, PlayerLeft, MasterChanged,
  NameChanged, CosmeticRejected, LocalPlayerSpawned). It only forwards to the existing calls.
- `PhotonVRManager.IsConnected`, `IsMasterClient`, `GetPlayersInRoom()`.
- `PhotonVRPlayFabManager.IsLoggedIn`, `BuyItem`, `RefreshAll`.
- A credit note at the top of the PhotonVRManager inspector.

### Changed
- Plainer tooltips and comments in a few places. No behaviour changes.

## [1.2.0] - 2026-11-23
PlayFab manager. Drop-in over 1.1.1; nothing new runs unless you add the component.

### Added
- `PhotonVRPlayFabManager` (PlayFab integration, needs `PHOTONVR_PLAYFAB`), laid out like the gorilla-tag
  `Playfablogin` script:
  - PLAYFAB: Title ID stored encrypted with the same protection as the Photon App IDs, optional `HardenTitleId`
    vault, optional login retry with backoff.
  - COSMETICS: `MyPlayFabID`, `CatalogName`, `specialitems` / `disableitems` turned on/off by owned ItemId == object
    name; owned items also feed `RequireOwnedCosmetics` (Load Owned Cosmetics is on by default on the manager).
  - CURRENCY: `CurrencyCode` field (no hardcoded code), `CurrencyName`, `currencyText`, `coins`, optional
    `HideCoinsInMemory`.
  - BANNED: on an account or IP ban, Photon disconnects first. Then `banneditems` turn on, `banTimeRemainingText` shows
    "Time remaining: N hours" (rounded up) or "Time remaining: indefinitely", and `banReasonText` shows the PlayFab ban reason.
    Then the optional `bannedscenename` loads. Also a `Banned(reason, hours)` event and `IsBanned`.
  - TITLE DATA: `MOTDText` from the title data key `MOTD`.
  - PLAYER DATA: `UserName` text, `StartingUsername` for new players, `UpdateName(string)` through the word filter
    to the Photon name and the PlayFab display name.
  - Economy: one `GetUserInventory` call for items and currency (`RefreshInventory`), `OwnsItem`, `GetCurrency`,
    `GetBalance`, `FormatBalance`, `GetCatalogItem`, `PurchaseItem`, `PurchaseCosmetic`, `ConsumeItem`,
    `InventoryChanged`. No client currency grants; grant from the server.
  - Everything is event-driven, with no Update loop. Null text fields are skipped.
- `Playfablogin`: a thin subclass with the same fields plus `instance`, `name`, `UpdateName()`, `Login()` and
  `GetVirtualCurrencies()`, for scenes and scripts built on that script.
- `PhotonVRPlayFabAuth.UsePhotonCustomAuth`: on by default, so existing setups are unchanged; off on the new manager.
- `PhotonVRAppIdKind.PlayFabTitle`.

### Changed
- `PhotonVRPlayFabAuth`: lifecycle methods and `OnLoggedIn` are now virtual, and it has small hooks for subclasses:
  `ApplyTitleId`, `LoginInfo`, `OnLoginError`, `RetryLastLogin`.
- `LoginSucceeded` fires right after the PlayFab login instead of after the Photon connect call.
- The PlayFab assembly now references `Unity.TextMeshPro`.

## [1.1.1] - 2026-11-23
Names, cosmetic ownership and PlayFab options. Drop-in over 1.1.0; new options are off by default.

### Fixed
- Changing a name no longer depends on anything but your own client: the name is your own actor's `NickName`, so it
  works through master switches and after the master disconnects.
- Names (and colour / cosmetics) set by your own scripts through `PhotonNetwork.NickName` or `SetCustomProperties` were
  treated as tampering and reverted. They are now kept, after the word filter.
- Publishing your profile removed every player's cached profile from the room, so late joiners could see old names.
  Only your own cached profile is replaced now.
- Restoring tampered properties now uses the writer Photon stamps on the change, so it only reacts to changes really
  made by someone else, and reports that player.

### Added
- `PhotonVRManager.UsernameChanged` event.
- Optional owned-cosmetics check: `RequireOwnedCosmetics` and `OwnedCosmetics` on the manager; `SetOwnedCosmetics`,
  `GrantCosmetic`, `OwnsCosmetic` and `CosmeticRejected`. Unowned cosmetics can't be equipped and are never sent.
- `ResyncOnMasterSwitch` (manager): one-time check of your own properties when the master changes.
- PlayFab (`PhotonVRPlayFabAuth`): `SyncDisplayName` now follows every name change (`UpdateUserTitleDisplayName`),
  not just saves. Also `LoadOwnedCosmetics` / `CosmeticItemPrefix` / `RefreshOwnedCosmetics()` and `PushDisplayName(name)`.
- `PhotonVRCosmeticUtility.SameDictionary`.

## [1.1.0] - 2026-11-23
Networking, security and moderation release. Still a drop-in replacement: no class, namespace, public member, GUID,
PlayerPrefs key or Photon property key was renamed or removed.

### Added
- **Security layer** (`PhotonVRSecurity`, set up by the manager):
  - master-client validation with hijack detection, reclaim and kick (`PhotonVRMasterClient`), plus validated handoff
    (`HandOffTo`, `HandOffToBest`);
  - validated kicks and session bans (`PhotonVRModeration`), with PUN `CloseConnection` disabled;
  - profiles (name, colour, cosmetics, tints) sent as server-stamped events, so players can only change their own,
    and tampered properties are restored;
  - identity checks: duplicate or banned UserIds, `PhotonVRAuthority.ValidateIdentity`;
  - owner checks and rate limits on pose sync, grabbables and RPCs (`PhotonVRRpc`);
  - violation reporting with `ViolationDetected`, `ReportPlayer`, `KickAfterViolations`, and an authority hook
    (`PhotonVRAuthority`) for a backend.
- **Banned-word filter** (`PhotonVRWordFilter`, `PhotonVRBlocklist` asset, default lists in
  `Resources/PhotonVR/Filter`):
  - normalises accents, leetspeak, look-alike letters, repeated letters and spaced-out letters, and has an allowlist;
  - applied to names, room codes and displayed names;
  - new events `UsernameRejected` and `RoomCodeRejected`.
- **Matchmaking**: `PhotonVRMatchmaking` (readable room codes, `JoinByCode`, `JoinPublic`, region switching / best
  region, player caps, spectators), and `PhotonVRManager.JoinRoomByCode`.
- **Ownership** helpers (`PhotonVROwnership`).
- **Grabbing**: networked grabbable/throwable objects (`PhotonVRGrabbable`, `PhotonVRGrabber`).
- **Voice**:
  - talk modes (open mic, push-to-talk with XR button / key / API, voice activation);
  - spatial presets (`PhotonVRVoicePreset`);
  - a per-player mute list (`PhotonVRMuteList`);
  - spectators muted by default.
- **Cosmetic registry** (`PhotonVRCosmeticRegistry`): cosmetics spawned on demand without editing the player prefab.
  Also cosmetic tints (`SetCosmeticTint`, `PhotonVRCosmetic.TintRenderers`).
- **Player extras**: `PhotonVREmotes`, `PhotonVRBodyIK`, `PhotonVRNameplate` (name, speaking and muted icons), and
  `PhotonVRPlayer.DisplayName`.
- **Debugging**: `PhotonVRDebugOverlay` (ping, region, rates, room, players and flags, owned views).
- **Saving**: `PhotonVRSaveSystem` with pluggable `IPhotonVRSaveProvider`, a PlayerPrefs provider and debounced saves.
- **PlayFab**: optional adapter (`PhotonVRPlayFabAuth`, `PhotonVRPlayFabSaveProvider`) in its own assembly that only
  compiles with `PHOTONVR_PLAYFAB`. Tools > PhotonVR > PlayFab > Enable Integration.
- **Samples and tests**: Lobby and Playground samples (`Samples~`), plus edit-mode tests (word filter, room codes, rate
  limiter, ID protection).
- **Editor**: Tools > PhotonVR > Create Blocklist Asset / Create Cosmetic Registry.
- `PhotonVRManager`:
  - `PlatformRates` sets send and serialization rates per platform;
  - `PublishUserIds`;
  - `ConnectWithAuthentication(AuthenticationValues)`.

### Changed
- `PhotonVRPoseSync`:
  - the interpolation delay adapts to jitter;
  - remote hands are smoothed and clamped to `MaxHandReach`;
  - packets from non-owners, bad values and floods are dropped;
  - the buffer is now 16.
- `SetUsername` now ignores blocked or over-long names (use `TrySetUsername` to get the result). Rich-text tags are
  stripped from names.
- `CreateRoomCode` skips codes the filter blocks.
- `PhotonVRVoice.IsMuted` reports your mute toggle; push-to-talk no longer reads as muted.
- The player spawner does not spawn an avatar for spectators.

### Security notes
Everything above runs on the players' clients. It protects honest players from what a modded client sends, but cannot
stop a modded client from sending it. Real enforcement needs a Photon Server plugin or an authoritative backend; see
README > Security.

## [1.0.1] - 2026-11-23
Clean-up and hardening release. Still a drop-in replacement: class names, namespaces, public members,
cosmetic slot names, Photon property keys and all 11 original script GUIDs are unchanged.

### Changed
- Source reorganised into `Runtime/Core`, `Networking`, `Player`, `Cosmetics`, `Voice`, `Saving`, `Utilities` and
  `Editor/Inspectors`, `Editor/Tools`. Files were moved together with their `.meta`, so every GUID is kept.
- `PhotonVRManager` is split into partial files (connection, rooms, player values, security).
- Head/hand sync moved out of `PhotonVRPlayer` into a dedicated `PhotonVRPoseSync` component (snapshot interpolation,
  limited extrapolation, teleport snapping). `PhotonVRPlayer.InterpolationBackTime`, `MaxExtrapolation`,
  `TeleportDistance` and `SpeakingIndicator` still exist and forward to the new components.
- Per-player voice moved into `PhotonVRPlayerVoice` (PhotonVoiceView + 3D Speaker on the head, speaking indicator,
  local mute / volume).
- `AppId` / `VoiceAppId` on the manager and settings asset are now properties backed by encrypted storage (see below).
  Existing scenes and assets are migrated automatically the first time they load.
- The IDs are no longer written into the shared `PhotonServerSettings` asset at runtime. They are decrypted into a
  private `AppSettings` copy at connect time, and Photon Voice gets its own copy.
- Logging goes through `PhotonVRLog`; comments and file headers cleaned up.

### Added
- Real prefabs:
  - `Resources/PhotonVR/Player.prefab` (same path and GUID) now includes `PhotonVRPoseSync` and `PhotonVRPlayerVoice`;
    the PhotonView observes only the pose sync.
  - `Prefabs/PhotonVRManager.prefab` (same GUID) is a complete drop-in: manager + `Voice` child (PunVoiceClient,
    Recorder, PhotonVRVoice) + `PlayerSpawner` child.
  - `Prefabs/Cosmetics/VRSunglasses.prefab`, a sample cosmetic using the new `PhotonVRCosmetic` component.
- `PhotonVRCosmetic` (optional id / slot / display name / equip events) and `PhotonVRCosmeticUtility`.
- `PhotonVRRigFinder` (better head / hand auto-detection) and `PhotonVRKeys.DefaultPlayerPrefab` / `RefreshRpc`.
- **Encrypted App IDs**: AES-256 + XOR + checksum, Base64 at rest (`PhotonVRProtectedString`). Edited through a
  masked inspector field or **Tools > PhotonVR > Set App IDs...**; never stored in a plaintext serialized field.
- **Tools > PhotonVR > Move App IDs Out Of PhotonServerSettings**, plus a build-time warning when
  PhotonServerSettings still contains plaintext IDs.
- **Optional memory hardening** (`HardenMemory`, off by default, on the manager or the settings asset): IDs are kept in
  a vault encrypted with a random per-session key and integrity-checked, decrypted into a zeroed byte buffer only when
  Photon needs them, and blanked from Photon's clients whenever they are idle on a server. They are restored right
  before server switches and reconnects. A light format check warns when an ID does not look like a Photon App ID.
- **Tools > PhotonVR > Prefabs > Create Player Prefab / Create Manager Prefab / Copy Default Player Prefab To Assets**.

### Fixed
- Child objects of the manager prefab (voice, spawner) no longer call `DontDestroyOnLoad` on themselves; the root is
  kept alive instead, and the prefab's PunVoiceClient has `ApplyDontDestroyOnLoad` off.
- A Speaker created at runtime is parented to the player's head instead of the root.
- `PhotonVRSettings` migration compiles on Unity versions without `AssetDatabase.SaveAssetIfDirty`.

Obfuscation is defense in depth, not real protection. See README > Protecting your App IDs.

## [1.0.0] - 2026-11-23 — Photon VR Plus
First release of the revamp, based on PhotonVR 0.0.5 (fchb1239, 2023-08-09).

### Kept (backwards compatible)
- Same namespaces (`Photon.VR`, `Photon.VR.Player`, `Photon.VR.Saving`, `Photon.VR.Testing`, global `PhotonVRPlayerBody`).
- Same class names, public fields, properties and method signatures. See `API_COMPAT.md`.
- Same script GUIDs, so scenes and prefabs made with the original keep their component references.
- Same cosmetic slot names (`Head`, `Face`, `Body`, `LeftHand`, `RightHand`), the same Photon custom property
  keys (`Colour`, `Cosmetics`, `queue`, `version`), the same PlayerPrefs keys and the same RPC name (`RPCRefreshPlayerValues`).

### Added
- `PhotonVRSettings` asset (App ID, Voice App ID, region, game version, send/serialization rates), applied at connect.
- `PhotonVRVoice`: sets up the PUN voice client and Recorder. Works with both `PunVoiceClient` (Voice 2.5x+) and
  `PhotonVoiceNetwork` (older). Adds mute/unmute (`SetMuted`, `ToggleMute`, `IsMuted`).
- Smooth head/hand sync: `PhotonVRPlayer` is now observed and uses snapshot interpolation with limited extrapolation
  and teleport snapping. It replaces the three `PhotonTransformView`s. Legacy prefabs are converted at runtime.
- Auto-reconnect with exponential backoff, which rejoins the same queue or private room afterwards.
- Room helpers: `LeaveRoom()`, `CurrentRoomName`, `ClearCosmetic`, `ClearAllCosmetics`, `GetCosmetic`. Joining while
  already in a room (or while disconnected) is queued and run at the right moment.
- Events: `StateChanged`, `ConnectedToMaster`, `JoinedRoom`, `LeftRoom`, `DisconnectedFromServer`, `CosmeticsChanged`,
  `ColourChanged`, `LocalPlayerSpawned`, `PhotonVRPlayer.ValuesRefreshed`, plus UnityEvents on the manager.
- `PhotonVRPlayer.All`, `IsLocal`, `Owner`, `IsSpeaking`, `SpeakingIndicator`, `GetSlot`, `GetEquipped`,
  `CosmeticSlot.Equip`, `CosmeticSlot.GetAvailableCosmetics`.
- `PhotonVRCosmeticSlots` / `PhotonVRKeys` constants.
- Editor: **Tools > PhotonVR > Setup Scene / Create Player Prefab / Copy Default Player Prefab To Assets /
  Create Settings Asset / Validate Setup**, plus a manager inspector with validation and runtime controls.
- Assembly definitions (`PhotonVR`, `PhotonVR.Editor`).

### Fixed
- `Manager` is now set in `Awake` instead of `Start`, so other scripts can use it in `Awake`/`OnEnable`.
- A duplicate manager is destroyed instead of calling `Application.Quit()`.
- Colour and cosmetics apply through `OnPlayerPropertiesUpdate`, which fixes the race where the refresh RPC arrived before the new properties.
- Properties on connect use `SetCustomProperties` instead of changing the hashtable directly.
- `ChangeServers` waits for the disconnect to finish before reconnecting (the original reconnect could fail).
- `SwitchScenes` / `JoinRandomRoom` while in a room now leave first and then join.
- Room-code collisions on create are retried.
- `PlayerSpawner.OnLeftRoom` no longer calls `PhotonNetwork.Destroy` after leaving (that call logged errors).
- `PhotonVRPlayerBody` turns by yaw only, using the forward vector, and no longer skews when you look up or down.
- `CheckDefaultValues` read `AppIdFusion` by mistake. It now reads `AppIdRealtime`.
- Null checks throughout (missing rig, missing manager, missing properties, room objects).
