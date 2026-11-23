# Photon VR Plus

A rebuilt, drop-in replacement for [PhotonVR](https://github.com/fchb1239/PhotonVR) by fchb1239. It is a
Unity package for **Photon PUN 2 VR multiplayer** (Gorilla Tag-style fan games and anything else in VR).

- Full networking through Photon PUN 2. The **App ID**, **Voice App ID**, region and game version are applied when you connect.
- **Photon Voice**: the local player transmits the microphone and remote players play back through a 3D Speaker.
- A networked **player prefab** with cosmetic slots for **Head, Face, Body, LeftHand and RightHand**.
- When you join a room, the **Main Camera drives the head** and your **XR controllers drive the hands**. Everyone sees
  it smoothly through snapshot interpolation.
- Ready-made prefabs: the networked **Player**, a drop-in **PhotonVRManager** (manager + voice + spawner) and a sample cosmetic.
- App IDs stored **encrypted**, with optional in-memory hardening (see [Protecting your App IDs](#protecting-your-app-ids)).
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
**Option A – .unitypackage (simplest):** Assets > Import Package > Custom Package… > `PhotonVRPlus-1.0.1.unitypackage`.
Everything goes into `Assets/PhotonVR`.

**Option B – UPM:** unzip `PhotonVRPlus-1.0.1-UPM.zip`, then Window > Package Manager > **+** > *Add package from disk…* >
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
4. **Tools > PhotonVR > Setup Scene**, or drag `Prefabs/PhotonVRManager.prefab` into the scene. You get:
   - `PhotonVRManager`: Head = Main Camera, Left/Right Hand = your controllers (auto-detected; check them!)
   - `Voice`: PUN voice client + Recorder (your microphone) + `PhotonVRVoice`
   - `PlayerSpawner`: spawns `Resources/PhotonVR/Player` when you join a room
5. **Tools > PhotonVR > Set App IDs...** and paste your **App ID** (PUN) and **Voice App ID** from
   <https://dashboard.photonengine.com>. They are saved encrypted to `Resources/PhotonVRSettings` (or to the scene's
   manager). Set the **Region** (`eu`, `us`, `usw`, `asia`, `jp`, `au`, … or `best`) on the manager or settings asset.
   If the PUN wizard put your IDs into PhotonServerSettings, use **Tools > PhotonVR > Move App IDs Out Of
   PhotonServerSettings** so they are not shipped as plain text.
6. Press Play. The manager connects, joins the `Default` queue and spawns your player. Run a second build to see each other.
7. **Tools > PhotonVR > Validate Setup** lists anything that is still missing.

## Package layout
```
Runtime/
  Core/        PhotonVRManager (fields, lifecycle), PhotonVRSettings, ConnectionState, PhotonVRKeys, PhotonVRProtectedString
  Networking/  PhotonVRManager.Connection / .Rooms / .Security, PhotonVRPoseSync, PlayerSpawner
  Player/      PhotonVRPlayer, PhotonVRPlayerBody, PhotonVRPlayerName
  Cosmetics/   PhotonVRManager.PlayerValues, PhotonVRCosmetic, PhotonVRCosmeticSlots, PhotonVRCosmeticUtility
  Voice/       PhotonVRVoice (voice client + mic), PhotonVRPlayerVoice (per player speaker)
  Saving/      PhotonVRValueSaver
  Utilities/   PhotonVRLog, PhotonVRRigFinder, PhotonVRIdProtector, PhotonVRIdVault
  Testing/     PhotonVRColourChanger, PhotonVRCosmeticsChanger
  Resources/PhotonVR/
    Player.prefab                      networked player (5 cosmetic slots, pose sync, voice)
    Prefabs/PhotonVRManager.prefab     drop-in manager + voice + spawner
    Prefabs/PhotonVRVoice.prefab       voice client on its own
    Prefabs/PhotonVRPlayerSpawner.prefab
    Prefabs/Cosmetics/VRSunglasses.prefab   sample cosmetic
    Prefabs/Testing/*                  colour / cosmetic test triggers
Editor/
  Inspectors/  manager inspector, encrypted-ID drawer, testing inspectors
  Tools/       Setup Scene, Set App IDs window, prefab builders, build check
```

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
- `PhotonVRPoseSync` on the player prefab: `InterpolationBackTime` (0 = auto), `MaxExtrapolation`, `TeleportDistance`.
  The old `PhotonVRPlayer` properties with the same names still work and forward to it. `PhotonVRPlayer.SmoothSync`
  turns the component on or off.

## Reconnects
`AutoReconnect` (on by default) retries after timeouts or network loss, with exponential backoff (`ReconnectDelay`,
`MaxReconnectAttempts`). With `RejoinAfterReconnect`, it rejoins the same queue or private room. Calling `Disconnect()`
yourself never triggers a reconnect.

## Protecting your App IDs
**Short version: anything a game client must use at runtime can be extracted from that client. The real protection
is on Photon's side (authentication). Everything below only raises the bar.**

### What the package does by default
- The App ID and Voice App ID are stored **encrypted** (AES-256 over an XOR mask with a checksum, Base64) in the
  manager and `PhotonVRSettings`. There is no plaintext serialized field, so they do not show up in a scene file,
  an asset bundle or a simple `strings` dump of your build.
- Enter them through the masked inspector field or **Tools > PhotonVR > Set App IDs...**.
- At connect time they are decrypted into a private `AppSettings` copy that goes straight to Photon. They are not
  written into `PhotonServerSettings` and not logged.
- Old scenes and assets with plaintext IDs are migrated to encrypted storage the first time they load in the editor.
- A build warning tells you when `PhotonServerSettings` still contains plaintext IDs. Fix it with
  **Tools > PhotonVR > Move App IDs Out Of PhotonServerSettings**.

The key is derived inside the package code. Anyone with a decompiler can reproduce it. This is **obfuscation**:
it stops casual copying, not a determined attacker.

### Optional memory hardening (`HardenMemory`)
Tick **Harden Memory** on the `PhotonVRManager` or on the `PhotonVRSettings` asset (either one enables it). It is off by
default. When it is on:
- On startup the IDs are moved into an in-memory vault, re-encrypted with a **random key generated for this session**
  and protected with an HMAC. In player builds the at-rest copies are then dropped from memory.
- An ID is decrypted into a byte buffer only when Photon needs it, and the buffer is zeroed right after. No field
  caches the plaintext.
- Photon's own copies (`LoadBalancingClient.AppId` for PUN and for the voice client, and the voice client's
  `Settings.AppIdVoice`) are **blanked whenever the client is idle on a server** (connected to master, in a lobby or in
  a room). They are put back just before Photon switches servers (joining or leaving a room) or reconnects, which is
  the only time Photon reads them. Auto-reconnect and `Connect()` decrypt them again.
- Cheap anti-tamper: the vault refuses to return an ID whose integrity check fails, and the manager warns when an ID
  does not look like a Photon App ID (a GUID).

Notes:
- The PhotonVRManager room methods and direct PUN calls (`JoinRoom`, `LeaveRoom`, `Reconnect`, ...) both keep working,
  because the IDs are restored from Photon's own state changes. While disconnected the PUN App ID stays restored so
  Photon can reconnect; it is blanked again as soon as the client reaches the master server.
- Honest limit: this makes memory dumping **much harder, not impossible**. The plaintext has to exist briefly every time
  Photon connects to a server, and Photon keeps it as a regular managed .NET `string` while it connects. Strings
  cannot be wiped and stay in memory until the garbage collector reuses that memory. A memory dumper or debugger
  attached at the right moment will still find it.

### The real fix: lock the App ID down in the Photon dashboard
A leaked App ID only matters if strangers can use it. Make the server refuse clients that are not yours:
1. Open <https://dashboard.photonengine.com>, pick your PUN app and click **Manage**. Do the same for the Voice app.
2. Under **Authentication**, add a provider. Use **Custom Server** with your own auth URL, or a platform provider
   such as **Meta Quest / Oculus**, **Steam** or **PlayFab**. A platform provider verifies the player owns your app,
   which is the closest Photon gets to a bundle-ID restriction.
3. Untick **"Allow anonymous clients to connect, independently of configured providers"**. The option only appears
   once a provider exists. From now on, clients without valid credentials are rejected, even if they have your App ID.
4. In the game, log in with the provider and pass the token. For custom auth that is
   `PhotonVRManager.ConnectAuthenticated(username, token)`, or set `PhotonNetwork.AuthValues` before connecting.
5. Your auth service can also reject outdated or unknown builds. For example, send `Application.version` as an auth
   parameter and refuse versions you no longer support. `GameVersion` alone only separates matchmaking; it is not a
   security check.
6. Optionally restrict the app to the regions you use, and watch the dashboard's CCU and traffic graphs.

Keep the custom auth endpoint secret on the server side (put shared keys in the dashboard's key/value pairs, not in
the client).

## License
MIT. Copyright (c) 2022 fchb1239 (original PhotonVR), plus Photon VR Plus contributors. See [LICENSE.md](LICENSE.md).
Photon PUN / Photon Voice are © Exit Games and not included.
