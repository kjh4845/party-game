using System.Collections;
using System.Linq;
using NUnit.Framework;
using ProjectHotfix.PhysicsJump;
using UnityEngine;
using UnityEngine.TestTools;

namespace ProjectHotfix.Physics.Tests
{
    public sealed partial class SkinContactCandidateTests
    {
        [UnityTest] public IEnumerator ContactModeRestoresMassFramesAndCollisionPairs()
        {
            Create(contacts:false);var bodies=_pair.ExternalTarget.Bodies;
            var centers=bodies.Select(b=>b.centerOfMass).ToArray();var inertia=bodies.Select(b=>b.inertiaTensor).ToArray();var rotations=bodies.Select(b=>b.inertiaTensorRotation).ToArray();
            var autoCenters=bodies.Select(b=>b.automaticCenterOfMass).ToArray();var autoInertia=bodies.Select(b=>b.automaticInertiaTensor).ToArray();
            var hands=new[]{"Forearm_L","Forearm_R"}.Select(n=>_pair.Players[0].Rig.Find(n).Shape).ToArray();
            var shapes=bodies.SelectMany(b=>b.GetComponentsInChildren<Collider>()).Where(c=>c.enabled&&!c.isTrigger).Distinct().ToArray();var ignored=hands.Select(h=>shapes.Select(s=>UnityEngine.Physics.GetIgnoreCollision(h,s)).ToArray()).ToArray();
            _pair.EnableSkinContacts=true;Tick();Assert.That(_pair.SkinContacts.PatchCount,Is.EqualTo(12));Acquire();
            for(var i=0;i<bodies.Length;i++){Assert.That(bodies[i].centerOfMass,Is.EqualTo(centers[i]));Assert.That(bodies[i].inertiaTensor,Is.EqualTo(inertia[i]));Assert.That(Quaternion.Angle(bodies[i].inertiaTensorRotation,rotations[i]),Is.LessThan(.001));}
            _pair.EnableSkinContacts=false;Tick();Assert.That(_pair.SkinContacts,Is.Null);Assert.That(_pair.LogicalGrips+_pair.PhysicalConnections+_pair.Throws,Is.Zero);
            for(var i=0;i<bodies.Length;i++){Assert.That(Vector3.Distance(bodies[i].centerOfMass,centers[i]),Is.LessThan(.000001));Assert.That(Vector3.Distance(bodies[i].inertiaTensor,inertia[i]),Is.LessThan(.000001));Assert.That(bodies[i].automaticCenterOfMass,Is.EqualTo(autoCenters[i]));Assert.That(bodies[i].automaticInertiaTensor,Is.EqualTo(autoInertia[i]));}
            for(var h=0;h<2;h++)for(var i=0;i<shapes.Length;i++)Assert.That(UnityEngine.Physics.GetIgnoreCollision(hands[h],shapes[i]),Is.EqualTo(ignored[h][i]));
            yield return null;
        }
        [UnityTest] public IEnumerator TargetDisableReenableCannotReuseHeldInput()
        {
            Create();Acquire();_pair.ExternalTarget.gameObject.SetActive(false);Tick();Assert.That(_pair.LogicalGrips+_pair.PhysicalConnections+_pair.Throws,Is.Zero);Assert.That(_pair.LiftSupportForce,Is.Zero);
            _pair.ExternalTarget.gameObject.SetActive(true);for(var i=0;i<30;i++)Tick();Assert.That(_pair.LogicalGrips,Is.Zero);
            foreach(var name in new[]{"Forearm_L","Forearm_R"})foreach(var shape in _pair.ExternalTarget.Bodies.SelectMany(b=>b.GetComponentsInChildren<Collider>()).Where(c=>c.enabled&&!c.isTrigger&&c.GetComponent<SkinContactPatchReview>()==null).Distinct())
                Assert.That(UnityEngine.Physics.GetIgnoreCollision(_pair.Players[0].Rig.Find(name).Shape,shape),Is.True);
            _pair.ResetReview();Buttons(0);Tick();for(var i=0;i<150;i++)Tick();Acquire();Assert.That(_pair.Throws,Is.Zero);yield return null;
        }
        [UnityTest] public IEnumerator ActivePatchesOnlyContactTheirOwnForearm()
        {
            Create();Acquire();
            var all=UnityEngine.Object.FindObjectsByType<Collider>(FindObjectsSortMode.None).Where(c=>c.gameObject.scene==_scene&&c.enabled&&c.gameObject.activeInHierarchy).ToArray();
            foreach(var patch in UnityEngine.Object.FindObjectsByType<SkinContactPatchReview>(FindObjectsSortMode.None).Where(p=>p.gameObject.scene==_scene&&p.Shape.enabled))
            {
                var material=all.First(c=>c.attachedRigidbody==patch.Shape.attachedRigidbody&&c.GetComponent<SkinContactPatchReview>()==null).sharedMaterial;
                Assert.That(patch.Shape.sharedMaterial,Is.SameAs(material));
                foreach(var shape in all.Where(c=>c!=patch.Shape))Assert.That(UnityEngine.Physics.GetIgnoreCollision(patch.Shape,shape),Is.EqualTo(shape!=patch.OwnerShape));
            }
            yield return null;
        }
    }
}
