using UnityEngine;

namespace Photon.VR.Testing
{
    /// <summary>Test helper: press "Change" in the inspector (in play mode, in a room) to apply <see cref="Colour"/>.</summary>
    [AddComponentMenu("PhotonVR/Testing/Colour Changer")]
    public class PhotonVRColourChanger : MonoBehaviour
    {
        public Color Colour;

        public void ChangeColour(Color Colour) => PhotonVRManager.SetColour(Colour);

        /// <summary>Applies the colour set in the inspector (handy for UnityEvents / buttons).</summary>
        public void ChangeColour() => PhotonVRManager.SetColour(Colour);
    }
}
