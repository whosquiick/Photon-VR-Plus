# Changelog

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
