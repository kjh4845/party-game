using UnityEngine;

namespace ProjectHotfix.PhysicsJump
{
    // Ragdoll proxy bodies live outside the actor hierarchy.
    public sealed class StrikeReviewHurtbox : MonoBehaviour
    {
        public Transform ActorRoot;
        public static StrikeReviewTarget Resolve(Collider collider)
        {
            var direct = collider.GetComponentInParent<StrikeReviewTarget>();
            if (direct != null) return direct;
            var proxy = collider.GetComponentInParent<StrikeReviewHurtbox>();
            return proxy != null && proxy.ActorRoot != null ? proxy.ActorRoot.GetComponent<StrikeReviewTarget>() : null;
        }
    }
}
