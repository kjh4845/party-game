using UnityEngine;

namespace ProjectHotfix.PhysicsJump
{
    [RequireComponent(typeof(Collider))]
    public sealed class ReviewGrabSurface : MonoBehaviour
    {
        public Transform RoofTop;
        public bool ClimbAllowed=true;
        public float TopY=>RoofTop!=null?RoofTop.TransformPoint(Vector3.up*.5f).y:transform.position.y;
        public Collider Shape=>GetComponent<Collider>();
    }
}
