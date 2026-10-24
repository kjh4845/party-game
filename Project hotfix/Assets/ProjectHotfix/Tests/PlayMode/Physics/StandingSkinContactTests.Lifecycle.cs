using System.Collections;
using System.Linq;
using NUnit.Framework;
using ProjectHotfix.PhysicsJump;
using UnityEngine;
using UnityEngine.TestTools;

namespace ProjectHotfix.Physics.Tests
{
    public sealed partial class StandingSkinContactTests
    {
        [UnityTest] public IEnumerator ContactModesPreserveMassMaterialAndRestoreSymmetricCollisionPairs()
        {
            Create(candidate:false);var bodies=_pair.Players.SelectMany(p=>p.Rig.Links.Select(l=>l.Body).Append(p.Rig.Actor.Motor.Body)).ToArray();
            var centers=bodies.Select(b=>b.centerOfMass).ToArray();var inertia=bodies.Select(b=>b.inertiaTensor).ToArray();var auto=bodies.Select(b=>b.automaticInertiaTensor).ToArray();
            var arms=_pair.Players.SelectMany(p=>new[]{p.Rig.Find("Forearm_L").Shape,p.Rig.Find("Forearm_R").Shape}).ToArray();
            var shapes=bodies.SelectMany(b=>b.GetComponentsInChildren<Collider>()).Where(c=>c.enabled&&!c.isTrigger).Distinct().ToArray();
            var ignored=arms.Select(a=>shapes.Select(b=>a==b||UnityEngine.Physics.GetIgnoreCollision(a,b)).ToArray()).ToArray();
            _pair.EnableStandingSkinContacts=true;Tick();Assert.That(_pair.StandingContacts.PatchCount,Is.EqualTo(24));Buttons(0,3);Buttons(1,3);Assert.That(Acquire(4),Is.True);
            for(var i=0;i<bodies.Length;i++){Assert.That(bodies[i].centerOfMass,Is.EqualTo(centers[i]));Assert.That(bodies[i].inertiaTensor,Is.EqualTo(inertia[i]));}
            foreach(var patch in UnityEngine.Object.FindObjectsByType<StandingSkinPatchReview>(FindObjectsSortMode.None).Where(p=>p.gameObject.scene==_scene&&p.Shape.enabled))
            {
                Assert.That(patch.Shape.sharedMaterial,Is.SameAs(shapes.First(c=>c.attachedRigidbody==patch.Anchor.Body).sharedMaterial));
                foreach(var shape in shapes)Assert.That(UnityEngine.Physics.GetIgnoreCollision(patch.Shape,shape),Is.EqualTo(shape!=patch.Owner));
                if(patch.Player==1)Assert.That(patch.Anchor.Body.name.StartsWith("Forearm_"),Is.False,"Duplicate forearm contact representation");
            }
            _pair.EnableStandingSkinContacts=false;Tick();Assert.That(_pair.StandingContacts,Is.Null);Assert.That(_pair.PhysicalConnections+_pair.LogicalGrips+_pair.Throws,Is.Zero);
            for(var i=0;i<bodies.Length;i++){Assert.That(Vector3.Distance(bodies[i].centerOfMass,centers[i]),Is.LessThan(.000001));Assert.That(Vector3.Distance(bodies[i].inertiaTensor,inertia[i]),Is.LessThan(.000001));Assert.That(bodies[i].automaticInertiaTensor,Is.EqualTo(auto[i]));}
            for(var a=0;a<arms.Length;a++)for(var b=0;b<shapes.Length;b++)if(arms[a]!=shapes[b])Assert.That(UnityEngine.Physics.GetIgnoreCollision(arms[a],shapes[b]),Is.EqualTo(ignored[a][b]));
            foreach(var player in _pair.Players)Assert.That(player.Rig.TotalMass,Is.EqualTo(20));yield return null;
        }
        [UnityTest] public IEnumerator LosingHandColliderOnReleaseDoesNotThrow()
        {
            Create(lift:true);Buttons(0,1);Assert.That(Acquire(1),Is.True);_pair.Players[0].Rig.Find("Forearm_L").Shape.enabled=false;Buttons(0,0);Tick();
            Assert.That(_pair.Throws+_pair.LogicalGrips+_pair.PhysicalConnections,Is.Zero);yield return null;
        }
    }
}
