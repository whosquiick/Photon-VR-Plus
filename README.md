# Photon VR Plus

A drop-in replacement for [PhotonVR](https://github.com/fchb1239/PhotonVR) (fchb1239) for Photon PUN 2 VR games.
Same class names, fields, cosmetic keys and script GUIDs, so old scenes and scripts keep working.

You get networking with smooth head/hand sync, Photon Voice (push-to-talk, mute lists), a player prefab with cosmetic
slots, grabbable objects, nameplates, room codes, spectators, master-client checks, a name filter, saving, an optional
PlayFab manager and encrypted App IDs.

## Requirements
- Unity 2020.3 or newer (Unity 6 works)
- [Photon Voice 2](https://assetstore.unity.com/packages/tools/audio/photon-voice-2-130518) (includes PUN 2)
- TextMeshPro + TMP Essential Resources
- An XR rig with the camera tagged **MainCamera**
- Optional: [PlayFab SDK](https://github.com/PlayFab/UnitySDK)

## Install
Pick **one** of these. Don't mix them, or Unity sees the same files twice and imports nothing.
- **Git URL:** Package Manager > **+** > *Add package from git URL* > `https://github.com/whosquiick/Photon-VR-Plus.git#v1.3.0`
- **UPM zip:** unzip `PhotonVRPlus-1.3.0-UPM.zip`, then *Add package from disk* > `com.photonvrplus.core/package.json`
- **.unitypackage:** Assets > Import Package > Custom Package > `PhotonVRPlus-1.3.0.unitypackage` (goes into `Assets/PhotonVR`)

**Updating:** import the new .unitypackage over the old one (changed files show up ticked), or bump the version in
Package Manager. Your scenes and settings are kept.

## Setup
1. **Tools > PhotonVR > Setup Scene** adds the manager, voice and spawner and asks for your IDs.
2. Paste your **App ID** and **Voice App ID** from the [Photon dashboard](https://dashboard.photonengine.com).
3. Press Play. Run a second build to see each other.

**Tools > PhotonVR > Validate Setup** lists anything missing. **Check Dependencies** tells you what to install.

## Main components
| Component | What it does |
|---|---|
| `PhotonVRManager` | Connects, joins rooms, holds the local rig, name and cosmetics |
| `PhotonVRVoice` | Microphone, push-to-talk, voice presets |
| `PlayerSpawner` + `PhotonVRPlayer` | Spawns and syncs the player (`Resources/PhotonVR/Player`) |
| `PhotonVRNetworkObject` | Networks any object in one click |
| `PhotonVRGrabbable` / `PhotonVRGrabber` | Grab and throw |
| `PhotonVRPlayFabManager` | Optional PlayFab login, currency, items, bans |

From code, the `PhotonVR` static class has the common calls: `PhotonVR.Connect()`, `PhotonVR.JoinRoom("Default")`,
`PhotonVR.JoinPrivateRoom("code")`, `PhotonVR.SetName("Monke")`, `PhotonVR.EquipCosmetic("Head", "Crown")`, plus events
like `PhotonVR.JoinedRoom`.

## App IDs
IDs are stored encrypted with a random key made for your project (`Assets/PhotonVR-ProjectKey`). Commit that folder
so teammates can read the IDs, and don't share it. IDs are only decrypted when connecting and never shown in full in
the inspector.

Anything a game uses can be pulled out of it by someone determined. The real protection is
**Photon dashboard > Authentication**: add a provider (Meta, Steam, PlayFab or your own server) and turn off anonymous
clients. Never put a PlayFab secret key in the game.

## More
- [Full guide](Documentation~/Guide.md): every feature, PlayFab, security details
- [CHANGELOG.md](CHANGELOG.md) and [API_COMPAT.md](API_COMPAT.md)

## Credits
Made by Quick ([discord.gg/uglymonke](https://discord.gg/uglymonke)). Original PhotonVR by fchb1239.
MIT licensed, see [LICENSE.md](LICENSE.md). Photon PUN and Voice are by Exit Games and not included.
