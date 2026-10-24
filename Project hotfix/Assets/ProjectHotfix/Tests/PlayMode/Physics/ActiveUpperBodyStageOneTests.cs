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
    public sealed class ActiveUpperBodyStageOneTests
    {
        private const float Dt=1f/60;
        private Scene _scene;
        private ActiveUpperBodyReview _lab;
        private string Output=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../.codex_tmp/active-upper-stage1-20260915"));
        private void Create(bool native=false)
        {
            _scene=SceneManager.CreateScene("Active upper stage one",new CreateSceneParameters(native?LocalPhysicsMode.None:LocalPhysicsMode.Physics3D));
            _lab=ActiveUpperBodyReview.Create(_scene);_lab.ManualStep=!native;_lab.ReadKeyboard=false;_lab.View.enabled=false;Directory.CreateDirectory(Output);
        }
        private void Tick()
        { _lab.Step(Dt);_scene.GetPhysicsScene().Simulate(Dt);_lab.CapturePhysics(); }
        [UnityTearDown] public IEnumerator Cleanup()
        { if(!_scene.IsValid()||!_scene.isLoaded)yield break;var op=SceneManager.UnloadSceneAsync(_scene);while(op!=null&&!op.isDone)yield return null; }
        [UnityTest] public IEnumerator InspectJointTargetsAndSkin()
        {
            Create();var rows=new List<string>();var skin=_lab.Actor.Pose.GetComponentsInChildren<SkinnedMeshRenderer>().Single(s=>s.name.EndsWith("SkinnedBody"));
            var mesh=new Mesh();var triangles=skin.sharedMesh.triangles;var pairs=CameraGaitReviewTests.Pairs(triangles);var surface=0f;
            foreach(var exercise in new[]{ActiveUpperBodyReview.Exercise.Rest,ActiveUpperBodyReview.Exercise.Reach,ActiveUpperBodyReview.Exercise.Curl})
            {
                _lab.TargetExercise=exercise;
                for(var tick=0;tick<150;tick++)
                {
                    Tick();if(tick%10!=0&&tick!=149)continue;
                    skin.BakeMesh(mesh);var angle=CameraGaitReviewTests.Surface(mesh.vertices,triangles,pairs,out var location);surface=Mathf.Max(surface,angle);
                    rows.Add($"{exercise} tick={tick} root={_lab.Actor.Motor.Body.position:R} handL={_lab.HandPosition(0):R} goalL={_lab.GoalHand(0):R} skin={angle:R} at={location:R} gap={_lab.MaximumJointGap:R} "+string.Join(" ",_lab.Links.Select(l=>$"{l.Name}={_lab.TrackingError(l.Name):F2}[target={l.Joint.targetRotation.eulerAngles:R}]")));
                }
            }
            File.WriteAllLines(Path.Combine(Output,"JointInspection.txt"),rows);UnityEngine.Object.Destroy(mesh);
            Assert.That(_lab.TotalMass,Is.EqualTo(20));Assert.That(_lab.MaximumJointGap,Is.LessThan(.008f));
            Assert.That(surface,Is.LessThan(60));Assert.That(_lab.Links.Max(l=>_lab.TrackingError(l.Name)),Is.LessThan(12));
            yield return null;
        }
        [UnityTest] public IEnumerator ContactDeflectsThePhysicalArmWithoutChangingTheReference()
        {
            Create();for(var i=0;i<60;i++)Tick();_lab.SetObstacle(true);_lab.TargetExercise=ActiveUpperBodyReview.Exercise.Reach;
            var penetration=0f;
            for(var i=0;i<120;i++)
            {
                Tick();foreach(var shape in _lab.Shapes)
                    if(UnityEngine.Physics.ComputePenetration(shape,shape.transform.position,shape.transform.rotation,_lab.Obstacle,_lab.Obstacle.transform.position,_lab.Obstacle.transform.rotation,out _,out var depth))penetration=Mathf.Max(penetration,depth);
            }
            var blocked=Vector3.Distance(_lab.HandPosition(0),_lab.GoalHand(0));var contacts=_lab.ObstacleContacts;var impulse=_lab.MaximumContactImpulse;
            _lab.SetObstacle(false);for(var i=0;i<150;i++)Tick();var free=Vector3.Distance(_lab.HandPosition(0),_lab.GoalHand(0));
            File.WriteAllText(Path.Combine(Output,"Contact.txt"),$"contacts={contacts}, maximumImpulse={impulse:R}, colliderPenetration={penetration:R}, blockedTargetError={blocked:R}, freeTargetError={free:R}, gap={_lab.MaximumJointGap:R}");
            Assert.That(contacts,Is.GreaterThan(0));Assert.That(impulse,Is.GreaterThan(.001f));Assert.That(penetration,Is.LessThan(.008f));
            Assert.That(blocked,Is.GreaterThan(free+.025f));Assert.That(free,Is.LessThan(.08f));yield return null;
        }
        [UnityTest] public IEnumerator RemovingArmDriveLetsGravityActAndRestoringItRecovers()
        {
            Create();_lab.TargetExercise=ActiveUpperBodyReview.Exercise.Reach;for(var i=0;i<150;i++)Tick();
            var left=_lab.HandPosition(0);var right=_lab.HandPosition(1);_lab.LeftPowered=false;
            for(var i=0;i<100;i++)
            {
                Tick();foreach(var name in new[]{"UpperArm_L","Forearm_L"})
                {var drive=_lab.Find(name).Joint.slerpDrive;Assert.That(drive.positionSpring+drive.positionDamper+drive.maximumForce,Is.Zero);}
            }
            var drop=left.y-_lab.HandPosition(0).y;var rightDrop=right.y-_lab.HandPosition(1).y;
            _lab.LeftPowered=true;for(var i=0;i<150;i++)Tick();var restored=Vector3.Distance(_lab.HandPosition(0),_lab.GoalHand(0));
            File.WriteAllText(Path.Combine(Output,"DriveOff.txt"),$"leftDrop={drop:R}, rightDrop={rightDrop:R}, restoredTargetError={restored:R}");
            Assert.That(drop,Is.GreaterThan(.12f));Assert.That(Mathf.Abs(rightDrop),Is.LessThan(.08f));Assert.That(restored,Is.LessThan(.08f));yield return null;
        }
        [UnityTest] public IEnumerator MassCollisionPairsAndResetKeepTheLabIsolated()
        {
            Create();Assert.That(_lab.TotalMass,Is.EqualTo(20));Assert.That(_lab.Links.Count,Is.EqualTo(7));
            Assert.That(_lab.Actor.GetComponent<GrabPhysicsReview>(),Is.Null);Assert.That(_lab.Actor.GetComponent<RagdollRecoveryReview>(),Is.Null);
            foreach(var link in _lab.Links)
            {
                Assert.That(link.Body.isKinematic,Is.False);Assert.That(link.Shape.isTrigger,Is.False);
                Assert.That(link.Body.solverIterations,Is.EqualTo(24));Assert.That(link.Joint.projectionMode,Is.EqualTo(JointProjectionMode.None));
            }
            Assert.That(UnityEngine.Physics.GetIgnoreCollision(_lab.Find("UpperArm_L").Shape,_lab.Find("Forearm_L").Shape),Is.True);
            Assert.That(UnityEngine.Physics.GetIgnoreCollision(_lab.Find("Forearm_L").Shape,_lab.Find("Chest").Shape),Is.False);
            Assert.That(UnityEngine.Physics.GetIgnoreCollision(_lab.Find("Forearm_L").Shape,_lab.Find("Forearm_R").Shape),Is.False);
            _lab.TargetExercise=ActiveUpperBodyReview.Exercise.Reach;for(var i=0;i<100;i++)Tick();_lab.LeftPowered=false;_lab.SetObstacle(true);
            for(var i=0;i<30;i++)Tick();_lab.ResetReview();Assert.That(_lab.Obstacle.gameObject.activeSelf,Is.False);
            foreach(var link in _lab.Links){Assert.That(link.Body.linearVelocity.sqrMagnitude,Is.Zero);Assert.That(Quaternion.Angle(link.Body.rotation,link.StartRotation),Is.LessThan(.05f));}
            for(var i=0;i<90;i++)Tick();Assert.That(_lab.TotalMass,Is.EqualTo(20));Assert.That(_lab.MaximumBodySpeed,Is.LessThan(4));yield return null;
        }
        [UnityTest] public IEnumerator EnabledSelfCollisionPairsStartClearAndRemainBounded()
        {
            Create();var maximum=0f;
            foreach(var exercise in new[]{ActiveUpperBodyReview.Exercise.Rest,ActiveUpperBodyReview.Exercise.Reach,ActiveUpperBodyReview.Exercise.Curl})
            {
                _lab.TargetExercise=exercise;
                for(var tick=0;tick<100;tick++)
                {
                    Tick();for(var a=0;a<_lab.Shapes.Count;a++)for(var b=a+1;b<_lab.Shapes.Count;b++)
                    {
                        var first=_lab.Shapes[a];var second=_lab.Shapes[b];if(UnityEngine.Physics.GetIgnoreCollision(first,second))continue;
                        if(UnityEngine.Physics.ComputePenetration(first,first.transform.position,first.transform.rotation,second,second.transform.position,second.transform.rotation,out _,out var depth))maximum=Mathf.Max(maximum,depth);
                    }
                }
            }
            File.WriteAllText(Path.Combine(Output,"SelfCollision.txt"),$"maximumNonAdjacentColliderPenetration={maximum:R}");
            Assert.That(maximum,Is.LessThan(.005f));yield return null;
        }
        private void DemoInput(int tick)
        {
            if(tick==60)_lab.TargetExercise=ActiveUpperBodyReview.Exercise.Reach;
            if(tick==150)_lab.TargetExercise=ActiveUpperBodyReview.Exercise.Curl;
            if(tick==240)_lab.TargetExercise=ActiveUpperBodyReview.Exercise.Rest;
            if(tick==300){_lab.SetObstacle(true);_lab.TargetExercise=ActiveUpperBodyReview.Exercise.Reach;}
            if(tick==390)_lab.SetObstacle(false);
            if(tick==435)_lab.PushArm(0,new Vector3(.15f,0,-.4f));
            if(tick==480)_lab.LeftPowered=false;
            if(tick==570)_lab.LeftPowered=true;
        }
        [UnityTest] public IEnumerator FrameSchedulesCannotChangeThePhysicsOrFeedRenderedBonesBack()
        {
            var samples=new List<Vector3[]>();var rows=new List<string>();
            foreach(var fps in new[]{60,15,144})
            {
                Create();var tick=0;var time=0d;var error=0f;
                while(tick<630)
                {
                    time=Math.Min(10.5,time+1d/fps);var due=Math.Min(630,(int)Math.Floor(time*60+1e-8));
                    while(tick<due)
                    {
                        DemoInput(tick);Tick();var points=_lab.Links.Select(l=>l.Body.position).Append(_lab.Actor.Motor.Body.position).ToArray();
                        if(fps==60)samples.Add(points);else for(var i=0;i<points.Length;i++)error=Mathf.Max(error,Vector3.Distance(points[i],samples[tick][i]));tick++;
                    }
                    _lab.Actor.FixedPose.Present((float)Math.Clamp((time-tick/60d)*60,0,1));
                }
                rows.Add($"fps={fps}, maximumBodyPositionDifference={error:R}, steps={_lab.Steps}, captures={_lab.Captures}");
                File.WriteAllLines(Path.Combine(Output,"FrameRates.txt"),rows);Assert.That(error,Is.LessThan(.0001f));
                var op=SceneManager.UnloadSceneAsync(_scene);while(op!=null&&!op.isDone)yield return null;
            }
        }
        [UnityTest] public IEnumerator NativePhysicsLoopCapturesEveryStep()
        {
            var saved=Time.captureDeltaTime;
            try
            {
                Time.captureDeltaTime=Dt;Create(true);_lab.TargetExercise=ActiveUpperBodyReview.Exercise.Reach;
                for(var i=0;i<150;i++)yield return null;
                Assert.That(_lab.Steps,Is.GreaterThan(100));Assert.That(_lab.Captures,Is.EqualTo(_lab.Steps+1));
                Assert.That(_lab.TotalMass,Is.EqualTo(20));Assert.That(_lab.MaximumJointGap,Is.LessThan(.008f));
            }
            finally{Time.captureDeltaTime=saved;}
        }
        [UnityTest] public IEnumerator CaptureActualSkinAndContactAtSixtyFps()
        {
            Create();for(var i=0;i<60;i++)Tick();var folder=Path.Combine(Output,"Frames");Directory.CreateDirectory(folder);
            var view=_lab.View;view.aspect=16f/9;var rt=new RenderTexture(960,540,24);rt.Create();view.targetTexture=rt;
            var image=new Texture2D(960,540,TextureFormat.RGB24,false);var mesh=new Mesh();
            var skin=_lab.Actor.Pose.GetComponentsInChildren<SkinnedMeshRenderer>().Single(s=>s.name.EndsWith("SkinnedBody"));
            var triangles=skin.sharedMesh.triangles;var pairs=CameraGaitReviewTests.Pairs(triangles);var surface=0f;var worst=-1;
            var penetration=0f;var penetrationTick=-1;
            var rows=new List<string>();
            for(var tick=0;tick<630;tick++)
            {
                DemoInput(tick);Tick();skin.BakeMesh(mesh);var angle=CameraGaitReviewTests.Surface(mesh.vertices,triangles,pairs,out var location);
                if(angle>surface){surface=angle;worst=tick;}
                rows.Add($"tick={tick}, exercise={_lab.TargetExercise}, powered={_lab.LeftPowered}, hand={_lab.HandPosition(0):R}, goal={_lab.GoalHand(0):R}, chest={_lab.TrackingError("Chest"):R}, contact={_lab.ObstacleContacts}, skin={angle:R}, gap={_lab.MaximumJointGap:R}");
                if(_lab.Obstacle.gameObject.activeSelf)
                {
                    var bounds=_lab.Obstacle.bounds;
                    foreach(var alpha in new[]{0f,.5f,1f})
                    {
                        _lab.Actor.FixedPose.Present(alpha);
                        foreach(var rendered in _lab.Actor.Pose.GetComponentsInChildren<SkinnedMeshRenderer>())
                        {
                            rendered.BakeMesh(mesh);var matrix=rendered.localToWorldMatrix;
                            foreach(var vertex in mesh.vertices)
                            {
                                var point=matrix.MultiplyPoint3x4(vertex);if(!bounds.Contains(point))continue;
                                var lo=point-bounds.min;var hi=bounds.max-point;var depth=Mathf.Min(lo.x,lo.y,lo.z,hi.x,hi.y,hi.z);
                                if(depth>penetration){penetration=depth;penetrationTick=tick;}
                            }
                        }
                    }
                }
                _lab.Actor.FixedPose.Present(1);yield return null;view.Render();var old=RenderTexture.active;RenderTexture.active=rt;
                image.ReadPixels(new Rect(0,0,960,540),0,0);image.Apply();RenderTexture.active=old;
                File.WriteAllBytes(Path.Combine(folder,$"Frame_{tick:D4}.png"),image.EncodeToPNG());
            }
            File.WriteAllLines(Path.Combine(Output,"CaptureTrace.txt"),rows);File.WriteAllText(Path.Combine(Output,"Skin.txt"),$"maximumSurfaceAngle={surface:R}, worstTick={worst}, maximumJointGap={_lab.MaximumJointGap:R}");
            File.WriteAllText(Path.Combine(Output,"RenderedContact.txt"),$"maximumSkinVertexPenetration={penetration:R}, worstTick={penetrationTick}, interpolationSamples=0/0.5/1");
            view.targetTexture=null;rt.Release();UnityEngine.Object.Destroy(rt);UnityEngine.Object.Destroy(image);UnityEngine.Object.Destroy(mesh);
            Assert.That(surface,Is.LessThan(60));Assert.That(_lab.ObstacleContacts,Is.GreaterThan(0));Assert.That(_lab.MaximumJointGap,Is.LessThan(.008f));
            Assert.That(penetration,Is.LessThan(.004f));
        }
    }
}
