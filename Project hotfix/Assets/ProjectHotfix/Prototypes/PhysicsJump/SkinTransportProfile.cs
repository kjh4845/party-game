using UnityEngine;

namespace ProjectHotfix.PhysicsJump
{
    // Per-model values a skin transport may carry beyond bones and meshes. Older transports
    // leave them unset and every consumer keeps its previous constant.
    public sealed class SkinTransportProfile : MonoBehaviour
    {
        // Right-arm idle direction in the character root frame; the left arm mirrors x.
        public Vector3 IdleArmDirection = new Vector3(.087f, -1f, 0f);
        public bool HasArmColliderRadii;
        public float UpperArmRadius = .041f, ForearmRadius = .037f;
        // Fixed-step contact skin search (SkinSurfaceReview): seeds and escape rings. 1/0 = original walk.
        public int ContactSearchSeeds = 1, ContactEscapeRings;
        // The upper arm skips its own Spine collider (it already skips Chest). Bodies whose arm hangs
        // close to a wide torso otherwise get pushed out of their idle angle.
        public bool UpperArmIgnoresTorso;
        // Slimmed bodies: standing Spine/Chest capsule radius and the limp ragdoll's spine/chest radii.
        public bool HasTorsoColliderRadii;
        public float TorsoRadius = .15f, RagdollSpineRadius = .11f, RagdollChestRadius = .12f;

        public Vector3 IdleDirection(string side) =>
            new Vector3(side == "L" ? -Mathf.Abs(IdleArmDirection.x) : Mathf.Abs(IdleArmDirection.x), IdleArmDirection.y, IdleArmDirection.z);
    }
}
