# PhotonVR → Photon VR Plus: API compatibility inventory

Source studied: https://github.com/fchb1239/PhotonVR at commit `67a58885` (2023-08-09), plus the
release package `PhotonVR_09082023.unitypackage` (tag 0.0.5) for prefab layout and script GUIDs.
License: MIT, Copyright (c) 2022 fchb1239 (kept in `LICENSE.md`).

Everything listed here still exists in Photon VR Plus with the same name, namespace, type and
signature. New members were only **added**. A ✅ means that exact member is kept.

## Namespaces
| Namespace | Types |
|---|---|
| `Photon.VR` | `PhotonVRManager`, `ConnectionState`, `PhotonVRManagerGUI` (editor) |
| `Photon.VR.Player` | `PhotonVRPlayer`, `PhotonVRPlayer.CosmeticSlot`, `PhotonVRPlayerName`, `PlayerSpawner` |
| `Photon.VR.Saving` | `PhotonVRValueSaver` |
| `Photon.VR.Testing` | `PhotonVRColourChanger`, `PhotonVRCosmeticsChanger`, `PhotonVRCosmeticsChanger.PhotonVRCosmeticTest`, `PhotonVRColourChangerGUI`, `PhotonVRCosmeticsChangerGUI` (editor) |
| *(global namespace)* | `PhotonVRPlayerBody` (the original has no namespace, and that stays the same) |

## Script GUIDs (kept, so existing scenes and prefabs keep their component references)
| Script | GUID |
|---|---|
| PhotonVRManager.cs (now Runtime/Core) | `c13d37f911dfcb6448ff72058ae197eb` |
| PhotonVRManagerGUI.cs (now Editor/Inspectors) | `f2276a7722c16a4468345ae07bda6dc3` |
| Player/PhotonVRPlayer.cs | `07487eeb44e22e24797eac37cb1fd900` |
| Player/PhotonVRPlayerBody.cs | `9eb52b1fff31a5548a4f8247608f70be` |
| Player/PhotonVRPlayerName.cs | `684d96d703aa09d4fa0bf7c6aacd0fe8` |
| PlayerSpawner.cs (now Runtime/Networking) | `78d3497c3b66e9649ae05b126a352e19` |
| Saving/PhotonVRValueSaver.cs | `3109970f097083047b01b4710a7b79f6` |
| Testing/PhotonVRColourChanger.cs | `24e3aeca6354b664b857f2cee2138c12` |
| Testing/PhotonVRColourChangerGUI.cs (now Editor/Inspectors/Testing) | `81ec5a0df0fa84543a25f1b5607f0667` |
| Testing/PhotonVRCosmeticsChanger.cs | `743007f78c92da84c998c61d724a48a1` |
| Testing/PhotonVRCosmeticsChangerGUI.cs (now Editor/Inspectors/Testing) | `a49b941d8e35ec34e97fea359f48fd34` |

Unity resolves scripts by GUID, not by path, so the 1.0.1 folder moves do not affect existing references.
Art assets (VRHead/VRBody/VRHand/VRTopHat/VRSunglasses .fbx, materials, logos) keep their original GUIDs too.

## `Photon.VR.PhotonVRManager : MonoBehaviourPunCallbacks`
Fields and properties:
- ✅ `public static PhotonVRManager Manager { get; private set; }`
- ✅ `AppId` / `VoiceAppId`: still `public string`, but since 1.0.1 they are **properties** backed by encrypted storage
  (get decrypts, set encrypts). Source compatible for reads and writes; old serialized plaintext values are migrated
  through `[FormerlySerializedAs]`. Code that uses them with `ref`/`out` or reflection on fields would need a change.
- ✅ `public string Region = "eu";`
- ✅ `public Transform Head;` ✅ `public Transform LeftHand;` ✅ `public Transform RightHand;`
- ✅ `public Color Colour;`
- ✅ `public Dictionary<string, string> Cosmetics { get; private set; }`
- ✅ `public string DefaultQueue = "Default";` ✅ `public int DefaultRoomLimit = 16;`
- ✅ `public bool ConnectOnAwake = true;` ✅ `public bool JoinRoomOnConnect = true;`
- ✅ `[NonSerialized] public PhotonVRPlayer LocalPlayer;`
- (private) `RoomOptions options`, `ConnectionState State`

Methods:
- ✅ `public static bool Connect()`
- ✅ `public static bool ConnectAuthenticated(string username, string token)` (Custom auth, params `username`/`token`)
- ✅ `public void Disconnect()` (instance method, unchanged)
- ✅ `public static void ChangeServers(string Id, string VoiceId)`
- ✅ `public static void ChangeServersAuthenticated(string Id, string VoiceId, string username, string token)`
- ✅ `public static void SetUsername(string Name)`
- ✅ `public static void SetColour(Color PlayerColour)`
- ✅ `public static void SetCosmetics(Dictionary<string, string> PlayerCosmetics)`
- ✅ `public static void SetCosmetic(string Type, string CosmeticId)`
- ✅ `public static ConnectionState GetConnectionState()`
- ✅ `public static void SwitchScenes(int SceneIndex, int MaxPlayers)` / `SwitchScenes(int SceneIndex)`
- ✅ `public static void JoinRandomRoom(string Queue, int MaxPlayers)` / `JoinRandomRoom(string Queue)`
- ✅ `public static void JoinPrivateRoom(string RoomId, int MaxPlayers)` / `JoinPrivateRoom(string RoomId)`
- ✅ `public static void _JoinPrivateRoom(string RoomId, int MaxPlayers)` (was public in the original)
- ✅ `public string CreateRoomCode()` (random 0–99998 as a string)
- ✅ `#if UNITY_EDITOR public void CheckDefaultValues()`
- ✅ overrides: `OnConnectedToMaster`, `OnJoinedRoom`, `OnDisconnected`, `OnJoinRandomFailed`

## `Photon.VR.ConnectionState` (enum, same order and values)
✅ `Disconnected = 0, Connecting = 1, Connected = 2, JoiningRoom = 3, InRoom = 4`

## `Photon.VR.Player.PhotonVRPlayer : MonoBehaviourPun`
- ✅ `public Transform Head, Body, LeftHand, RightHand;`
- ✅ `public List<MeshRenderer> ColourObjects;`
- ✅ `public List<CosmeticSlot> CosmeticSlots`
- ✅ `public TextMeshPro NameText;` ✅ `public bool HideLocalPlayer = true;`
- ✅ `public void RefreshPlayerValues()` (sends RPC `"RPCRefreshPlayerValues"` to All)
- ✅ `[PunRPC] private void RPCRefreshPlayerValues()` (RPC name kept for wire compatibility)
- ✅ nested `[Serializable] public class CosmeticSlot { public string SlotName; public Transform Object; }`

## `Photon.VR.Player.PhotonVRPlayerName : MonoBehaviour`
- ✅ `public float Offset = 0.17f;` ✅ `public Transform Head;`

## `PhotonVRPlayerBody : MonoBehaviour` (global namespace)
- ✅ `public Transform Head;` ✅ `public float Offset;`

## `Photon.VR.Player.PlayerSpawner : MonoBehaviourPunCallbacks`
- ✅ `public string PrefabLocation = "PhotonVR/Player";`
- ✅ overrides `OnJoinedRoom`, `OnLeftRoom`

## `Photon.VR.Saving.PhotonVRValueSaver : MonoBehaviour`
- ✅ `public static void SaveDictionary(string location, Dictionary<string, string> value)`
- ✅ `public static Dictionary<string, string> GetDictionary(string location)`
- Storage format kept: `PlayerPrefs[location] = "key1,key2,..."`, `PlayerPrefs[location + key] = value`

## Testing helpers
- ✅ `PhotonVRColourChanger { public Color Colour; public void ChangeColour(Color Colour); }`
- ✅ `PhotonVRCosmeticsChanger { public List<PhotonVRCosmeticTest> Cosmetics; public void ChangeCosmetics(Dictionary<string,string> Cosmetics); }`
- ✅ `PhotonVRCosmeticTest { public string SlotName; public string Cosmetic; }`

## PlayerPrefs keys (unchanged)
| Key | Content |
|---|---|
| `Username` | nickname |
| `Colour` | `JsonUtility.ToJson(Color)` |
| `Cosmetics` | comma-joined slot names (PhotonVRValueSaver format) |
| `Cosmetics<SlotName>` | cosmetic ID per slot, e.g. `CosmeticsHead` |

## Photon custom properties (unchanged on the wire)
| Scope | Key | Type |
|---|---|---|
| Player | `Colour` | `string` (Color as JSON) |
| Player | `Cosmetics` | `Dictionary<string,string>` (slot to cosmetic ID) |
| Room | `queue` | `string` (lobby-visible, used for matchmaking) |
| Room | `version` | `string` (`Application.version`, lobby-visible) |

## Cosmetic slots (names in the shipped prefab, unchanged)
| SlotName | Container object | Parent |
|---|---|---|
| `Head` | `HeadCosmetics` | Head |
| `Face` | `FaceCosmetics` | Head |
| `Body` | `BodyCosmetics` | Body |
| `LeftHand` | `LeftHandCosmetics` | LeftHand |
| `RightHand` | `RightHandCosmetics` | RightHand |

Cosmetic ID = the **name of a child GameObject** under the slot's container. Equipping enables the child
whose name matches and disables its siblings. An empty or unknown ID clears the slot. Default cosmetics in the
prefab: `VRTopHat`, `VRTopHatWhite` (Head), `VRSunglasses`, `GooglyEyes` (Face).

## Behaviour notes from the original (and what Photon VR Plus does)
- The original set `Manager` in `Start`, so other scripts' `Awake` saw null. Photon VR Plus sets it in `Awake`.
- The original called `Application.Quit()` on a duplicate manager. Photon VR Plus destroys the duplicate.
- The original changed `LocalPlayer.CustomProperties` directly on connect. Photon VR Plus uses `SetCustomProperties`.
- The original refreshed by RPC right after `SetCustomProperties`, which could race the property update. Photon VR Plus also
  refreshes in `OnPlayerPropertiesUpdate`. The RPC is still sent for old clients.
- The original synced head and hands with three `PhotonTransformView`s. Photon VR Plus serializes them in `PhotonVRPoseSync`
  (snapshot interpolation; in 1.0.0 this lived in `PhotonVRPlayer`). Legacy prefabs that still have the transform
  views, or still observe `PhotonVRPlayer`, are converted at runtime: the pose sync is added and becomes the only
  observed component.
- The original `CheckDefaultValues` read `AppIdFusion` by mistake. Photon VR Plus reads `AppIdRealtime`.

## 1.1.0 notes
- Nothing renamed or removed. All new types are additive (`Photon.VR`, `Photon.VR.Player`, `Photon.VR.Interaction`,
  `Photon.VR.Debugging`, `Photon.VR.Saving`, `Photon.VR.Samples`, `Photon.VR.PlayFab`).
- Behaviour changes:
  - `SetUsername` silently refuses names blocked by the word filter or longer than 24 characters.
  - Rich-text tags are stripped from names.
  - `JoinPrivateRoom` refuses codes the filter blocks.
  - `PhotonVRVoice.IsMuted` reflects the user's mute toggle, not push-to-talk state.
  - Spectators get no avatar.
- New Photon custom properties:
  - `CosmeticTints` (player), `pvr_spectator`, `pvr_ping` (player), `pvr_private` (room).
  - Event codes 180 (profile), 181 (master handoff), 182 (kick) are reserved by Photon VR Plus.
- New PlayerPrefs keys: `CosmeticTints`, `PhotonVRMuted` (and `PhotonVRSave` if you register `PhotonVRPlayerPrefsSaveProvider`).
- `PhotonNetwork.EnableCloseConnection` is forced off; use `PhotonVRModeration.Kick`.

## 1.1.1 notes
- Nothing renamed or removed; no new Photon keys, events or RPCs.
- Only additions: `UsernameChanged`, `CosmeticRejected`, `OwnsCosmetic`, `SetOwnedCosmetics`, `GrantCosmetic`
  (static, on `PhotonVRManager`); the manager fields `ResyncOnMasterSwitch`, `RequireOwnedCosmetics` and
  `OwnedCosmetics`; and the `PhotonVRPlayFabAuth` members `LoadOwnedCosmetics`, `CosmeticItemPrefix`,
  `RefreshOwnedCosmetics` and `PushDisplayName`.
- Behaviour change: changes to your own name / colour / cosmetics made outside PhotonVR are now kept (as in 1.0.x)
  instead of being reverted (1.1.0).

## 1.2.0 notes
- Nothing renamed or removed; no new Photon keys, events or RPCs.
- Additions:
  - `PhotonVRAppIdKind.PlayFabTitle` (added at the end, existing values unchanged);
  - `PhotonVRPlayFabManager` and `Playfablogin` (new scripts, PlayFab assembly only; `Playfablogin` is in the global
    namespace, like the script it replaces);
  - `PhotonVRPlayFabAuth.UsePhotonCustomAuth` (defaults to true = old behaviour).
- `PhotonVRPlayFabAuth` changes:
  - `OnEnable`, `OnDisable`, `Start` and `OnDestroy` went from private to `protected virtual`.
  - `OnLoggedIn` is now `virtual`.
  - New protected hooks: `ApplyTitleId`, `LoginInfo`, `OnLoginError`, `RetryLastLogin`, `Fail`.
- `LoginSucceeded` is raised right after the PlayFab login, before the Photon connect call.
- If your project still has its own `Playfablogin` class, it takes priority over the package one in your scripts
  (CS0436 warning). Delete yours to switch.

## 1.2.1 notes
- Additions only: the `Photon.VR.PhotonVR` static class, `PhotonVRManager.IsConnected` / `IsMasterClient` /
  `GetPlayersInRoom()`, `PhotonVRPlayFabManager.IsLoggedIn` / `BuyItem` / `RefreshAll`.
- If your project already has its own class called `PhotonVR` in the global namespace, it takes priority in your
  scripts. Use `Photon.VR.PhotonVR` to reach this one.
