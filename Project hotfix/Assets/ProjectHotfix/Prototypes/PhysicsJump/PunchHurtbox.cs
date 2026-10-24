using UnityEngine;
using ProjectHotfix.Simulation;

namespace ProjectHotfix.PhysicsJump
{
    // Dedicated query volumes; locomotion and grabbing keep their physical colliders.
    public sealed class PunchHurtbox : MonoBehaviour
    {
        public PunchHitRegion Region;
        public StrikeReviewTarget Target;
        public bool StandingOnly;
        public bool AcceptsHit => Target != null && (!StandingOnly || !(Target.GetComponent<RagdollRecoveryReview>()?.Active ?? false));
        public static void AddStanding(StrikeReviewTarget target)
        {
            var rig=target.GetComponent<PunchHitRig>() ?? target.gameObject.AddComponent<PunchHitRig>();rig.Bind(target);
        }
    }
}
