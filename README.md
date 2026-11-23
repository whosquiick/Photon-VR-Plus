# Photon VR Plus

A rebuilt, drop-in replacement for [PhotonVR](https://github.com/fchb1239/PhotonVR) by fchb1239. It is a
Unity package for **Photon PUN 2 VR multiplayer** (Gorilla Tag-style fan games and anything else in VR).

- Full networking through Photon PUN 2. The **App ID**, **Voice App ID**, region and game version are applied when you connect.
- **Photon Voice**: the local player transmits the microphone and remote players play back through a 3D Speaker.
- A networked **player prefab** with cosmetic slots for **Head, Face, Body, LeftHand and RightHand**.
- When you join a room, the **Main Camera drives the head** and your **XR controllers drive the hands**. Everyone sees
  it smoothly through snapshot interpolation.
- Auto-reconnect, room helpers, events, null-safe code and an editor menu that sets up everything for you.
- **Backwards compatible**: same class names, namespace (`Photon.VR`), public fields, methods, cosmetic names,
  Photon property keys and even script GUIDs. Existing scripts that use PhotonVR compile and work unchanged.
  See [`API_COMPAT.md`](API_COMPAT.md).

## Requirements
| Dependency | Where |
|---|---|
| Unity 2020.3 LTS or newer (tested API against 2021.x reference assemblies) | |
| **PUN 2** (free or plus) | [Asset Store](https://assetstore.unity.com/packages/tools/network/pun-2-free-119922) |
| **Photon Voice 2** | [Asset Store](https://assetstore.unity.com/packages/tools/audio/photon-voice-2-130518) |
| **TextMeshPro** + *TMP Essential Resources* | Built in: Window > TextMeshPro > Import TMP Essential Resources |
| An XR rig (XR Interaction Toolkit XR Origin, OpenXR, Oculus/Meta rig, or your own) | Package Manager |

Photon Voice 2 already contains PUN 2. If you import only Photon Voice 2, you get both.

## Install
**Option A – .unitypackage (simplest):** Assets > Import Package > Custom Package… > `PhotonVRPlus.unitypackage`.
Everything goes into `Assets/PhotonVR`.

**Option B – UPM:** unzip `PhotonVRPlus-UPM.zip`, then Window > Package Manager > **+** > *Add package from disk…* >
select `com.photonvrplus.core/package.json`. (You can also push the folder to a Git repo and use *Add package from git URL*.)

Use one option, not both.

### Upgrading from the original PhotonVR
1. If you customised `Assets/Resources/PhotonVR/Player.prefab` (your cosmetics), **move** it somewhere outside that
   folder first, for example `Assets/MyGame/Resources/PhotonVR/Player.prefab`. Moving keeps its GUID.
2. Delete the old `Assets/Resources/PhotonVR` folder.
3. Import Photon VR Plus. Your scenes and prefabs reconnect to the new scripts automatically (same script GUIDs).
4. If you kept your own `Player.prefab` under `Resources/PhotonVR/`, delete the one shipped in Photon VR Plus
   (`Assets/PhotonVR/Runtime/Resources/PhotonVR/Player.prefab`) so that only one `PhotonVR/Player` exists. Your old
   prefab works as is: its PhotonTransformViews are turned off at runtime and the smooth sync takes over.

## Setup (5 minutes)
1. Import PUN 2 + Photon Voice 2 and TMP Essentials. Enter anything in the PUN wizard, or skip it.
2. Import Photon VR Plus (see above).
3. Open your scene with your XR rig (camera tagged **MainCamera**).
4. **Tools > PhotonVR > Setup Scene**. This creates:
   - `PhotonVRManager`: Head = Main Camera, Left/Right Hand = your controllers (auto-detected by name; check them!)
   - `PhotonVRPlayerSpawner`: spawns `Resources/PhotonVR/Player` when you join a room
   - `PhotonVRVoice`: PUN voice client + Recorder (your microphone)
5. Select `PhotonVRManager` and paste your **App ID** (PUN) and **Voice App ID** from
   <https://dashboard.photonengine.com>, and set the **Region** (`eu`, `us`, `usw`, `asia`, `jp`, `au`, … or `best`).
   *Or* **Tools > PhotonVR > Create Settings Asset** and put them there, which overrides the manager fields.
6. Press Play. The manager connects, joins the `Default` queue and spawns your player. Run a second build to see each other.
7. **Tools > PhotonVR > Validate Setup** lists anything that is still missing.

## Using it from code (same API as PhotonVR)
```cs
using Photon.VR;

PhotonVRManager.Connect();
PhotonVRManager.ChangeServers("AppId", "VoiceAppId");
PhotonVRManager.ConnectAuthenticated(username, token);          // custom auth (PlayFab etc.)
PhotonVRManager.JoinRandomRoom("Space", 8);                     // queue-based matchmaking
PhotonVRManager.JoinPrivateRoom("1234", 8);                     // room code
PhotonVRManager.SwitchScenes(1, 8);
PhotonVRManager.SetUsername("fchb1239");
PhotonVRManager.SetColour(new Color(0, 0, 1));

// Cosmetics: slot name -> cosmetic ID (the name of a child object under that slot)
PhotonVRManager.SetCosmetics(new Dictionary<string, string> {
    { "Head", "VRTopHat" },
    { "Face", "VRSunglasses" }
});
PhotonVRManager.SetCosmetic("Head", "VRTopHat");
PhotonVRManager.SetCosmetic("Head", "");                         // clear the head slot
```

### New in Photon VR Plus
```cs
PhotonVRManager.LeaveRoom();
PhotonVRManager.ClearCosmetic(PhotonVRCosmeticSlots.Face);
PhotonVRManager.ClearAllCosmetics();
string hat = PhotonVRManager.GetCosmetic("Head");

PhotonVRManager.StateChanged       += state => Debug.Log(state);
PhotonVRManager.JoinedRoom         += room  => Debug.Log(room.Name);
PhotonVRManager.LocalPlayerSpawned += p     => Debug.Log("spawned " + p.name);
PhotonVRManager.CosmeticsChanged   += c     => SaveToMyBackend(c);

PhotonVRVoice.SetMuted(true);   // or PhotonVRVoice.ToggleMute()

foreach (var player in Photon.VR.Player.PhotonVRPlayer.All)
    Debug.Log($"{player.Owner.NickName} speaking: {player.IsSpeaking}, hat: {player.GetEquipped("Head")}");
```

## Cosmetics
Every body part on the player prefab has a `…Cosmetics` child. That child is the slot container:

| Slot name | Container | Lives under |
|---|---|---|
| `Head` | `HeadCosmetics` | Head |
| `Face` | `FaceCosmetics` | Head |
| `Body` | `BodyCosmetics` | Body |
| `LeftHand` | `LeftHandCosmetics` | LeftHand |
| `RightHand` | `RightHandCosmetics` | RightHand |

Put your cosmetic models under a container and **name the object after the cosmetic ID**. For example, a hat with
ID `VRTopHat` goes at `Head/HeadCosmetics/VRTopHat`. Equipping an ID enables that child and disables its siblings.
An empty or unknown ID clears the slot. Your own slots work too: add an entry to `PhotonVRPlayer.CosmeticSlots`.

Cosmetics and colour are stored in Photon player custom properties (`Cosmetics`, `Colour`), so late joiners see them
right away. They are also saved to PlayerPrefs and restored on the next launch.

If you use the UPM install, the shipped prefab is read-only. Use **Tools > PhotonVR > Copy Default Player Prefab To
Assets** (or **Create Player Prefab**). This writes `Assets/PhotonVR/Resources/PhotonVR/PlayerCustom.prefab` and points
the spawner at it.

## Sync settings
- `PhotonVRManager.SendRate` / `SerializationRate` (defaults 30 / 15). A higher serialization rate is smoother but uses more Photon messages.
- `PhotonVRPlayer.SmoothSync`, `InterpolationBackTime` (0 = auto), `MaxExtrapolation`, `TeleportDistance`.

## Reconnects
`AutoReconnect` (on by default) retries after timeouts or network loss, with exponential backoff (`ReconnectDelay`,
`MaxReconnectAttempts`). With `RejoinAfterReconnect`, it rejoins the same queue or private room. Calling `Disconnect()`
yourself never triggers a reconnect.

## License
MIT. Copyright (c) 2022 fchb1239 (original PhotonVR), plus Photon VR Plus contributors. See [LICENSE.md](LICENSE.md).
Photon PUN / Photon Voice are © Exit Games and not included.
