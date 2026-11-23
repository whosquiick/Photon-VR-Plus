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
- **New in 1.1.0:** master-client and ownership validation, blocked kicks and renames, a banned-word filter for names and
  room codes, push-to-talk, voice presets and mute lists, grabbable/throwable objects, emotes, simple body IK, nameplates,
  room codes, spectators, a debug overlay, a cosmetic registry with tints, pluggable saving and an optional PlayFab adapter.
- **New in 1.1.1:** names and cosmetics never depend on the master client, your own scripts can set
  `PhotonNetwork.NickName` directly, optional owned-cosmetics check, and optional name sync to the PlayFab display name.
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
**Option A – .unitypackage (simplest):** Assets > Import Package > Custom Package… > `PhotonVRPlus-1.1.1.unitypackage`.
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
  Voice/       PhotonVRVoice (mic, talk modes), PhotonVRPlayerVoice (per player speaker), presets, mute list, XR input
  Saving/      PhotonVRValueSaver, PhotonVRSaveSystem (+ PlayerPrefs provider)
  Security/    PhotonVRSecurity, PhotonVRMasterClient, PhotonVRModeration, PhotonVRProfiles, PhotonVRRpc, PhotonVRAuthority
  Moderation/  PhotonVRWordFilter, PhotonVRBlocklist
  Interaction/ PhotonVRGrabbable, PhotonVRGrabber
  Debugging/   PhotonVRDebugOverlay
  Integrations/PlayFab/  optional PlayFab adapter (own asmdef, compiles only with PHOTONVR_PLAYFAB)
  Samples/     PhotonVRLobbyMenu
  Utilities/   PhotonVRLog, PhotonVRRigFinder, PhotonVRIdProtector, PhotonVRIdVault, PhotonVRRateLimiter
  Testing/     PhotonVRColourChanger, PhotonVRCosmeticsChanger
  Resources/PhotonVR/
    Player.prefab                      networked player (5 cosmetic slots, pose sync, voice)
    Prefabs/PhotonVRManager.prefab     drop-in manager + voice + spawner
    Prefabs/PhotonVRVoice.prefab       voice client on its own
    Prefabs/PhotonVRPlayerSpawner.prefab
    Prefabs/Cosmetics/VRSunglasses.prefab   sample cosmetic
    Prefabs/Testing/*                  colour / cosmetic test triggers
    Filter/DefaultBlocklist.txt, DefaultAllowlist.txt   banned-word data
Samples~/      Lobby and Playground scenes (Package Manager > Samples; Assets/PhotonVR/Samples in the .unitypackage)
Tests/Editor/  edit-mode tests (word filter, room codes, rate limiter, ID protection)
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

## Networking features (1.1.0)

### Master client and ownership
```cs
PhotonVRMasterClient.HandOffToBest();          // legitimate master only; picks the lowest ping, skips spectators
PhotonVRMasterClient.HandOffTo(player);
PhotonVRMasterClient.MasterChanged += (master, legitimate) => { };
PhotonVROwnership.TransferTo(view, player);    // only the owner or the legitimate master
StartCoroutine(PhotonVROwnership.RequestAndWait(view, 2f, ok => { }));
```

### RPC helpers
```cs
PhotonVRRpc.ToAll(photonView, nameof(RPC_Hit), damage);

[PunRPC] void RPC_Hit(int damage, PhotonMessageInfo info)
{
    if (!PhotonVRRpc.FromOwner(photonView, info, "hit", 5f, 10f)) return;   // owner only + rate limit
}
```
`FromMaster` accepts only the *legitimate* master (see Security); `Rate` is a plain per-sender limit.

### Grabbing and throwing
Add `PhotonVRGrabbable` to an object with a Rigidbody and PhotonView (scene object or network-instantiated). Add
`PhotonVRGrabber` to each **rig** hand (not the avatar). The grip grabs the nearest object, ownership is requested and
handed over, the holder simulates it, and releasing throws with the averaged hand velocity. Call `Grab()` / `Release()`
yourself if you use another input system. `AllowSteal` decides whether others can take it out of your hand.

### Hand sync
`PhotonVRPoseSync` now adapts its interpolation delay to measured jitter (`JitterMargin`), smooths remote hands
(`HandSmoothing`), clamps hands to `MaxHandReach`, drops packets from non-owners and limits packets per second.
Send rates can be set per platform on the manager (`PlatformRates`, e.g. lower on Android/Quest).

### Voice
```cs
PhotonVRVoice.SetTalkMode(PhotonVRTalkMode.PushToTalk);   // OpenMic, PushToTalk, VoiceActivation
PhotonVRVoice.SetPushToTalk(true);                       // drive it from your own input; or set PushToTalkButton/Hand
PhotonVRPlayerVoice.ApplyPresetToAll(PhotonVRVoicePreset.Proximity); // Proximity, LargeRoom, Hybrid, Global
PhotonVRMuteList.Toggle(player);                         // local mute, saved by UserId
```
Spectators do not transmit unless `SpectatorsCanTalk` is on.

### Cosmetics without prefab edits
Create **Tools > PhotonVR > Create Cosmetic Registry** (saved as `Resources/PhotonVRCosmeticRegistry`) and add entries
(ID, slot, prefab, offset), or call `PhotonVRCosmeticRegistry.Register(...)` at runtime. When a player equips an ID that
is not already under the slot, it is spawned there. Tints: `PhotonVRManager.SetCosmeticTint("Head", Color.red)`; set
`TintRenderers` on a `PhotonVRCosmetic` to choose which renderers / material slots take the colour.

### Emotes, body IK, nameplates
- `PhotonVREmotes` on the player prefab: a list of emotes (visual, sound, animator trigger). `PhotonVREmotes.PlayLocal("wave")`.
- `PhotonVRBodyIK`: optional two-bone arm IK and torso-follows-head for avatars with arms. Runs from the synced transforms.
- `PhotonVRNameplate`: billboarded name, speaking icon and muted icon. Shows the filtered, verified name.

### Matchmaking
```cs
string code = PhotonVRMatchmaking.CreatePrivateRoom(8);   // "K7MQ2X", filtered, no 0/O/1/I
PhotonVRMatchmaking.JoinByCode("k7mq2x");                  // fails instead of creating
PhotonVRMatchmaking.JoinPublic("Default", 10);
PhotonVRMatchmaking.UseBestRegion();                       // or SwitchRegion("eu")
PhotonVRMatchmaking.SetSpectator(true);                    // no avatar, no mic
PhotonVRMatchmaking.MaxPlayersCap = 16;
```

### Debug overlay
Add `PhotonVRDebugOverlay` to any object (F3 toggles). It shows state, region, ping, send rates, the room, every player
with master / spectator / muted flags and violation count, and how many views you own. Assign `WorldText` for VR.

## Security

PUN has no server-side game logic: the Photon Cloud relays messages, and the client that is master is just another
player's game. Photon VR Plus makes honest clients **refuse and repair** what a modded client tries to do, and reports
it. It cannot stop a modded client from sending things.

Call `PhotonVRSecurity.Initialize()` yourself only if you don't use `PhotonVRManager` (the manager does it in `Awake`).

| Threat | What Photon VR Plus does |
|---|---|
| Making yourself master (`SetMasterClient`) | Every client tracks the *legitimate* master. A switch is accepted only when the old master left, or the legitimate master announced a handoff, or your `PhotonVRAuthority` approves it. Otherwise it is logged as a violation, the previous master takes the role back and kicks the hijacker. `FromMaster` RPC checks and kicks only trust the legitimate master. |
| Kicking / disconnecting others | PUN's `CloseConnection` is disabled (`EnableCloseConnection = false`). Kicks go through `PhotonVRModeration.Kick`, which only the legitimate master can send and every client checks (sender is stamped by the Photon server) and `PhotonVRAuthority.ValidateKick` can veto. Bad attempts are reported. |
| Renaming others / changing their cosmetics | Every player sets their own name (`NickName`) and cosmetics on their own Photon actor; the master client is never involved, so it keeps working when the master leaves. Other players read them from a profile event the **server stamps with the real sender**. Photon also stamps who wrote a property change; if someone else writes to yours, your client puts them back and reports the writer as `PropertyTamper`. |
| Wearing cosmetics you don't own | Optional (`RequireOwnedCosmetics`): unowned cosmetics can't be equipped and are never sent. See [Names and cosmetic ownership](#names-and-cosmetic-ownership). |
| Joining as someone else | Rooms publish UserIds; the master rejects duplicate UserIds, banned UserIds and anything your `PhotonVRAuthority.ValidateIdentity` rejects. With PlayFab custom auth, the UserId is the verified PlayFabId. |
| Moving objects you don't own | Pose sync and grabbables ignore serialized data not sent by the owner; `PhotonVRRpc.FromOwner` does the same for RPCs. Ownership requests are rate limited and PUN only accepts transfers from the current owner. |
| Spam | Per-sender token buckets on profile updates, RPCs, pose packets and grab requests. |
| Slurs in names and room codes | See [Banned words](#banned-words). Remote names are filtered again on display. |

Hooks:
```cs
class MyAuthority : PhotonVRAuthority
{
    public override bool? ValidateMasterClient(Player newMaster, Player previous) => null;   // null = default rules
    public override bool ValidateIdentity(Player player) => MyBackend.IsTicketValid(player.UserId);
    public override void OnViolation(PhotonVRViolation v) => MyBackend.Report(v.Offender.UserId, v.Type, v.Detail);
}
PhotonVRSecurity.Authority = new MyAuthority();
PhotonVRSecurity.KickAfterViolations = 5;                  // master auto-kicks repeat offenders (0 = off)
PhotonVRSecurity.ViolationDetected += v => Debug.LogWarning(v);
PhotonVRSecurity.ReportPlayer(player, "harassment");       // your report button
PhotonVRModeration.Ban(player, "cheating");                // session ban by UserId, enforced by the master
```

**What this cannot do (honest limits):**
- A cheater's own game can do anything it likes locally, and can still write room/player properties and send events.
  Honest clients ignore or undo those, but the messages still reach the server.
- Restoring your properties relies on Photon including the writer's actor number in property-change events, which
  Photon Cloud does. If a change arrives without one (e.g. written by a server plugin), it is left alone.
- The owned-cosmetics check runs on the player's own client. It stops honest players and normal builds from wearing
  things they don't own; a modded client can still send any cosmetic ID, and other clients will show it. Checking other
  players' inventories needs your server (e.g. CloudScript validating purchases).
- When the master leaves, clients accept the next master. A hijacker who calls `SetMasterClient` in that same moment
  and wins the race can be accepted, because nobody legitimate is left to object; your `Authority` can veto it.
- Bans and the word filter run on clients. A modded client can skip its own filter; everyone else still filters its name.
- **Real anti-cheat needs an authority outside the players' games**: a Photon Server plugin (Photon Server / Enterprise
  Cloud), or an authoritative backend such as PlayFab CloudScript / Azure Functions with Photon webhooks, plus custom
  authentication so only verified accounts can connect. `PhotonVRAuthority` is where you plug that in.

## Names and cosmetic ownership

**Names.** `PhotonVRManager.SetUsername(name)` sets your Photon `NickName`, which lives on your own actor. No master is
needed: it works with no master, during a master switch and after the master disconnects. Your own scripts (e.g. a
name computer) can also set `PhotonNetwork.NickName` directly; PhotonVR notices, runs the word filter, saves it and
shows it. Blocked names are put back to the previous name and raise `UsernameRejected`. Every accepted change raises
`PhotonVRManager.UsernameChanged`.

**Owned cosmetics.** Cosmetics are still equipped by ID exactly as before. To stop players wearing items they don't
own, tick **Require Owned Cosmetics** on the manager and list free items in **Owned Cosmetics**. Add the rest at
runtime:
```cs
PhotonVRManager.SetOwnedCosmetics(new[] { "VRTopHat", "VRSunglasses" });  // e.g. from your backend
PhotonVRManager.GrantCosmetic("VRCrown");                                  // after a purchase
PhotonVRManager.CosmeticRejected += (slot, id) => ShowShop(id);
```
Equipping something not owned is refused locally, and anything not owned (e.g. from an old save) is never sent to
other players. With PlayFab, tick **Load Owned Cosmetics** on `PhotonVRPlayFabAuth` instead (see below).
This is client side: see the honest limits under [Security](#security).

**Manager toggles (all off by default):**
| Toggle | What it does |
|---|---|
| `RequireOwnedCosmetics` | Only owned cosmetics can be equipped and sent. |
| `OwnedCosmetics` | IDs everyone owns (free / default items). |
| `ResyncOnMasterSwitch` | When the master changes, check once that your name / colour / cosmetics are still what you set and re-send them if not. Names don't depend on the master anyway; this is a safety net and sends nothing unless something differs. |

## Banned words

`PhotonVRWordFilter` checks player names (`SetUsername` / `TrySetUsername`), room codes (`JoinPrivateRoom`,
`JoinByCode`, generated codes) and names shown above remote players.

- The default lists live in `Runtime/Resources/PhotonVR/Filter/DefaultBlocklist.txt` and `DefaultAllowlist.txt`:
  slurs (racial, homophobic, transphobic, ableist), common swears, sexual terms and extremist terms/numbers.
- Text is normalised first: lower case, accents removed (`fággot`), leetspeak mapped (`b!tch`, `n1gg`, `$h1t`),
  look-alike Cyrillic/Greek letters mapped, repeated letters matched (`fuuuck`), and spaced-out letters joined (`f a g`).
- List syntax: `word` matches anywhere, `=word` matches a whole word only (`=ass` blocks "ass" but not "grass"),
  `raw:1488` matches digits as typed, `#` starts a comment. The allowlist removes innocent words before matching
  (`assassin`, `Scunthorpe`, `raccoon`, `Nigeria`...).
- To customise: **Tools > PhotonVR > Create Blocklist Asset** (`Resources/PhotonVRBlocklist`), then add your own
  TextAssets, extra words or allowed words, or untick `IncludeDefaultLists`. From code:
  `PhotonVRWordFilter.Default = new PhotonVRWordFilter(lines, allowLines);` or
  `PhotonVRWordFilter.Default.CustomCheck = text => MyModerationApi.IsBad(text);`.
- Events: `PhotonVRManager.UsernameRejected`, `PhotonVRManager.RoomCodeRejected`. With
  `PhotonVRModeration.KickBlockedNames` the master kicks players that publish blocked names.

No list catches everything; treat it as a first line and give players a report button.

## Saving

Name, colour, cosmetics and tints are always written to PlayerPrefs (same keys as PhotonVR). Add your own backend with
`IPhotonVRSaveProvider`:
```cs
class MySave : IPhotonVRSaveProvider
{
    public string Name => "MyBackend";
    public void Load(Action<PhotonVRSaveData> done) { /* fetch, then */ done(data); }   // done(null) = nothing saved
    public void Save(PhotonVRSaveData data) { /* upload PhotonVRSaveJson.ToJson(data) */ }
}
PhotonVRSaveSystem.Register(new MySave());   // loads now; later answers win
```
Changes are debounced (`PhotonVRSaveSystem.SaveDelay`, 2 s) so several cosmetic changes become one call. Loaded names
go through the word filter.

## PlayFab (optional)

1. Import the PlayFab Unity SDK and set your Title ID.
2. **Tools > PhotonVR > PlayFab > Enable Integration** (adds `PHOTONVR_PLAYFAB`; the `PhotonVR.PlayFab` assembly only
   compiles with that define, so projects without PlayFab are unaffected). If your SDK has no `PlayFab` assembly
   definition, move the two scripts from `Runtime/Integrations/PlayFab` into your Assets folder instead.
3. In the Photon dashboard, set your app's authentication to **Custom Server** with PlayFab's URL
   (`https://<titleId>.playfabapi.com/photon/authenticate`) and turn off anonymous clients. In PlayFab, add your Photon
   Realtime App ID under Add-ons > Photon.
4. Add `PhotonVRPlayFabAuth` to a scene object and untick `ConnectOnAwake` on the manager. It logs in (device ID or your
   custom ID), gets a Photon token, connects with `UserId = PlayFabId`, and registers `PhotonVRPlayFabSaveProvider`
   (user data key `PhotonVRSave`). If you log in some other way, call `OnLoggedIn(result)` yourself, or untick
   `LoginOnStart` and just use the toggles below.
5. Optional toggles on `PhotonVRPlayFabAuth` (off by default; they only do anything once PlayFab is logged in):
   - `SyncDisplayName`: every accepted name change is also sent to `UpdateUserTitleDisplayName` (3-25 characters;
     PlayFab may refuse names that are taken or that its own filter blocks; that is logged, the Photon name still changes).
   - `LoadOwnedCosmetics` + `CosmeticItemPrefix`: reads `GetUserInventory` and passes the ItemIds to
     `PhotonVRManager.SetOwnedCosmetics` when you log in or connect. Call `RefreshOwnedCosmetics()` after a purchase.

## Samples and tests
- Package Manager > Photon VR Plus > Samples: **Lobby** and **Playground**. In the .unitypackage they are in
  `Assets/PhotonVR/Samples`. Set your App IDs first.
- Edit-mode tests in `Tests/Editor` run in the Test Runner (needs the Test Framework package).

## License
MIT. Copyright (c) 2022 fchb1239 (original PhotonVR), plus Photon VR Plus contributors. See [LICENSE.md](LICENSE.md).
Photon PUN / Photon Voice are © Exit Games and not included.
