using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ProjectHotfix.PhysicsJump
{
    // Two-way extension kept separate from the approved permanent-Down fixture.
    public sealed class StandingSkinContactReview : IDisposable
    {
        private sealed class MassState
        {
            public Rigidbody Body;public Vector3 Center,Inertia;public Quaternion Rotation;public bool AutoCenter,AutoInertia;
            public void Preserve(){if(Body==null)return;Body.centerOfMass=Center;Body.inertiaTensor=Inertia;Body.inertiaTensorRotation=Rotation;}
            public void Restore(){if(Body==null)return;Preserve();Body.automaticCenterOfMass=AutoCenter;Body.automaticInertiaTensor=AutoInertia;}
        }
        private readonly ActiveGrabPairReview _pair;
        private readonly SkinSurfaceReview[] _surfaces=new SkinSurfaceReview[2];
        private readonly List<StandingSkinPatchReview> _patches=new List<StandingSkinPatchReview>();
        private readonly Dictionary<Rigidbody,int> _actorByBody=new Dictionary<Rigidbody,int>();
        private readonly Dictionary<Rigidbody,PhysicsMaterial> _materials=new Dictionary<Rigidbody,PhysicsMaterial>();
        private readonly HashSet<Rigidbody> _forearms=new HashSet<Rigidbody>();
        private readonly List<(Collider a,Collider b,bool ignored)> _pairs=new List<(Collider,Collider,bool)>();
        private readonly List<MassState> _mass=new List<MassState>();
        private readonly HashSet<ActiveGrabPairReview.PhysicalGrip> _updated=new HashSet<ActiveGrabPairReview.PhysicalGrip>();
        private Collider[] _sceneColliders;
        private bool _disposed;
        public int PatchCount=>_patches.Count;
        public int FixedSteps {get;private set;}
        public float LastStepMilliseconds {get;private set;}
        public SkinSurfaceReview Surface(int actor)=>_surfaces[actor];
        public StandingSkinContactReview(ActiveGrabPairReview pair)
        {
            _pair=pair;var shapes=new Collider[2][];
            for(var p=0;p<2;p++)
            {
                var rig=pair.Players[p].Rig;var bodies=rig.Links.Select(l=>l.Body).Append(rig.Actor.Motor.Body).ToArray();
                _surfaces[p]=new SkinSurfaceReview(rig.Actor,bodies);
                shapes[p]=bodies.SelectMany(b=>b.GetComponentsInChildren<Collider>()).Where(c=>c.enabled&&!c.isTrigger&&c.attachedRigidbody!=null).Distinct().ToArray();
                foreach(var body in bodies)
                {
                    _actorByBody[body]=p;_materials[body]=shapes[p].First(c=>c.attachedRigidbody==body).sharedMaterial;
                    _mass.Add(new MassState{Body=body,Center=body.centerOfMass,Inertia=body.inertiaTensor,Rotation=body.inertiaTensorRotation,AutoCenter=body.automaticCenterOfMass,AutoInertia=body.automaticInertiaTensor});
                }
                foreach(var name in new[]{"Forearm_L","Forearm_R"})_forearms.Add(rig.Find(name).Body);
            }
            var seen=new HashSet<(Collider,Collider)>();
            for(var p=0;p<2;p++)for(var side=0;side<2;side++)
            {
                var shape=(CapsuleCollider)pair.Players[p].Rig.Find("Forearm_"+(side==0?"L":"R")).Shape;
                foreach(var other in shapes[1-p])
                {
                    var key=shape.GetInstanceID()<other.GetInstanceID()?((Collider)shape,other):(other,(Collider)shape);
                    if(seen.Add(key)){_pairs.Add((shape,other,Physics.GetIgnoreCollision(shape,other)));Physics.IgnoreCollision(shape,other,true);}
                }
                for(var sample=0;sample<6;sample++)
                {
                    var go=new GameObject($"Standing skin contact {p}/{side}/{sample}");SceneManager.MoveGameObjectToScene(go,pair.gameObject.scene);go.transform.SetParent(pair.Players[1-p].Rig.Actor.Motor.Body.transform,false);
                    var patch=go.AddComponent<StandingSkinPatchReview>();patch.Shape=go.AddComponent<SphereCollider>();patch.Shape.enabled=false;patch.Shape.radius=.08f;patch.Shape.contactOffset=.001f;
                    patch.Owner=shape;patch.Player=p;patch.Side=side;patch.Fraction=sample/5f;_patches.Add(patch);
                }
            }
            _sceneColliders=UnityEngine.Object.FindObjectsByType<Collider>(FindObjectsInactive.Include,FindObjectsSortMode.None).Where(c=>c.gameObject.scene==pair.gameObject.scene).ToArray();
            foreach(var m in _mass)m.Preserve();
        }
        // A knocked-down rig hands its body to the ragdoll and deactivates the links its patches hang
        // from; treat it as inactive so those patches are switched off rather than marked Lost for good.
        private bool Active(int p)=>_pair.Players[p].Rig!=null&&_pair.Players[p].Rig.isActiveAndEnabled&&!_pair.Players[p].Rig.RagdollActive;
        private void Route(StandingSkinPatchReview patch)
        {foreach(var other in _sceneColliders)if(other!=null&&other!=patch.Shape)Physics.IgnoreCollision(patch.Shape,other,other!=patch.Owner);}
        public SkinSurfaceReview.Anchor Contact(Rigidbody body,Vector3 point)
        {
            if(body==null||!_actorByBody.TryGetValue(body,out var actor)||!Active(actor))return null;
            var result=_surfaces[actor].Closest(point);result.Body=body;return result;
        }
        public void Step()
        {
            var watch=System.Diagnostics.Stopwatch.StartNew();FixedSteps++;
            for(var p=0;p<2;p++)if(Active(p))_surfaces[p].BeginFixedFrame();
            _updated.Clear();
            foreach(var hand in _pair.Players.SelectMany(p=>p.Hands))
            {
                var grip=hand.Connection;if(grip==null||!grip.Valid||grip.SurfaceB==null||!_updated.Add(grip))continue;
                grip.LocalB=Quaternion.Inverse(grip.B.rotation)*(grip.SurfaceB.Position-grip.B.position);grip.Joint.connectedAnchor=grip.LocalB;
            }
            foreach(var patch in _patches)
            {
                if(patch==null||patch.Shape==null)continue;
                if(!Active(patch.Player)||!Active(1-patch.Player)){patch.Shape.enabled=false;patch.ExpectedEnabled=false;continue;}
                if(patch.Owner==null||!patch.Owner.enabled||patch.Owner.isTrigger||patch.Owner.attachedRigidbody==null){patch.Shape.enabled=false;patch.ExpectedEnabled=false;continue;}
                if(patch.Lost||patch.Shape.isTrigger||!patch.gameObject.activeInHierarchy||patch.ExpectedEnabled&&!patch.Shape.enabled){patch.Lost=true;patch.Shape.enabled=false;continue;}
                var hand=_pair.Players[patch.Player].Hands[patch.Side];var owner=patch.Owner.attachedRigidbody;var shape=patch.Owner;
                var half=Mathf.Max(0,shape.height*.5f-shape.radius);var point=owner.position+owner.rotation*(shape.center+Vector3.back*Mathf.Lerp(-half,half,patch.Fraction));
                var pinned=hand.Connection!=null&&hand.Connection.Valid&&hand.TargetCollider==patch.Shape&&hand.Connection.SurfaceB!=null;
                patch.Anchor=pinned?hand.Connection.SurfaceB:_surfaces[1-patch.Player].Closest(point,patch.Anchor);
                // Each forearm/forearm pair has one contact representation. Both
                // dynamic bodies still receive the collision callback/reaction.
                var canonical=!_forearms.Contains(patch.Anchor.Body)||patch.Player==0;
                var active=canonical&&(pinned||Vector3.Distance(point,patch.Anchor.Position)<.25f);
                var changed=patch.transform.parent!=patch.Anchor.Body.transform||patch.Shape.enabled!=active;
                patch.transform.SetParent(patch.Anchor.Body.transform,true);patch.Shape.sharedMaterial=_materials[patch.Anchor.Body];
                patch.transform.SetPositionAndRotation(patch.Anchor.Position-patch.Anchor.Normal*patch.Shape.radius,Quaternion.identity);
                patch.Shape.enabled=active;patch.ExpectedEnabled=active;if(changed)Route(patch);
            }
            // Not while knocked down: the ragdoll owns the root's mass properties then, and the standing
            // tensor (X/Z zeroed by the rotation freeze) locked the limp pelvis's tilt (2026-10-17).
            foreach(var m in _mass)if(Active(_actorByBody[m.Body]))m.Preserve();watch.Stop();LastStepMilliseconds=(float)watch.Elapsed.TotalMilliseconds;
        }
        public void Dispose()
        {
            if(_disposed)return;_disposed=true;
            foreach(var p in _patches)if(p!=null){if(p.Shape!=null)p.Shape.enabled=false;UnityEngine.Object.Destroy(p.gameObject);}
            foreach(var p in _pairs)if(p.a!=null&&p.b!=null)Physics.IgnoreCollision(p.a,p.b,p.ignored);
            foreach(var m in _mass)m.Restore();
        }
    }
    public sealed class StandingSkinPatchReview : MonoBehaviour
    {
        public SphereCollider Shape;
        public CapsuleCollider Owner;
        public int Player,Side;
        public float Fraction;
        public SkinSurfaceReview.Anchor Anchor;
        internal bool ExpectedEnabled,Lost;
    }
}
