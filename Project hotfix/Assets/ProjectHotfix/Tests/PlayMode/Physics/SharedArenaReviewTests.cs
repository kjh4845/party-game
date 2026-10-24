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
    public sealed class SharedArenaReviewTests
    {
        private Scene _scene;
        private SharedArenaReview _arena;
        private string Output=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../.codex_tmp/shared-arena-20260909"));
        [UnityTearDown] public IEnumerator Cleanup(){if(!_scene.IsValid()||!_scene.isLoaded)yield break;var op=SceneManager.UnloadSceneAsync(_scene);while(op!=null&&!op.isDone)yield return null;}
        private void Create(int count,float? movementScale=null)
        {
            _scene=SceneManager.CreateScene("Shared arena "+count,new CreateSceneParameters(LocalPhysicsMode.Physics3D));_arena=movementScale.HasValue ? SharedArenaReview.Create(_scene,count,movementScale:movementScale.Value) : SharedArenaReview.Create(_scene,count);
            _arena.ReadKeyboard=false;_arena.ShowHelp=false;_arena.SharedCamera.ManualStep=true;_arena.SharedCamera.View.enabled=false;
            foreach(var actor in _arena.Actors){actor.Motor.ManualStep=true;actor.Motor.Body.interpolation=RigidbodyInterpolation.None;}
            _arena.InitializeParticipants();Directory.CreateDirectory(Output);
        }
        private void Tick()
        {
            foreach(var actor in _arena.Actors){var r=actor.GetComponent<PunchDownReview>();r.Ragdoll.Down.Step(1f/60);r.Ragdoll.Step(1f/60);r.Step();}
            foreach(var actor in _arena.Actors)actor.Motor.Step(1f/60);
            foreach(var actor in _arena.Actors)actor.Hands.Step(1f/60);
            _scene.GetPhysicsScene().Simulate(1f/60);
            foreach(var actor in _arena.Actors){var r=actor.GetComponent<RagdollRecoveryReview>();if(r.Active)r.ApplyPose();else actor.ApplyPresentation(1f/60);}
            _arena.SharedCamera.SampleAuthority();_arena.SharedCamera.Present(1f/60);
        }
        private void AssertFramed(float margin=.149f)
        {
            var camera=_arena.SharedCamera;Assert.That(camera.CapacityExceeded,Is.False);
            for(var i=0;i<_arena.Actors.Length;i++)if(camera.Included[i])
            {
                var center=_arena.Actors[i].Motor.Body.position+camera.SubjectCenter;
                for(var x=-1;x<=1;x+=2)for(var y=-1;y<=1;y+=2)for(var z=-1;z<=1;z+=2)
                {
                    var point=camera.View.WorldToViewportPoint(center+Vector3.Scale(camera.SubjectExtents,new Vector3(x,y,z)));
                    Assert.That(point.z,Is.GreaterThan(camera.View.nearClipPlane));Assert.That(point.x,Is.InRange(margin,1-margin));Assert.That(point.y,Is.InRange(margin,1-margin));
                }
            }
        }
        [UnityTest] public IEnumerator TwoThreeFourActorsFitAllThreeAspectRatiosFromAuthorityRoots()
        {
            var rows=new List<string>();
            foreach(var count in new[]{2,3,4})
            {
                Create(count);var positions=new[]{new Vector3(-3.4f,.02f,-2.4f),new Vector3(3.4f,.27f,2.4f),new Vector3(-3.4f,.02f,2.4f),new Vector3(3.4f,.02f,-2.4f)};
                for(var i=0;i<count;i++){_arena.Actors[i].transform.position=positions[i];_arena.Actors[i].Motor.Body.position=positions[i];}UnityEngine.Physics.SyncTransforms();
                for(var i=0;i<count;i++)Assert.That(Vector3.Distance(_arena.Actors[i].Motor.Body.position,positions[i]),Is.LessThan(.00001f));
                foreach(var aspect in new[]{16f/9,16f/10,21f/9})
                {
                    var c=_arena.SharedCamera;c.View.aspect=aspect;for(var step=0;step<90;step++){c.SampleAuthority();c.Present(1f/60);}AssertFramed();
                    rows.Add($"{{\"players\":{count},\"aspect\":{aspect.ToString("R",System.Globalization.CultureInfo.InvariantCulture)},\"dolly\":{c.Dolly.ToString("R",System.Globalization.CultureInfo.InvariantCulture)}}}");
                    var rt=new RenderTexture(Mathf.RoundToInt(450*aspect),450,24);rt.Create();c.View.targetTexture=rt;yield return null;c.View.Render();
                    var previous=RenderTexture.active;RenderTexture.active=rt;var image=new Texture2D(rt.width,rt.height,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);image.Apply();RenderTexture.active=previous;
                    File.WriteAllBytes(Path.Combine(Output,$"Framing_{count}_{Mathf.RoundToInt(aspect*1000)}.png"),image.EncodeToPNG());c.View.targetTexture=null;rt.Release();UnityEngine.Object.Destroy(rt);UnityEngine.Object.Destroy(image);
                }
                var bounds=_arena.SharedCamera.AuthorityBounds;var target=_arena.SharedCamera.TargetFocus;
                var actor=_arena.Actors[0];foreach(var skin in actor.Pose.GetComponentsInChildren<SkinnedMeshRenderer>())skin.localBounds=new Bounds(Vector3.one*100,Vector3.one*1000);
                var proxy=_scene.GetRootGameObjects().Single(g=>g.GetComponent<StrikeReviewHurtbox>()?.ActorRoot==actor.transform&&g.name=="RagdollCandidatePhysics");proxy.transform.position+=Vector3.one*100;
                actor.Motor.AuthorityEnabled=false;_arena.SetMenu(true);_arena.SharedCamera.SampleAuthority();
                Assert.That(_arena.SharedCamera.SubjectCount,Is.EqualTo(count));Assert.That(_arena.SharedCamera.AuthorityBounds,Is.EqualTo(bounds));Assert.That(_arena.SharedCamera.TargetFocus,Is.EqualTo(target));
                var oldFocus=_arena.SharedCamera.Focus;var oldDolly=_arena.SharedCamera.Dolly;_arena.SharedCamera.Included[0]=false;_arena.SharedCamera.SampleAuthority();_arena.SharedCamera.Present(1f/60);
                Assert.That(_arena.SharedCamera.SubjectCount,Is.EqualTo(count-1));Assert.That(Vector3.Distance(oldFocus,_arena.SharedCamera.Focus),Is.LessThanOrEqualTo(.10001f));Assert.That(oldDolly-_arena.SharedCamera.Dolly,Is.LessThanOrEqualTo(.08334f));
                for(var i=0;i<count;i++)_arena.SharedCamera.Included[i]=false;oldFocus=_arena.SharedCamera.Focus;_arena.SharedCamera.SampleAuthority();_arena.SharedCamera.Present(1f/60);Assert.That(_arena.SharedCamera.Focus,Is.EqualTo(oldFocus));
                _arena.SharedCamera.Included[0]=true;_arena.SharedCamera.Included[1]=true;
                _arena.Actors[0].transform.position=new Vector3(-50,0,0);_arena.Actors[0].Motor.Body.position=_arena.Actors[0].transform.position;
                _arena.Actors[1].transform.position=new Vector3(50,0,0);_arena.Actors[1].Motor.Body.position=_arena.Actors[1].transform.position;
                _arena.SharedCamera.SampleAuthority();_arena.SharedCamera.Present(1f/60);Assert.That(_arena.SharedCamera.CapacityExceeded,Is.True);Assert.That(_arena.SharedCamera.Dolly,Is.EqualTo(15));
                var op=SceneManager.UnloadSceneAsync(_scene);while(op!=null&&!op.isDone)yield return null;
            }
            File.WriteAllText(Path.Combine(Output,"Framing.json"),"{\"status\":\"PASS\",\"cases\":["+string.Join(",",rows)+"]}");
        }
        [UnityTest] public IEnumerator PlayerSwitchMenuAndResetCannotThrowOrReplayHeldClicks()
        {
            Create(2);var spawn=_arena.Actors.Select(a=>a.Motor.Body.position).ToArray();
            for(var i=0;i<2;i++){var p=new Vector3(0,.02f,i*.4f);_arena.Actors[i].transform.SetPositionAndRotation(p,Quaternion.identity);_arena.Actors[i].Motor.Body.position=p;_arena.Actors[i].Motor.Body.rotation=Quaternion.identity;}UnityEngine.Physics.SyncTransforms();
            for(var step=0;step<90;step++)Tick();
            _arena.SharedCamera.Yaw=90;var rotation=_arena.Actors[0].Motor.Body.rotation;_arena.SubmitLocal(Vector2.up,true,false,false,false);
            Assert.That(_arena.Actors[0].Motor.MoveInput.x,Is.EqualTo(1).Within(.00001));Assert.That(_arena.Actors[0].Motor.MoveInput.y,Is.EqualTo(0).Within(.00001));Assert.That(_arena.Actors[0].Motor.Body.rotation,Is.EqualTo(rotation));
            _arena.SubmitLocal(Vector2.zero,false,false,true,false);for(var step=0;step<30;step++)Tick();
            var old=_arena.Actors[0];Assert.That(old.Hands.Grabs.IsHolding(0),Is.True,$"phase={old.Hands.State.Phase(0)} armed={old.Hands.State.Armed} reason={old.Hands.Grabs.LastReleaseReason} acquisitions={old.Hands.Grabs.Acquisitions} owner={old.Motor.Body.position} target={_arena.Actors[1].Motor.Body.position} forward={old.transform.forward}");
            _arena.SelectPlayer(1);Assert.That(old.Hands.Grabs.IsHolding(0),Is.False);Assert.That(old.Hands.Grabs.ThrowCount,Is.Zero);Assert.That(old.Motor.MoveInput,Is.EqualTo(Vector2.zero));
            _arena.SubmitLocal(Vector2.zero,false,false,true,false);for(var step=0;step<20;step++)Tick();Assert.That(_arena.Actors[1].Hands.State.PunchCount,Is.Zero);
            _arena.SubmitLocal(Vector2.zero,false,false,false,false);Tick();Assert.That(_arena.Actors[1].Hands.State.PunchCount,Is.Zero);
            _arena.SubmitLocal(Vector2.zero,false,false,false,false,true,false,1f/60);Tick();Assert.That(_arena.Actors[1].Hands.State.PunchCount,Is.EqualTo(1));
            var rag=old.GetComponent<RagdollRecoveryReview>();rag.BeginDown(Vector3.zero,Vector3.zero);var remaining=rag.Down.State.RemainingSeconds;
            _arena.SetMenu(true);for(var step=0;step<30;step++){_arena.SubmitLocal(Vector2.one,true,true,true,true);Tick();}
            Assert.That(rag.Down.State.RemainingSeconds,Is.LessThan(remaining-.4));Assert.That(_arena.SharedCamera.SubjectCount,Is.EqualTo(2));
            _arena.SetMenu(false);_arena.SubmitLocal(Vector2.zero,false,false,true,true);Tick();Assert.That(_arena.Actors[1].Hands.State.PunchCount,Is.EqualTo(1));
            _arena.ResetReview();Tick();foreach(var actor in _arena.Actors){var r=actor.GetComponent<PunchDownReview>();Assert.That(r.Ragdoll.Down.State.DownCount,Is.Zero);Assert.That(r.Accumulation.Points,Is.Zero);Assert.That(actor.Hands.Grabs.ThrowCount,Is.Zero);}
            for(var i=0;i<2;i++){var p=_arena.Actors[i].Motor.Body.position;Assert.That(Vector2.Distance(new Vector2(p.x,p.z),new Vector2(spawn[i].x,spawn[i].z)),Is.LessThan(.001f));}
            yield return null;
        }
        [UnityTest] public IEnumerator PreviousSpeedFourActorMovementJumpAndDownStayFramedInSharedView()
        {
            // This historical radial camera fixture is bounded at the previous speed.
            // The approved 2x unbounded spread reaches the existing dolly limit; map/camera integration remains open.
            Create(4,1f);var c=_arena.SharedCamera;c.View.aspect=16f/9;var rt=new RenderTexture(960,540,24);rt.Create();c.View.targetTexture=rt;var image=new Texture2D(960,540,TextureFormat.RGB24,false);
            var dir=Path.Combine(Output,"Motion");Directory.CreateDirectory(dir);var maxDolly=0f;var minDolly=100f;
            for(var step=0;step<300;step++)
            {
                for(var i=0;i<4;i++)
                {
                    var input=i==0?Vector2.left:i==1?Vector2.right:i==2?Vector2.up:Vector2.down;
                    _arena.Actors[i].Motor.MoveInput=step>=60&&step<240?input:Vector2.zero;_arena.Actors[i].Motor.SprintRequested=true;
                }
                if(step==110)_arena.Actors[0].Motor.RequestJump();
                if(step==150)_arena.Actors[1].GetComponent<RagdollRecoveryReview>().BeginDown(Vector3.zero,Vector3.right*.4f);
                Tick();AssertFramed();maxDolly=Mathf.Max(maxDolly,c.Dolly);minDolly=Mathf.Min(minDolly,c.Dolly);
                if(step%3==0)
                {
                    yield return null;c.View.Render();var previous=RenderTexture.active;RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,960,540),0,0);image.Apply();RenderTexture.active=previous;File.WriteAllBytes(Path.Combine(dir,$"Frame_{step/3:D3}.png"),image.EncodeToPNG());
                }
            }
            Assert.That(_arena.Actors[0].Motor.JumpCount,Is.EqualTo(1));Assert.That(_arena.Actors[1].GetComponent<RagdollRecoveryReview>().Down.State.DownCount,Is.EqualTo(1));Assert.That(c.SubjectCount,Is.EqualTo(4));
            File.WriteAllText(Path.Combine(dir,"Report.json"),$"{{\"status\":\"PASS\",\"actors\":4,\"frames\":100,\"fps\":20,\"minDolly\":{minDolly.ToString("R",System.Globalization.CultureInfo.InvariantCulture)},\"maxDolly\":{maxDolly.ToString("R",System.Globalization.CultureInfo.InvariantCulture)},\"fixture\":\"Scripted movement; jump request and explicit Down injection for camera framing review, not recorded human multiplayer.\"}}");
            c.View.targetTexture=null;rt.Release();UnityEngine.Object.Destroy(rt);UnityEngine.Object.Destroy(image);
        }
    }
}
