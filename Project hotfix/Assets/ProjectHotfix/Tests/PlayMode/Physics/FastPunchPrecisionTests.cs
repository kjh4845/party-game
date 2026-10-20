using System;
using System.IO;
using System.Linq;
using System.Collections;
using NUnit.Framework;
using ProjectHotfix.PhysicsJump;
using ProjectHotfix.Simulation;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace ProjectHotfix.Physics.Tests
{
    public sealed class FastPunchPrecisionTests
    {
        private Scene _scene;
        [UnityTearDown] public IEnumerator Cleanup()
        { if(!_scene.IsValid()||!_scene.isLoaded)yield break;var op=SceneManager.UnloadSceneAsync(_scene);while(op!=null&&!op.isDone)yield return null; }
        private PhysicsJumpReview Actor(Vector3 point,double rate)
        {
            var actor=PhysicsJumpReview.Create(point);SceneManager.MoveGameObjectToScene(actor.gameObject,_scene);
            actor.ReadKeyboard=false;actor.Motor.ManualStep=true;actor.Motor.Body.interpolation=RigidbodyInterpolation.None;
            actor.EnableHandStrikeReview();actor.Hands.ConfigurePunchTiming(rate);return actor;
        }
        [UnityTest] public IEnumerator SerializedReviewRebindsPoseCachesAndRetainsFastRate()
        {
            _scene=SceneManager.CreateScene("Reload-style review",new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            var source=Actor(new Vector3(3,.02f,0),1.4);source.EnableGrabReview();source.Hands.PreciseHits=true;
            var clone=UnityEngine.Object.Instantiate(source.gameObject,new Vector3(0,.02f,0),Quaternion.identity).GetComponent<PhysicsJumpReview>();
            SceneManager.MoveGameObjectToScene(clone.gameObject,_scene);
            var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);SceneManager.MoveGameObjectToScene(floor,_scene);floor.transform.position=Vector3.down*.1f;floor.transform.localScale=new Vector3(12,.2f,12);
            yield return null;
            Assert.That(clone.Hands.State.PunchPlaybackRate,Is.EqualTo(1.4));Assert.That(clone.Hands.PreciseHits,Is.True);
            Assert.That(float.IsNaN(clone.GrabPose.Terminal(0).x),Is.False);
            for(var i=0;i<90;i++){clone.Motor.Step(1f/60);_scene.GetPhysicsScene().Simulate(1f/60);}
            clone.Hands.SubmitButtons(false,false);clone.Hands.SubmitButtons(false,false,true,false,1f/60);
            for(var i=0;i<8;i++){clone.Hands.Step(1f/60);clone.ApplyPresentation(1f/60);}
            Assert.That(clone.Punches.IsActive(0),Is.True);
            var elbow=clone.Pose.GetComponentsInChildren<Transform>().Single(t=>t.name=="Forearm_L");
            Assert.That(Quaternion.Angle(elbow.localRotation,clone.PunchLibrary.clips[0].Sample((float)clone.Hands.State.SourcePunchAge(0),2)),Is.LessThan(.06f));
        }
        [UnityTest] public IEnumerator DedicatedZonesDistinguishHeadBodyMissesWallsAndSimulationRates()
        {
            foreach(var hz in new[]{30,60,120})foreach(var kind in new[]{"Body","Head","Arm","Leg","LateralMiss","OutOfReach","LateTarget","Wall","UnrelatedTrigger"})
            {
                _scene=SceneManager.CreateScene(kind+hz,new CreateSceneParameters(LocalPhysicsMode.Physics3D));
                var height=kind=="Head" ? .23f : 0f;var actor=Actor(new Vector3(0,height+.02f,0),1.4);actor.Hands.PreciseHits=true;
                var x=kind=="LateralMiss" ? .10f : kind=="Arm" ? .02f : kind=="Leg" ? -.259f : -.179f;
                var target=Actor(new Vector3(x,kind=="Leg" ? .58f : 0,kind=="OutOfReach"||kind=="LateTarget" ? .70f : kind=="Arm" ? .36f : .4f),1);
                target.Motor.Body.useGravity=false;target.Motor.Body.constraints=RigidbodyConstraints.FreezeAll;
                var receiver=target.gameObject.AddComponent<PunchDownReview>();receiver.RegionPointCandidate=true;receiver.Bind(target);
                var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);SceneManager.MoveGameObjectToScene(floor,_scene);floor.transform.position=new Vector3(0,height-.1f,0);floor.transform.localScale=new Vector3(3,.2f,.30f);
                if(kind=="Wall"||kind=="UnrelatedTrigger")
                {
                    var obstacle=GameObject.CreatePrimitive(PrimitiveType.Cube);SceneManager.MoveGameObjectToScene(obstacle,_scene);obstacle.transform.position=new Vector3(0,.6f,.20f);obstacle.transform.localScale=new Vector3(.8f,1.2f,.015f);obstacle.GetComponent<Collider>().isTrigger=kind=="UnrelatedTrigger";
                }
                UnityEngine.Physics.SyncTransforms();var dt=1f/hz;
                for(var step=0;step<hz;step++){actor.Motor.Step(dt);_scene.GetPhysicsScene().Simulate(dt);}
                Assert.That(actor.Motor.Grounded,Is.True,kind+hz);
                actor.Hands.SubmitButtons(false,false);
                actor.Hands.SubmitButtons(false,false,true,false,dt);
                for(var step=0;step<hz;step++)
                {
                    if(kind=="LateTarget"&&step==Mathf.CeilToInt(.17f*hz))target.Motor.Body.position=new Vector3(x,0,.4f);
                    actor.Hands.Step(dt);_scene.GetPhysicsScene().Simulate(dt);
                }
                var expected=kind=="Body"||kind=="Head"||kind=="Arm"||kind=="Leg"||kind=="UnrelatedTrigger" ? 1 : 0;
                Assert.That(receiver.Target.HitCount,Is.EqualTo(expected),kind+hz);
                Assert.That(receiver.Accumulation.Points,Is.EqualTo(expected*(kind=="Head" ? 2 : 1)),kind+hz);
                if(kind=="Head")Assert.That(receiver.LastHitRegion,Is.EqualTo(PunchHitRegion.Head));
                if(kind=="Arm")StringAssert.Contains("Arm",receiver.LastHitPart);
                if(kind=="Leg")Assert.That(receiver.LastHitPart.StartsWith("Thigh")||receiver.LastHitPart.StartsWith("Calf")||receiver.LastHitPart.StartsWith("Foot"),Is.True,receiver.LastHitPart);
                Assert.That(actor.Hands.QueryOverflowCount,Is.Zero);
                var op=SceneManager.UnloadSceneAsync(_scene);while(op!=null&&!op.isDone)yield return null;
            }
        }
        [UnityTest] public IEnumerator QueryRigFollowsMotionWithoutReadingRenderedBones()
        {
            _scene=SceneManager.CreateScene("Independent hit pose",new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            var actor=Actor(new Vector3(0,.02f,0),1.4);actor.Hands.PreciseHits=true;
            var receiver=actor.gameObject.AddComponent<PunchDownReview>();receiver.RegionPointCandidate=true;receiver.Bind(actor);
            var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);SceneManager.MoveGameObjectToScene(floor,_scene);floor.transform.position=Vector3.down*.1f;floor.transform.localScale=new Vector3(12,.2f,12);
            var rig=actor.GetComponent<PunchHitRig>();var nodes=actor.Pose.GetComponentsInChildren<Transform>();var maxError=0f;var armTravel=0f;Vector3 initial=default;
            for(var step=0;step<180;step++)
            {
                actor.Motor.MoveInput=step>=45 ? Vector2.up : Vector2.zero;actor.Motor.Step(1f/60);
                actor.Hands.SubmitButtons(false,false,step==80,false,1f/60);actor.Hands.Step(1f/60);
                _scene.GetPhysicsScene().Simulate(1f/60);actor.ApplyPresentation(1f/60);
                foreach(var name in new[]{"Head","Forearm_L","HandLogical_L","Calf_L","Foot_R"})
                {
                    var visual=actor.transform.InverseTransformPoint(nodes.Single(t=>t.name==name).position);
                    var root=rig.Bone("Root").parent;
                    var hit=root.InverseTransformPoint(rig.Bone(name).position);
                    maxError=Mathf.Max(maxError,Vector3.Distance(visual,hit));
                }
                var hand=rig.Bone("Root").parent.InverseTransformPoint(rig.Bone("HandLogical_L").position);
                if(step==40)initial=hand;if(step>80)armTravel=Mathf.Max(armTravel,Vector3.Distance(initial,hand));
            }
            Assert.That(maxError,Is.LessThan(.03f),"fixed-step query vs render pose offset");Assert.That(armTravel,Is.GreaterThan(.20f));
            var before=rig.Bone("HandLogical_L").position;nodes.Single(t=>t.name=="HandLogical_L").position+=Vector3.right*3;
            PunchHitRig.SyncScene(_scene,1f/60);Assert.That(Vector3.Distance(before,rig.Bone("HandLogical_L").position),Is.LessThan(.0001f),"rendered bone edits must not move hit geometry");
            yield return null;
        }
        [UnityTest] public IEnumerator TenBodySweepsStartDownAndLowerNextThreshold()
        {
            _scene=SceneManager.CreateScene("Point threshold integration",new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            var actor=Actor(new Vector3(0,.02f,0),1.4);actor.Hands.PreciseHits=true;
            var target=Actor(new Vector3(-.179f,0,.4f),1);target.Motor.Body.useGravity=false;target.Motor.Body.constraints=RigidbodyConstraints.FreezeAll;
            target.gameObject.AddComponent<RagdollRecoveryReview>().ArticulatedTorso=true;
            var receiver=target.gameObject.AddComponent<PunchDownReview>();receiver.RegionPointCandidate=true;receiver.Bind(target);
            var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);SceneManager.MoveGameObjectToScene(floor,_scene);floor.transform.position=Vector3.down*.1f;floor.transform.localScale=new Vector3(12,.2f,12);
            UnityEngine.Physics.SyncTransforms();
            for(var step=0;step<500&&receiver.Ragdoll.Down.State.DownCount==0;step++)
            {
                receiver.Ragdoll.Down.Step(1f/60);receiver.Ragdoll.Step(1f/60);receiver.Step();actor.Motor.Step(1f/60);
                actor.Hands.SubmitButtons(false,false,step>=90&&step%34==0,false,1f/60);actor.Hands.Step(1f/60);_scene.GetPhysicsScene().Simulate(1f/60);
            }
            Assert.That(receiver.Target.HitCount,Is.EqualTo(10));Assert.That(receiver.Ragdoll.Down.State.DownCount,Is.EqualTo(1));
            Assert.That(receiver.Accumulation.Points,Is.Zero);Assert.That(receiver.RequiredPoints,Is.EqualTo(9));Assert.That(receiver.Ragdoll.Down.State.AppliedDurationSeconds,Is.EqualTo(1.5));
            Assert.That(receiver.Ragdoll.Active,Is.True);Assert.That(receiver.LastHitRegion,Is.EqualTo(PunchHitRegion.Body));
            receiver.ResetReview();Assert.That(receiver.RequiredPoints,Is.EqualTo(10));Assert.That(receiver.Ragdoll.Down.State.DownCount,Is.Zero);
            yield return null;
        }
        [UnityTest] public IEnumerator SpeedCandidatePreservesSourcePoseAndAllowsEarlierNextPunch()
        {
            _scene=SceneManager.CreateScene("Fast punch comparison",new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            var actors=new[]{Actor(new Vector3(-.65f,.02f,0),1),Actor(new Vector3(.65f,.02f,0),1.4)};
            foreach(var skin in actors[1].GetComponentsInChildren<SkinnedMeshRenderer>())skin.sharedMaterial.SetColor("_BaseColor",new Color(.92f,.48f,.16f));
            var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);SceneManager.MoveGameObjectToScene(floor,_scene);floor.transform.position=Vector3.down*.1f;floor.transform.localScale=new Vector3(12,.2f,12);
            var cameraObject=new GameObject("Camera");SceneManager.MoveGameObjectToScene(cameraObject,_scene);var camera=cameraObject.AddComponent<Camera>();camera.enabled=false;camera.orthographic=true;camera.orthographicSize=.94f;
            camera.transform.position=new Vector3(1.1f,1.3f,3.8f);camera.transform.LookAt(new Vector3(0,.48f,.10f));
            var lightObject=new GameObject("Light");SceneManager.MoveGameObjectToScene(lightObject,_scene);var light=lightObject.AddComponent<Light>();light.type=LightType.Directional;light.intensity=2;light.transform.rotation=Quaternion.Euler(45,-35,0);
            var output=Path.GetFullPath(Path.Combine(Application.dataPath,"../../.codex_tmp/punch-precision-20260909/SpeedCapture"));Directory.CreateDirectory(output);
            var rt=new RenderTexture(800,600,24);rt.Create();camera.targetTexture=rt;var image=new Texture2D(800,600,TextureFormat.RGB24,false);
            var maxError=0f;var maxLegacyTipOffset=0f;var elapsed=new[]{0,0};var lastCounts=new[]{0,0};
            for(var step=0;step<300;step++)
            {
                for(var i=0;i<2;i++)
                {
                    var a=actors[i];a.Motor.Step(1f/60);
                    a.Hands.SubmitButtons(false,false,step>=60&&step<270&&a.Hands.State.Phase(0)==HandIntentPhase.Idle,false,1f/60);
                    a.Hands.Step(1f/60);
                }
                _scene.GetPhysicsScene().Simulate(1f/60);
                for(var i=0;i<2;i++)
                {
                    var a=actors[i];a.ApplyPresentation(1f/60);
                    if(a.Hands.State.Phase(0)!=HandIntentPhase.Punch)continue;
                    elapsed[i]++;var sourceTime=(float)a.Hands.State.SourcePunchAge(0);
                    if(sourceTime<.04f)continue;
                    var clip=a.PunchLibrary.clips[0];var nodes=a.GetComponentsInChildren<Transform>();
                    for(var bone=0;bone<4;bone++)maxError=Mathf.Max(maxError,Quaternion.Angle(nodes.Single(t=>t.name==clip.bones[bone]).localRotation,clip.Sample(sourceTime,bone)));
                    if(a.Hands.State.Active(0))
                    {
                        var hand=nodes.Single(t=>t.name=="HandLogical_L");var tip=hand.TransformPoint(Vector3.back*.06f);
                        maxLegacyTipOffset=Mathf.Max(maxLegacyTipOffset,Vector3.Distance(a.Hands.StrikePoint(0,a.Hands.State.PunchAge(0)),tip));
                    }
                }
                if(step>=45&&step<285&&step%2==0)
                {
                    yield return null;camera.Render();var previous=RenderTexture.active;RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,800,600),0,0);image.Apply();RenderTexture.active=previous;
                    File.WriteAllBytes(Path.Combine(output,$"Frame_{(step-46)/2:D3}.png"),image.EncodeToPNG());
                }
            }
            Assert.That(maxError,Is.LessThan(.06f));Assert.That(actors[1].Hands.State.PunchCount,Is.GreaterThan(actors[0].Hands.State.PunchCount));
            Assert.That(maxLegacyTipOffset,Is.GreaterThan(.04f),"Audit: existing fixed hit line differs from the visible fist");
            File.WriteAllText(Path.Combine(output,"Report.json"),$"{{\"status\":\"PASS\",\"oldSeconds\":0.7,\"candidateSeconds\":0.5,\"oldPunches\":{actors[0].Hands.State.PunchCount},\"candidatePunches\":{actors[1].Hands.State.PunchCount},\"maximumSourceAngleError\":{maxError.ToString("R",System.Globalization.CultureInfo.InvariantCulture)},\"maximumLegacyTipOffsetMeters\":{maxLegacyTipOffset.ToString("R",System.Globalization.CultureInfo.InvariantCulture)}}}");
            camera.targetTexture=null;rt.Release();UnityEngine.Object.Destroy(rt);UnityEngine.Object.Destroy(image);
        }
    }
}
