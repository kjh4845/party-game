using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ProjectHotfix.PhysicsJump
{
    // Local contact candidate for a live forearm against the permanent Down fixture.
    // No render mesh offset, root teleport, extra body or change to mass/pose drives.
    public sealed class SkinContactReview : IDisposable
    {
        private sealed class MassState
        {
            public Rigidbody Body;public Vector3 Center,Inertia;public Quaternion Rotation;public bool AutoCenter,AutoInertia;
            public void Preserve(){if(Body==null)return;Body.centerOfMass=Center;Body.inertiaTensor=Inertia;Body.inertiaTensorRotation=Rotation;}
            public void Restore(){if(Body==null)return;Preserve();Body.automaticCenterOfMass=AutoCenter;Body.automaticInertiaTensor=AutoInertia;}
        }
        private readonly ActiveGrabPairReview _pair;
        private readonly LimpGrabTargetReview _target;
        private readonly List<SkinContactPatchReview> _patches=new List<SkinContactPatchReview>();
        private readonly List<(Collider hand,Collider target,bool ignored)> _pairs=new List<(Collider,Collider,bool)>();
        private readonly MassState[] _mass;
        private readonly Dictionary<Rigidbody,PhysicsMaterial> _materials;
        private Collider[] _sceneColliders;
        public SkinSurfaceReview Surface {get;private set;}
        public int PatchCount=>_patches.Count;
        public int ActivePatches=>_patches.Count(p=>p!=null&&p.Shape.enabled);
        public float LastStepMilliseconds {get;private set;}
        public int FixedSteps {get;private set;}
        private bool _disposed;
        public SkinContactReview(ActiveGrabPairReview pair)
        {
            _pair=pair;_target=pair.ExternalTarget;Surface=new SkinSurfaceReview(_target.Actor,_target.Bodies);
            _mass=_target.Bodies.Select(b=>new MassState{Body=b,Center=b.centerOfMass,Inertia=b.inertiaTensor,Rotation=b.inertiaTensorRotation,AutoCenter=b.automaticCenterOfMass,AutoInertia=b.automaticInertiaTensor}).ToArray();
            var targetShapes=_target.Bodies.SelectMany(b=>b.GetComponentsInChildren<Collider>()).Where(c=>c.enabled&&!c.isTrigger).Distinct().ToArray();
            _materials=targetShapes.GroupBy(c=>c.attachedRigidbody).ToDictionary(g=>g.Key,g=>g.First().sharedMaterial);
            for(var side=0;side<2;side++)
            {
                var owner=pair.Players[0].Rig.Find("Forearm_"+(side==0?"L":"R"));var shape=(CapsuleCollider)owner.Shape;
                foreach(var other in targetShapes){_pairs.Add((shape,other,Physics.GetIgnoreCollision(shape,other)));Physics.IgnoreCollision(shape,other,true);}
                for(var i=0;i<6;i++)
                {
                    var go=new GameObject("Skin contact "+side+"/"+i);SceneManager.MoveGameObjectToScene(go,pair.gameObject.scene);go.transform.SetParent(_target.Bodies[0].transform,false);
                    var patch=go.AddComponent<SkinContactPatchReview>();patch.Shape=go.AddComponent<SphereCollider>();patch.Shape.enabled=false;patch.Shape.radius=.08f;patch.Shape.contactOffset=.001f;
                    patch.OwnerShape=shape;patch.Side=side;patch.Fraction=i/5f;_patches.Add(patch);
                }
            }
            _sceneColliders=UnityEngine.Object.FindObjectsByType<Collider>(FindObjectsInactive.Include,FindObjectsSortMode.None).Where(c=>c.gameObject.scene==pair.gameObject.scene).ToArray();
            foreach(var patch in _patches)RouteCollisions(patch);foreach(var m in _mass)m.Preserve();
        }
        private void RouteCollisions(SkinContactPatchReview patch)
        {
            foreach(var other in _sceneColliders)if(other!=null&&other!=patch.Shape)Physics.IgnoreCollision(patch.Shape,other,other!=patch.OwnerShape);
        }
        public void Step()
        {
            var watch=System.Diagnostics.Stopwatch.StartNew();FixedSteps++;
            if(_target==null||!_target.Available||!_pair.Players[0].Rig.isActiveAndEnabled)
            {foreach(var p in _patches)if(p!=null&&p.Shape!=null){p.Shape.enabled=false;p.ExpectedEnabled=false;}return;}
            Surface.BeginFixedFrame();
            // A saved material coordinate keeps a held contact on the same skin
            // triangle as it bends. The connected physical body still bears load.
            foreach(var hand in _pair.Players[0].Hands)
            {
                var grip=hand.Connection;if(grip==null||!grip.Valid||grip.SurfaceB==null)continue;
                grip.LocalB=Quaternion.Inverse(grip.B.rotation)*(grip.SurfaceB.Position-grip.B.position);grip.Joint.connectedAnchor=grip.LocalB;
            }
            foreach(var patch in _patches)
            {
                if(patch==null||patch.Shape==null)continue;
                if(patch.OwnerShape==null||!patch.OwnerShape.enabled||patch.OwnerShape.isTrigger||patch.OwnerShape.attachedRigidbody==null){patch.Shape.enabled=false;patch.ExpectedEnabled=false;continue;}
                if(patch.Lost||patch.Shape.isTrigger||!patch.gameObject.activeInHierarchy||patch.ExpectedEnabled&&!patch.Shape.enabled){patch.Lost=true;patch.Shape.enabled=false;continue;}
                var hand=_pair.Players[0].Hands[patch.Side];var body=patch.OwnerShape.attachedRigidbody;var shape=patch.OwnerShape;
                var half=Mathf.Max(0,shape.height*.5f-shape.radius);var point=body.position+body.rotation*(shape.center+Vector3.back*Mathf.Lerp(-half,half,patch.Fraction));
                var pinned=hand.Connection!=null&&hand.Connection.Valid&&hand.TargetCollider==patch.Shape&&hand.Connection.SurfaceB!=null;
                patch.Anchor=pinned?hand.Connection.SurfaceB:Surface.Closest(point,patch.Anchor);
                var active=pinned||Vector3.Distance(point,patch.Anchor.Position)<.25f;
                var changed=patch.transform.parent!=patch.Anchor.Body.transform||patch.Shape.enabled!=active;
                patch.transform.SetParent(patch.Anchor.Body.transform,true);
                patch.Shape.sharedMaterial=_materials[patch.Anchor.Body];
                patch.transform.SetPositionAndRotation(patch.Anchor.Position-patch.Anchor.Normal*patch.Shape.radius,Quaternion.identity);
                patch.Shape.enabled=active;patch.ExpectedEnabled=active;if(changed)RouteCollisions(patch);
            }
            // Child collider fitting must not alter the original body mass frame.
            foreach(var state in _mass)state.Preserve();watch.Stop();LastStepMilliseconds=(float)watch.Elapsed.TotalMilliseconds;
        }
        public void Dispose()
        {
            if(_disposed)return;_disposed=true;
            foreach(var patch in _patches)if(patch!=null){if(patch.Shape!=null)patch.Shape.enabled=false;UnityEngine.Object.Destroy(patch.gameObject);}
            foreach(var pair in _pairs)if(pair.hand!=null&&pair.target!=null)Physics.IgnoreCollision(pair.hand,pair.target,pair.ignored);
            foreach(var state in _mass)state.Restore();
        }
    }
    public sealed class SkinContactPatchReview : MonoBehaviour
    {
        public SphereCollider Shape;
        public CapsuleCollider OwnerShape;
        public int Side;
        public float Fraction;
        public SkinSurfaceReview.Anchor Anchor;
        internal bool ExpectedEnabled,Lost;
    }
}
