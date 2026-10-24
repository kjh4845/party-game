using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using ProjectHotfix.PhysicsJump;
using ProjectHotfix.Simulation;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace ProjectHotfix.Physics.Tests
{
    public sealed class ArticulatedPunchDownReviewTests
    {
        private Scene _scene;
        private PhysicsJumpReview _attacker, _victim;
        private PunchDownReview _receiver;
        private void Create()
        {
            _scene=SceneManager.CreateScene("Punch Down",new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            _attacker=PhysicsJumpReview.Create(new Vector3(0,.02f,0));_victim=PhysicsJumpReview.Create(new Vector3(0,.02f,.46f));
            foreach(var actor in new[] {_attacker,_victim})
            { SceneManager.MoveGameObjectToScene(actor.gameObject,_scene);actor.ReadKeyboard=false;actor.Motor.ManualStep=true;actor.Motor.Body.interpolation=RigidbodyInterpolation.None;actor.EnableGrabReview(); }
            _victim.gameObject.AddComponent<RagdollRecoveryReview>().ArticulatedTorso=true;
            _receiver=_victim.gameObject.AddComponent<PunchDownReview>();_receiver.Bind(_victim);
            foreach(var skin in _victim.GetComponentsInChildren<SkinnedMeshRenderer>()) skin.sharedMaterial.SetColor("_BaseColor",new Color(.92f,.48f,.16f));
            var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);SceneManager.MoveGameObjectToScene(floor,_scene);floor.transform.position=new Vector3(0,-.1f,0);floor.transform.localScale=new Vector3(30,.2f,30);
            _attacker.Hands.ReviewTargets=new[]{_receiver.Target};
            UnityEngine.Physics.SyncTransforms();_attacker.Hands.SubmitButtons(false,false);_victim.Hands.SubmitButtons(false,false);
        }
        private void Tick()
        {
            var rag=_receiver.Ragdoll;rag.Down.Step(1f/60);rag.Step(1f/60);_receiver.Step();
            foreach(var actor in new[]{_attacker,_victim}) { actor.Motor.Step(1f/60);actor.Hands.Step(1f/60); }
            _scene.GetPhysicsScene().Simulate(1f/60);_attacker.ApplyPresentation(1f/60);
            if(rag.Active) rag.ApplyPose();else _victim.ApplyPresentation(1f/60);
        }
        private void PositionForPunch()
        {
            var body=_attacker.Motor.Body;body.position=_victim.Motor.Body.position+Vector3.back*.46f;body.rotation=Quaternion.identity;
            _attacker.transform.SetPositionAndRotation(body.position,body.rotation);body.linearVelocity=body.angularVelocity=Vector3.zero;UnityEngine.Physics.SyncTransforms();
        }
        [UnityTearDown] public IEnumerator Cleanup()
        { if(!_scene.IsValid()||!_scene.isLoaded) yield break;var op=SceneManager.UnloadSceneAsync(_scene);while(op!=null&&!op.isDone)yield return null; }
        [Serializable] private sealed class Report { public string status;public int[] hitsPerDown;public double[] downDurations;public int recoveries;public bool preservedAcrossTenSeconds;public float moveDistance;public int capturedFrames; }
        [UnityTest] public IEnumerator RealPunchSweepsKeepFiveHitsWhileDownDurationIncreases()
        {
            Create();var rag=_receiver.Ragdoll;
            var output=Path.GetFullPath(Path.Combine(Application.dataPath,"../../.codex_tmp/articulated-torso-20260908/PunchCapture"));Directory.CreateDirectory(output);
            var cameraObject=new GameObject("Camera");SceneManager.MoveGameObjectToScene(cameraObject,_scene);var camera=cameraObject.AddComponent<Camera>();camera.enabled=false;camera.orthographic=true;camera.orthographicSize=.95f;
            var lightObject=new GameObject("Light");SceneManager.MoveGameObjectToScene(lightObject,_scene);var light=lightObject.AddComponent<Light>();light.type=LightType.Directional;light.intensity=2;light.transform.rotation=Quaternion.Euler(45,-35,0);
            var render=new RenderTexture(640,480,24);render.Create();camera.targetTexture=render;var image=new Texture2D(640,480,TextureFormat.RGB24,false);
            var retreatUntil=-1;var nextStrike=90;var release=-1;var hitCount=0;var hitsThisDown=0;uint downCount=0;var counts=new List<int>();var durations=new List<double>();var priorActive=false;var firstHitStep=-1;var captured=0;var captureStart=-1;var preserved=false;var settled=0;var startMove=Vector3.zero;
            for(var step=0;step<5000;step++)
            {
                if(step==retreatUntil) _attacker.Motor.MoveInput=Vector2.zero;
                if(!rag.Active && rag.Recoveries<4 && step>=nextStrike) { _attacker.Motor.MoveInput=Vector2.zero;PositionForPunch();_attacker.Hands.SubmitButtons(true,false);release=step+3;nextStrike=step+70; }
                if(step==release) _attacker.Hands.SubmitButtons(false,false);
                Tick();
                if(_receiver.Target.HitCount>hitCount)
                {
                    Assert.That(_receiver.Target.HitCount-hitCount,Is.EqualTo(1));hitCount=_receiver.Target.HitCount;hitsThisDown++;
                    if(hitCount==1) {firstHitStep=step;nextStrike=step+660;}
                    if(hitCount==4)captureStart=step;
                }
                if(step==firstHitStep+600 && firstHitStep>=0) { Assert.That(_receiver.Accumulation.Hits,Is.EqualTo(1),"Ten idle seconds cannot heal accumulated hits");preserved=true; }
                if(rag.Down.State.DownCount>downCount)
                { counts.Add(hitsThisDown);durations.Add(rag.Down.State.AppliedDurationSeconds);hitsThisDown=0;downCount=rag.Down.State.DownCount;Assert.That(_receiver.Accumulation.Hits,Is.Zero);nextStrike=int.MaxValue; _attacker.Motor.MoveInput=Vector2.down;retreatUntil=step+90; }
                if(priorActive&&!rag.Active)nextStrike=step+45;priorActive=rag.Active;
                if(captureStart>=0&&step<captureStart+420&&(step-captureStart)%3==0)
                {
                    var center=(_attacker.transform.position+_victim.transform.position)*.5f+Vector3.up*.45f;camera.transform.position=center+new Vector3(2.5f,1.0f,2.5f);camera.transform.LookAt(center);
                    yield return null;camera.Render();var old=RenderTexture.active;RenderTexture.active=render;image.ReadPixels(new Rect(0,0,640,480),0,0);image.Apply();RenderTexture.active=old;
                    File.WriteAllBytes(Path.Combine(output,$"Frame_{captured++:D3}.png"),image.EncodeToPNG());
                }
                if(rag.Recoveries==4)
                {
                    if(settled==0)startMove=_victim.Motor.Body.position;
                    _victim.Motor.MoveInput=Vector2.right;settled++;if(settled>=45)break;
                }
            }
            var distance=Vector3.Distance(startMove,_victim.Motor.Body.position);
            File.WriteAllText(Path.Combine(output,"Report.json"),JsonUtility.ToJson(new Report{status="OBSERVED",hitsPerDown=counts.ToArray(),downDurations=durations.ToArray(),recoveries=rag.Recoveries,preservedAcrossTenSeconds=preserved,moveDistance=distance,capturedFrames=captured},true));
            Assert.That(counts,Is.EqualTo(new[]{5,5,5,5}),$"phase={rag.Down.State.Phase}, age={rag.GetUpAge}, angle={rag.ReadyAngle}, distance={rag.ReadyDistance}, root={_victim.Motor.Body.position}, attacker={_attacker.Motor.Body.position}");Assert.That(durations,Is.EqualTo(new[]{1.5,2.0,2.5,3.0}));Assert.That(rag.Recoveries,Is.EqualTo(4),$"phase={rag.Down.State.Phase}, age={rag.GetUpAge}, angle={rag.ReadyAngle}, distance={rag.ReadyDistance}, root={_victim.Motor.Body.position}, attacker={_attacker.Motor.Body.position}");Assert.That(preserved,Is.True);Assert.That(distance,Is.GreaterThan(.2f));
            Assert.That(rag.MaximumJointGap,Is.LessThan(.015f),$"{rag.WorstJoint} {rag.WorstGapPhase} age={rag.WorstGapGetUpAge} down={rag.WorstGapDownCount} speed={rag.MaximumSpeed}");Assert.That(rag.MaximumSpeed,Is.LessThan(8f));
            _receiver.Target.ResetReview();Assert.That(rag.Down.State.DownCount,Is.Zero);Assert.That(_receiver.Accumulation.Hits,Is.Zero);Assert.That(_receiver.RequiredHits,Is.EqualTo(5));
            File.WriteAllText(Path.Combine(output,"Report.json"),JsonUtility.ToJson(new Report{status="PASS",hitsPerDown=counts.ToArray(),downDurations=durations.ToArray(),recoveries=rag.Recoveries,preservedAcrossTenSeconds=preserved,moveDistance=distance,capturedFrames=captured},true));
            camera.targetTexture=null;render.Release();UnityEngine.Object.Destroy(render);UnityEngine.Object.Destroy(image);
        }
        [UnityTest] public IEnumerator DownHitsOnlyImpulse_ProxyOwnershipAndInvalidSourcesAreSafe()
        {
            Create();for(var i=0;i<90;i++)Tick();
            var rag=_receiver.Ragdoll;rag.BeginDown(Vector3.zero,Vector3.zero);_receiver.Synchronize();
            var proxies=_scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Collider>()).Where(c=>c.enabled&&c.GetComponentInParent<StrikeReviewHurtbox>()!=null).ToArray();
            Assert.That(proxies.Length,Is.GreaterThan(8));foreach(var c in proxies)Assert.That(StrikeReviewHurtbox.Resolve(c),Is.EqualTo(_receiver.Target));
            // Exercise the receiver at a real active action; this fixture isolates
            // post-Down policy, while the other test exercises actual sweep hits.
            _attacker.Hands.SubmitButtons(true,false);for(var i=0;i<3;i++)Tick();_attacker.Hands.SubmitButtons(false,false);for(var i=0;i<7;i++)Tick();
            var action=_attacker.Hands.State.ActionId(0);Assert.That(_attacker.Hands.State.Active(0),Is.True);
            var seconds=rag.Down.State.RemainingSeconds;var episode=rag.Down.State.EpisodeId;var count=rag.Down.State.DownCount;
            var hit=proxies[0];var body=hit.attachedRigidbody;var before=body.linearVelocity;
            Assert.That(_receiver.Confirm(_attacker.Hands,action,hit,Vector3.forward*10,out var impulse),Is.True);Assert.That(impulse.magnitude,Is.GreaterThan(0));
            Assert.That(rag.Down.State.RemainingSeconds,Is.EqualTo(seconds));Assert.That(rag.Down.State.EpisodeId,Is.EqualTo(episode));Assert.That(rag.Down.State.DownCount,Is.EqualTo(count));Assert.That(_receiver.Accumulation.Hits,Is.Zero);
            _scene.GetPhysicsScene().Simulate(1f/60);Assert.That(Vector3.Dot(body.linearVelocity-before,impulse.normalized),Is.GreaterThan(.001f),"Accepted Down hit moves the physical body");
            Assert.That(_receiver.Confirm(_attacker.Hands,action,hit,Vector3.forward*10,out _),Is.False,"duplicate");
            _attacker.Motor.AuthorityEnabled=false;Assert.That(_receiver.Confirm(_attacker.Hands,action+1,hit,Vector3.forward*10,out _),Is.False);
            _attacker.Motor.AuthorityEnabled=true;Assert.That(_receiver.Confirm(_attacker.Hands,action+1,hit,Vector3.forward*10,out _),Is.False,"fabricated action");
            for(var i=0;i<600;i++)Tick();Assert.That(rag.Down.State.DownCount,Is.EqualTo(1));Assert.That(_receiver.Accumulation.Hits,Is.Zero,"floor/limb collision cannot add damage");
            yield return null;
        }
        [UnityTest] public IEnumerator WallHoldReleaseThrowAndDisabledAuthorityCannotCreateDamage()
        {
            Create();for(var i=0;i<90;i++)Tick();PositionForPunch();
            var wall=GameObject.CreatePrimitive(PrimitiveType.Cube);SceneManager.MoveGameObjectToScene(wall,_scene);wall.transform.position=(_attacker.transform.position+_victim.transform.position)*.5f+Vector3.up*.5f;wall.transform.localScale=new Vector3(.8f,1,.05f);UnityEngine.Physics.SyncTransforms();
            _attacker.Hands.SubmitButtons(true,false);for(var i=0;i<3;i++)Tick();_attacker.Hands.SubmitButtons(false,false);for(var i=0;i<60;i++)Tick();
            Assert.That(_receiver.Accumulation.Hits,Is.Zero,"wall occludes a real sweep");
            wall.SetActive(false);PositionForPunch();
            _attacker.Hands.SubmitButtons(true,false);for(var i=0;i<40;i++)Tick();
            Assert.That(_attacker.Hands.Grabs.IsHolding(0),Is.True);
            _attacker.Hands.SubmitButtons(false,false);for(var i=0;i<120;i++)Tick();
            Assert.That(_attacker.Hands.Grabs.ThrowCount,Is.EqualTo(1));Assert.That(_receiver.Accumulation.Hits,Is.Zero,"hold/release throw is not a Punch");
            Assert.That(_receiver.Ragdoll.Down.State.DownCount,Is.Zero,"throw and floor collisions cannot automatically down");
            _receiver.Target.ResetReview();PositionForPunch();_attacker.Motor.AuthorityEnabled=false;
            _attacker.Hands.SubmitButtons(true,false);for(var i=0;i<3;i++)Tick();_attacker.Hands.SubmitButtons(false,false);for(var i=0;i<60;i++)Tick();
            Assert.That(_receiver.Accumulation.Hits,Is.Zero);Assert.That(_receiver.Target.HitCount,Is.Zero);
            yield return null;
        }
        [UnityTest] public IEnumerator NonDownRecoveryCannotClearAccumulationOrGrantDownHitImmunity()
        {
            Create();for(var i=0;i<90;i++)Tick();var state=_receiver.Ragdoll.Down.State;
            _receiver.Accumulation.Receive(state.RoundGeneration,123,1,false);
            Assert.That(state.TryBeginDropkickRecovery(state.RoundGeneration,1),Is.True);_receiver.Synchronize();
            Assert.That(_receiver.Accumulation.Hits,Is.EqualTo(1));Assert.That(state.DownCount,Is.Zero);
            // State-boundary fixture only; it does not claim implemented air combat.
            _attacker.Motor.Body.position+=Vector3.back*3;_attacker.transform.position=_attacker.Motor.Body.position;UnityEngine.Physics.SyncTransforms();
            _attacker.Hands.SubmitButtons(true,false);for(var i=0;i<3;i++)Tick();_attacker.Hands.SubmitButtons(false,false);for(var i=0;i<7;i++)Tick();
            Assert.That(_receiver.Confirm(_attacker.Hands,_attacker.Hands.State.ActionId(0),_victim.GetComponent<CapsuleCollider>(),Vector3.forward,out _),Is.True);
            Assert.That(_receiver.Accumulation.Hits,Is.EqualTo(2));Assert.That(state.Kind,Is.EqualTo(CharacterRecoveryKind.DropkickRecovery));Assert.That(state.DownCount,Is.Zero);
            yield return null;
        }
        [UnityTest] public IEnumerator ThresholdTransitionWaitsForMotorBoundaryAndResetCancelsIt()
        {
            Create();for(var i=0;i<90;i++)Tick();
            _attacker.Motor.Body.position+=Vector3.back*3;_attacker.transform.position=_attacker.Motor.Body.position;UnityEngine.Physics.SyncTransforms();
            _attacker.Hands.SubmitButtons(true,false);for(var i=0;i<3;i++)Tick();_attacker.Hands.SubmitButtons(false,false);for(var i=0;i<7;i++)Tick();
            var state=_receiver.Ragdoll.Down.State;var action=_attacker.Hands.State.ActionId(0);
            for(ulong i=1;i<=4;i++)_receiver.Accumulation.Receive(state.RoundGeneration,123,i,false);
            Assert.That(_receiver.Confirm(_attacker.Hands,action,_victim.GetComponent<CapsuleCollider>(),Vector3.forward*20,out _),Is.True);
            Assert.That(_receiver.Ragdoll.Active,Is.False);Assert.That(_victim.Motor.Body.mass,Is.EqualTo(20),"Mass cannot change under queued motor forces");
            _receiver.Target.ResetReview();_receiver.Step();Assert.That(_receiver.Ragdoll.Active,Is.False);Assert.That(state.DownCount,Is.Zero);Assert.That(_receiver.Accumulation.Hits,Is.Zero);
            for(ulong i=1;i<=4;i++)_receiver.Accumulation.Receive(state.RoundGeneration,123,i,false);
            Assert.That(_receiver.Confirm(_attacker.Hands,action,_victim.GetComponent<CapsuleCollider>(),Vector3.forward*20,out _),Is.True);
            _receiver.Step();Assert.That(_receiver.Ragdoll.Active,Is.True);Assert.That(state.DownCount,Is.EqualTo(1));Assert.That(_receiver.Accumulation.Hits,Is.Zero);
            _receiver.Step();Assert.That(state.DownCount,Is.EqualTo(1));Assert.That(_receiver.Target.LastImpulse,Is.GreaterThan(0));
            yield return null;
        }
    }
}
