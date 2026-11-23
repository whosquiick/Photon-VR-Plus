#if PHOTONVR_PLAYFAB
using Photon.VR.PlayFab;

/// <summary>
/// Same fields and <c>instance</c> as the common gorilla-tag Playfablogin script, so scenes and scripts written for it
/// keep working. It is a <see cref="PhotonVRPlayFabManager"/>; new projects can use that directly.
/// </summary>
public class Playfablogin : PhotonVRPlayFabManager
{
    public static Playfablogin instance;

    [UnityEngine.Tooltip("Name used by UpdateName().")]
    public new string name;

    protected override void Awake()
    {
        instance = this;
        base.Awake();
    }

    public void Login() => LoginWithDeviceId();

    /// <summary>Sets the name from the <c>name</c> field (word-filtered, also sent to the PlayFab display name).</summary>
    public void UpdateName() => UpdateName(name);

    /// <summary>Reloads items and coins.</summary>
    public void GetVirtualCurrencies() => RefreshInventory();
}
#endif
