# Changelog

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
