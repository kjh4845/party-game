using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ProjectHotfix.PhysicsJump
{
    // A permanently limp, explicit test fixture. No damage, timed recovery or throw.
    public sealed class LimpGrabTargetReview : MonoBehaviour
    {
        public enum Orientation { FaceUp,FaceDown,LeftSide,RightSide }
        public PhysicsJumpReview Actor {get;private set;}
        public RagdollRecoveryReview Ragdoll {get;private set;}
        public Rigidbody[] Bodies {get;private set;}
        public bool Available=>isActiveAndEnabled&&Ragdoll!=null&&Ragdoll.Active&&Bodies!=null&&Bodies.All(b=>b!=null&&!b.isKinematic&&b.gameObject.activeInHierarchy);
        private Collider[] _colliders;
        private ShoulderHelperRig _helpers;
        private Vector3[] _spawnPositions;
        private Quaternion[] _spawnRotations;
        private readonly HashSet<UnityEngine.Object> _owned=new HashSet<UnityEngine.Object>();
        private void OnDestroy(){foreach(var item in _owned)if(item!=null)Destroy(item);}
        public bool Owns(Rigidbody body)=>Bodies!=null&&System.Array.IndexOf(Bodies,body)>=0;
        public Vector3 Center=>Bodies.Aggregate(Vector3.zero,(v,b)=>v+b.worldCenterOfMass*b.mass)/Bodies.Sum(b=>b.mass);
        public static LimpGrabTargetReview Create(Scene scene,string skinPath,Orientation orientation=Orientation.FaceUp)
        {
            var actor=PhysicsJumpReview.Create(new Vector3(0,.5f,.45f),false,skinPath);SceneManager.MoveGameObjectToScene(actor.gameObject,scene);
            actor.ReadKeyboard=false;actor.enabled=false;actor.Motor.ManualStep=true;actor.Motor.AuthorityEnabled=true;actor.Motor.enabled=false;
            var fixture=actor.gameObject.AddComponent<LimpGrabTargetReview>();fixture.Actor=actor;
            var bones=actor.Pose.GetComponentInChildren<SkinnedMeshRenderer>().bones;
            fixture._helpers=actor.Pose.GetComponentInChildren<ShoulderHelperRig>();
            var rag=actor.gameObject.AddComponent<RagdollRecoveryReview>();rag.ArticulatedTorso=true;rag.RelaxedRagdoll=true;rag.UseGetUpAnimation=false;rag.Bind(actor);rag.enabled=false;fixture.Ragdoll=rag;
            if(!rag.BeginDown(Vector3.zero,Vector3.zero))throw new System.InvalidOperationException("Unable to initialize limp target");actor.Motor.AuthorityEnabled=false;rag.Down.enabled=false;
            var bodies=new Rigidbody[32];var count=rag.CopyDynamicBodies(bodies);fixture.Bodies=bodies.Take(count).ToArray();
            fixture._colliders=fixture.Bodies.SelectMany(b=>b.GetComponentsInChildren<Collider>()).Where(c=>c.enabled&&!c.isTrigger&&c.attachedRigidbody!=null).Distinct().ToArray();
            // The previous down-only proxy was narrower than the visible trunk.
            // Fit this contact fixture to the same torso envelope as the standing lab.
            foreach(var capsule in fixture._colliders.OfType<CapsuleCollider>())
            {
                var b=capsule.attachedRigidbody;
                if(b==actor.Motor.Body){fixture.ReplaceWithEllipsoid(capsule,new Vector3(.177f,.165f,.145f));continue;}
                if(b.name.StartsWith("Spine_")){fixture.ReplaceWithEllipsoid(capsule,new Vector3(.170f,.147f,.180f));continue;}
                if(b.name.StartsWith("Chest_")){fixture.ReplaceWithEllipsoid(capsule,new Vector3(.177f,.145f,.180f),3f);continue;}
                if(b.name.StartsWith("Head_"))capsule.radius=.15f;
                if(b.name.StartsWith("UpperArm_"))capsule.radius=.060f;
                capsule.height=Mathf.Max(capsule.height,capsule.radius*2);
            }
            fixture._colliders=fixture.Bodies.SelectMany(b=>b.GetComponentsInChildren<Collider>()).Where(c=>c.enabled&&!c.isTrigger&&c.attachedRigidbody!=null).Distinct().ToArray();
            for(var a=0;a<fixture._colliders.Length;a++)for(var b=a+1;b<fixture._colliders.Length;b++)Physics.IgnoreCollision(fixture._colliders[a],fixture._colliders[b]);
            foreach(var skin in actor.Pose.GetComponentsInChildren<SkinnedMeshRenderer>()){skin.sharedMaterial.SetColor("_BaseColor",new Color(.95f,.46f,.16f));fixture._owned.Add(skin.sharedMesh);fixture._owned.Add(skin.sharedMaterial);}
            fixture.Place(orientation);fixture._spawnPositions=fixture.Bodies.Select(b=>b.position).ToArray();fixture._spawnRotations=fixture.Bodies.Select(b=>b.rotation).ToArray();return fixture;
        }
        public void ResetFixture()
        {
            for(var i=0;i<Bodies.Length;i++){var b=Bodies[i];b.transform.SetPositionAndRotation(_spawnPositions[i],_spawnRotations[i]);b.position=_spawnPositions[i];b.rotation=_spawnRotations[i];b.linearVelocity=b.angularVelocity=Vector3.zero;}
            Physics.SyncTransforms();Capture();Actor.FixedPose.CaptureEvaluatedPose();
        }
        public void Place(Orientation orientation)
        {
            var roll=orientation==Orientation.FaceUp?0:orientation==Orientation.FaceDown?180:orientation==Orientation.LeftSide?-90:90;
            var rotation=Quaternion.AngleAxis(roll,Vector3.right)*Quaternion.LookRotation(Vector3.up,Vector3.right);
            var origin=Actor.Motor.Body.position;var destination=new Vector3(-.43f,.35f,.30f);
            foreach(var b in Bodies)
            {var p=destination+rotation*(b.position-origin);var q=rotation*b.rotation;b.transform.SetPositionAndRotation(p,q);b.position=p;b.rotation=q;b.linearVelocity=b.angularVelocity=Vector3.zero;}
            Physics.SyncTransforms();var minimum=_colliders.Min(c=>c.bounds.min.y);var delta=Vector3.up*(.035f-minimum);
            foreach(var b in Bodies){var p=b.position+delta;b.transform.position=p;b.position=p;}
            Physics.SyncTransforms();Capture();Actor.FixedPose.CaptureEvaluatedPose();
        }
        private void ReplaceWithEllipsoid(CapsuleCollider source,Vector3 radii,float power=2f)
        {
            // A round torso capsule cannot fit both the wider sides and flatter
            // front/back of this mesh. A small convex hull preserves that shape.
            const int sides=16,rings=8;var vertices=new List<Vector3>{Vector3.up*radii.y};var triangles=new List<int>();
            for(var row=1;row<rings;row++)for(var col=0;col<sides;col++)
            {
                var latitude=Mathf.PI*row/rings;var longitude=2*Mathf.PI*col/sides;
                var unit=new Vector3(Mathf.Sin(latitude)*Mathf.Cos(longitude),Mathf.Cos(latitude),Mathf.Sin(latitude)*Mathf.Sin(longitude));
                // Fill only the radial chest corners, keeping the axial ends
                // unchanged so the hull does not create an invisible gap.
                if(power>2)
                {
                    var angle=Mathf.Atan2(unit.y,unit.x);var radius=Mathf.Sqrt(Mathf.Max(0,1-unit.z*unit.z));var x=Mathf.Cos(angle);var y=Mathf.Sin(angle);
                    unit.x=Mathf.Sign(x)*Mathf.Pow(Mathf.Abs(x),2f/power)*radius;unit.y=Mathf.Sign(y)*Mathf.Pow(Mathf.Abs(y),2f/power)*radius;
                }
                vertices.Add(Vector3.Scale(unit,radii));
            }
            var bottom=vertices.Count;vertices.Add(Vector3.down*radii.y);
            for(var col=0;col<sides;col++)
            {
                var next=(col+1)%sides;triangles.AddRange(new[]{0,1+next,1+col});
                for(var row=0;row<rings-2;row++){var a=1+row*sides+col;var b=1+row*sides+next;triangles.AddRange(new[]{a,b,a+sides,b,b+sides,a+sides});}
                var last=1+(rings-2)*sides;triangles.AddRange(new[]{bottom,last+col,last+next});
            }
            var mesh=new Mesh{name="Low target convex torso"};mesh.SetVertices(vertices);mesh.SetTriangles(triangles,0);mesh.RecalculateBounds();_owned.Add(mesh);
            var go=new GameObject("Fitted skin contact");go.layer=source.gameObject.layer;go.transform.SetParent(source.transform,false);go.transform.localPosition=source.center;
            var collider=go.AddComponent<MeshCollider>();collider.sharedMesh=mesh;collider.convex=true;collider.sharedMaterial=source.sharedMaterial;collider.contactOffset=.002f;source.enabled=false;
        }
        public bool TrySurface(Vector3 from,out Vector3 point)
        {
            point=Vector3.zero;var best=float.PositiveInfinity;
            foreach(var c in _colliders)
            {
                if(c==null||!c.enabled||!c.gameObject.activeInHierarchy)continue;
                var p=c.ClosestPoint(from);var d=(p-from).sqrMagnitude;if(d>=best)continue;best=d;point=p;
            }
            return best<.8f*.8f;
        }
        public void Capture()
        {
            Actor.FixedPose.RestoreForEvaluation();Ragdoll.ApplyPose();
            if(_helpers!=null)_helpers.Apply();
            Actor.FixedPose.CaptureEvaluatedPose();
        }
        public void Present(float alpha)=>Actor.FixedPose.Present(alpha);
    }
}
