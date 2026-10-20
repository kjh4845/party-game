using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using ProjectHotfix.PhysicsJump;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
namespace ProjectHotfix.Physics.Tests
{
    public sealed partial class AutomaticLiftReviewTests
    {
        private struct Segment {public Vector3 a,b;public float radius;public string name;}
        private static Segment[] Arms(PhysicsJumpReview actor)
        {
            var bones=actor.Pose.GetComponentInChildren<SkinnedMeshRenderer>().bones;
            Transform Bone(string n)=>Array.Find(bones,b=>b.name==n);
            var result=new List<Segment>();
            for(var side=0;side<2;side++)
            {
                var suffix=side==0?"L":"R";
                result.Add(new Segment{a=Bone("UpperArm_"+suffix).position,b=Bone("Forearm_"+suffix).position,radius=.045f,name="upper"+side});
                result.Add(new Segment{a=Bone("Forearm_"+suffix).position,b=actor.GrabPose.Terminal(side),radius=.035f,name="forearm"+side});
            }
            return result.ToArray();
        }
        private static float SegmentDistance(Vector3 p1,Vector3 q1,Vector3 p2,Vector3 q2)
        {
            var d1=q1-p1;var d2=q2-p2;var r=p1-p2;var a=Vector3.Dot(d1,d1);var e=Vector3.Dot(d2,d2);var f=Vector3.Dot(d2,r);float s,t;
            if(a<=1e-9f&&e<=1e-9f)return r.magnitude;
            if(a<=1e-9f){s=0;t=Mathf.Clamp01(f/e);}
            else{var c=Vector3.Dot(d1,r);if(e<=1e-9f){t=0;s=Mathf.Clamp01(-c/a);}else{var b=Vector3.Dot(d1,d2);var denom=a*e-b*b;s=denom>1e-12f?Mathf.Clamp01((b*f-c*e)/denom):0;t=(b*s+f)/e;if(t<0){t=0;s=Mathf.Clamp01(-c/a);}else if(t>1){t=1;s=Mathf.Clamp01((b-c)/a);}}}
            return Vector3.Distance(p1+d1*s,p2+d2*t);
        }

        [UnityTest] public IEnumerator BoundedContactPreservesInputPhysicsAndMeasuresResidualOverlap()
        {
            var rows = new List<string>();
            foreach (var hands in new[] { 1, 3 })
            {
                var reference = new List<Frame>();
                foreach (var candidate in new[] { false, true })
                {
                    Create(); foreach (var actor in _arena.Actors) actor.Hands.Grabs.BoundedContactPose = candidate;
                    Buttons(A, hands); Buttons(B, hands == 1 ? 2 : 3);
                    var overlap = 0f; var heldOverlap = 0f; var correction = 0f; var offset = 0f; var physicsError = 0f;
                    for (var tick = 0; tick < 130; tick++)
                    {
                        if (tick == 70) { A.Motor.MoveInput = Vector2.right; B.Motor.MoveInput = Vector2.right; }
                        if (tick == 80) A.Motor.RequestJump();
                        if (tick == 90) Buttons(B, 0);
                        if (tick == 110) Buttons(A, 0);
                        Tick(); var sample = Sample(tick);
                        if (!candidate) reference.Add(sample);
                        else
                        {
                            var old = reference[tick]; physicsError = Mathf.Max(physicsError, Vector3.Distance(old.owner, sample.owner), Vector3.Distance(old.target, sample.target));
                            Assert.That(sample.held, Is.EqualTo(old.held)); Assert.That(sample.throws, Is.EqualTo(old.throws)); Assert.That(sample.mutual, Is.EqualTo(old.mutual));
                        }
                        foreach (var actor in _arena.Actors)
                        {
                            correction = Mathf.Max(correction, actor.GrabPose.MaximumBoneCorrection); offset = Mathf.Max(offset, actor.GrabPose.MaximumContactOffset);
                            Assert.That(actor.GrabPose.MaximumContactOffset, Is.LessThanOrEqualTo(.03501f));
                            Assert.That(actor.GrabPose.MaximumBoneCorrection, Is.LessThanOrEqualTo(18.01f));
                        }
                        foreach (var alpha in new[] { 0f, .5f, 1f })
                        {
                            foreach (var actor in _arena.Actors) actor.FixedPose.Present(alpha);
                            foreach (var a in Arms(A)) foreach (var b in Arms(B))
                            {
                                var depth = Mathf.Max(0, a.radius + b.radius - SegmentDistance(a.a, a.b, b.a, b.b));
                                overlap = Mathf.Max(overlap, depth); if (tick >= 25 && tick < 70) heldOverlap = Mathf.Max(heldOverlap, depth);
                            }
                        }
                    }
                    rows.Add($"candidate={candidate}, hands={hands}, physicsError={physicsError:R}, allArmEnvelopeOverlap={overlap:R}, stableMutualOverlap={heldOverlap:R}, maxBoneCorrection={correction:R}, maxOffset={offset:R}, throws={G.ThrowCount}/{B.Hands.Grabs.ThrowCount}");
                    File.WriteAllLines(Path.Combine(Output, "BoundedMutual.txt"), rows);
                    Assert.That(physicsError, Is.LessThan(.00001f));
                    // B's throw can break A's remaining grip before A releases.
                    Assert.That(B.Hands.Grabs.ThrowCount, Is.EqualTo(1));
                    var op = SceneManager.UnloadSceneAsync(_scene); while (op != null && !op.isDone) yield return null;
                }
            }
        }
    }
}
