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
    public sealed class OpeningContactProbe : MonoBehaviour
    {
        public ulong FirstTick;
        private void OnCollisionEnter(Collision collision)
        {if(FirstTick==0&&collision.collider.GetComponent<PhysicsJumpReview>()!=null)FirstTick=GetComponent<PhysicsJumpReview>().Motor.SimulationTick;}
    }
    public sealed class ArenaBoundaryReviewTests
    {
        private const float Dt=1f/60;
        private Scene _scene;
        private ArenaBoundaryReview _boundary;
        private SharedArenaReview Arena=>_boundary.Arena;
        private string Output=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../.codex_tmp/arena-boundary-20260910"));
        [Serializable] private sealed class Opening {public int players,layout;public bool sprint;public float seconds;}
        [Serializable] private sealed class Framing {public int players;public float aspect,previousDolly,candidateDolly;public bool previousOverflow;}
        [Serializable] private sealed class Motion {public string profile;public int frames,outs,unsafeFrames;public float maximumDolly,minimumMargin,maximumRootError,maximumCameraError;public ulong[] outTicks;}
        [Serializable] private sealed class Report {public string status;public Opening[] opening;public Framing[] framing;public Motion[] motion;}
        [UnityTearDown] public IEnumerator Cleanup(){if(!_scene.IsValid()||!_scene.isLoaded)yield break;var op=SceneManager.UnloadSceneAsync(_scene);while(op!=null&&!op.isDone)yield return null;}
        private void Create(int count=2,float maxDolly=22,int layout=0)
        {
            _scene=SceneManager.CreateScene("Bounded arena",new CreateSceneParameters(LocalPhysicsMode.Physics3D));_boundary=ArenaBoundaryReview.Create(_scene,count,maxDolly,layout);_boundary.ManualStep=true;
            Arena.ReadKeyboard=false;Arena.ShowHelp=false;Arena.SharedCamera.ManualStep=true;Arena.SharedCamera.View.enabled=false;
            foreach(var a in Arena.Actors){a.ReadKeyboard=false;a.Motor.ManualStep=true;a.Hands.SubmitButtons(false,false);}Arena.InitializeParticipants();Directory.CreateDirectory(Output);
        }
        private void Tick()
        {
            foreach(var a in Arena.Actors.Where(a=>a.gameObject.activeInHierarchy)){var r=a.GetComponent<PunchDownReview>();r.Ragdoll.Down.Step(Dt);r.Ragdoll.Step(Dt);r.Step();}
            foreach(var a in Arena.Actors.Where(a=>a.gameObject.activeInHierarchy))a.Motor.Step(Dt);
            foreach(var a in Arena.Actors.Where(a=>a.gameObject.activeInHierarchy))a.Hands.Step(Dt);
            _scene.GetPhysicsScene().Simulate(Dt);_boundary.StepAfterPhysics();
            foreach(var a in Arena.Actors.Where(a=>a.gameObject.activeInHierarchy))a.FixedPose.Advance(Dt);Arena.SharedCamera.AdvanceFixed(Dt);
        }
        private void Place(int index,Vector3 position)
        {
            var a=Arena.Actors[index];a.transform.SetPositionAndRotation(position,Quaternion.identity);a.Motor.Body.position=position;a.Motor.Body.rotation=Quaternion.identity;
            if(!a.Motor.Body.isKinematic)a.Motor.Body.linearVelocity=a.Motor.Body.angularVelocity=Vector3.zero;
            UnityEngine.Physics.SyncTransforms();
        }
        private float Margin()
        {
            var c=Arena.SharedCamera;var margin=1f;
            for(var i=0;i<Arena.Actors.Length;i++)
            {
                if(_boundary.Out[i])continue;
                var bounds=new Bounds(Arena.Actors[i].Motor.Body.position+c.SubjectCenter,c.SubjectExtents*2);
                for(var x=-1;x<=1;x+=2)for(var y=-1;y<=1;y+=2)for(var z=-1;z<=1;z+=2)
                {
                    var p=c.View.WorldToViewportPoint(bounds.center+Vector3.Scale(bounds.extents,new Vector3(x,y,z)));
                    margin=Mathf.Min(margin,p.x,1-p.x,p.y,1-p.y);if(p.z<=c.View.nearClipPlane)return -1;
                }
            }
            return margin;
        }
        [UnityTest] public IEnumerator CoreOobExcludesOnSameTickAndResetRestoresDetachedRagdoll()
        {
            Create();for(var i=0;i<60;i++)Tick();var actor=Arena.Actors[0];var rag=actor.GetComponent<RagdollRecoveryReview>();
            rag.BeginDown(Vector3.zero,Vector3.zero);var bodies=new Rigidbody[16];var count=rag.CopyDynamicBodies(bodies);
            var limb=bodies.Take(count).First(b=>b.name.StartsWith("Calf"));limb.position=new Vector3(20,-10,0);UnityEngine.Physics.SyncTransforms();
            _boundary.StepAfterPhysics();Arena.SharedCamera.AdvanceFixed(Dt);Assert.That(_boundary.Out[0],Is.False,"An outside limb must not eliminate the core.");Assert.That(Arena.SharedCamera.SubjectCount,Is.EqualTo(2));
            Place(0,new Vector3(0,-5,0));_boundary.StepAfterPhysics();Arena.SharedCamera.AdvanceFixed(Dt);
            Assert.That(_boundary.Out[0],Is.True);Assert.That(_boundary.OutTick[0],Is.EqualTo(_boundary.Tick));Assert.That(Arena.SharedCamera.SubjectCount,Is.EqualTo(1));
            Assert.That(bodies.Take(count).All(b=>!b.gameObject.activeInHierarchy),Is.True,"Detached proxy bodies must also leave physics.");
            Arena.ResetReview();Tick();actor.FixedPose.Present(0);Assert.That(actor.Pose.transform.position,Is.EqualTo(actor.Motor.Body.position),"Reset must not display the previous OOB position.");Assert.That(_boundary.AliveCount,Is.EqualTo(2));Assert.That(rag.Active,Is.False);rag.BeginDown(Vector3.zero,Vector3.zero);Tick();
            count=rag.CopyDynamicBodies(bodies);Assert.That(count,Is.EqualTo(12));Assert.That(bodies.Take(count).All(b=>b.gameObject.activeInHierarchy),Is.True);Assert.That(rag.MaximumJointGap,Is.LessThan(.08f));
            foreach(var side in new[]{"East","West","North","South","Bottom"})
            {
                Arena.ResetReview();Tick();Assert.That(_boundary.TryCoreBounds(0,out var core),Is.True);
                var limit=side=="Bottom"?_boundary.BottomCU:side=="East"||side=="West"?_boundary.SideX:_boundary.SideZ;
                var axis=side=="Bottom"?Vector3.down:side=="East"?Vector3.right:side=="West"?Vector3.left:side=="North"?Vector3.forward:Vector3.back;
                var extent=Vector3.Dot(core.extents,new Vector3(Mathf.Abs(axis.x),Mathf.Abs(axis.y),Mathf.Abs(axis.z)));
                var inside=axis*(Mathf.Abs(limit)-extent-.002f);var component=Vector3.Scale(core.center,new Vector3(Mathf.Abs(axis.x),Mathf.Abs(axis.y),Mathf.Abs(axis.z)));
                Place(0,actor.Motor.Body.position+inside-component);_boundary.StepAfterPhysics();Assert.That(_boundary.Out[0],Is.False,side+" inside");
                Place(0,actor.Motor.Body.position+axis*.004f);_boundary.StepAfterPhysics();Arena.SharedCamera.AdvanceFixed(Dt);
                Assert.That(_boundary.OutReason[0],Is.EqualTo(side));Assert.That(Arena.SharedCamera.Included[0],Is.False);Assert.That(Arena.SharedCamera.SubjectCount,Is.EqualTo(1));Assert.That(actor.GetComponent<PunchHitRig>().Bone("Root").gameObject.activeInHierarchy,Is.False);
            }
            yield return null;
        }
        [UnityTest] public IEnumerator OobCancelsOnlyGripsToLostTargetAndCannotThrow()
        {
            Create(4);for(var i=0;i<4;i++)Place(i,new Vector3(i<2?-2:2,.02f,i%2*.4f));for(var i=0;i<90;i++)Tick();
            var left=Arena.Actors[0];var right=Arena.Actors[2];left.Hands.SubmitButtons(true,false);right.Hands.SubmitButtons(true,false);for(var i=0;i<30;i++)Tick();
            Assert.That(left.Hands.Grabs.IsHolding(0)&&right.Hands.Grabs.IsHolding(0),Is.True);
            Place(1,new Vector3(_boundary.SideX+1,.02f,0));_boundary.StepAfterPhysics();Arena.SharedCamera.AdvanceFixed(Dt);
            Assert.That(left.Hands.Grabs.IsHolding(0),Is.False);Assert.That(right.Hands.Grabs.IsHolding(0),Is.True);Assert.That(left.Hands.Grabs.ThrowCount,Is.Zero);Assert.That(Arena.SharedCamera.SubjectCount,Is.EqualTo(3));
            left.Hands.SubmitButtons(false,false);Tick();Assert.That(left.Hands.Grabs.ThrowCount,Is.Zero);
            Arena.ResetReview();Tick();Assert.That(_boundary.AliveCount,Is.EqualTo(4));foreach(var a in Arena.Actors)Assert.That(a.Hands.Grabs.ThrowCount,Is.Zero);
            yield return null;
        }
        [UnityTest] public IEnumerator NativeLoopRemovesOobBeforeCameraSnapshot()
        {
            _scene=SceneManager.CreateScene("Native boundary loop");_boundary=ArenaBoundaryReview.Create(_scene,2);Arena.ReadKeyboard=false;Arena.ShowHelp=false;Arena.SharedCamera.View.enabled=false;
            foreach(var a in Arena.Actors)a.ReadKeyboard=false;Place(1,new Vector3(20,.02f,0));
            yield return new WaitForFixedUpdate();yield return null;
            Assert.That(_boundary.Out[1],Is.True);Assert.That(Arena.SharedCamera.SubjectCount,Is.EqualTo(1));Assert.That(Arena.Actors[1].gameObject.activeInHierarchy,Is.False);
            Arena.ResetReview();yield return new WaitForFixedUpdate();yield return null;
            Assert.That(_boundary.AliveCount,Is.EqualTo(2));Assert.That(Arena.SharedCamera.SubjectCount,Is.EqualTo(2));
        }
        [UnityTest] public IEnumerator OpeningUsesSameGeometryAndMeasuresActualFirstContact()
        {
            var rows=new List<Opening>();
            foreach(var count in new[]{2,3,4})foreach(var sprint in new[]{false,true})for(var layout=0;layout<(count==2?2:4);layout++)
            {
                Create(count,22,layout);Assert.That(Arena.transform.Find("ReviewRoof").localScale,Is.EqualTo(new Vector3(8.8f,.28f,6.2f)));
                var probes=Arena.Actors.Select(a=>a.gameObject.AddComponent<OpeningContactProbe>()).ToArray();for(var i=0;i<60;i++)Tick();
                Assert.That(probes.All(p=>p.FirstTick==0),Is.True,"Spawn must not start in another player.");var start=Arena.Actors[0].Motor.SimulationTick;
                for(var i=0;i<180&&probes.All(p=>p.FirstTick==0);i++)
                {foreach(var a in Arena.Actors){var p=a.Motor.Body.position;var d=new Vector2(-p.x,-p.z).normalized;a.Motor.MoveInput=d;a.Motor.SprintRequested=sprint;}Tick();}
                Assert.That(probes.Any(p=>p.FirstTick>0),Is.True);Assert.That(_boundary.AliveCount,Is.EqualTo(count));
                var time=(probes.Where(p=>p.FirstTick>0).Min(p=>p.FirstTick)-start)*Dt;rows.Add(new Opening{players=count,layout=layout,sprint=sprint,seconds=time});
                Assert.That(time,Is.LessThan(sprint?1.2f:1.8f));var op=SceneManager.UnloadSceneAsync(_scene);while(op!=null&&!op.isDone)yield return null;
            }
            File.WriteAllText(Path.Combine(Output,"Opening.json"),JsonUtility.ToJson(new Report{status="PASS",opening=rows.ToArray()},true));
        }
        [UnityTest] public IEnumerator NineRosterAspectCasesKeepAliveRecoveryInsideCandidateCamera()
        {
            var rows=new List<Framing>();
            foreach(var count in new[]{2,3,4})foreach(var aspect in new[]{16f/9,16f/10,21f/9})
            {
                Create(count);var c=Arena.SharedCamera;c.View.aspect=aspect;
                var positions=new[]{new Vector3(0,.02f,2.9f),new Vector3(0,-2.62f,-4.63f),new Vector3(5.93f,-2.62f,-4.63f),new Vector3(-5.93f,.27f,4.63f)};
                for(var i=0;i<count;i++){Place(i,positions[i]);Arena.Actors[i].Motor.Body.isKinematic=true;Arena.Actors[i].FixedPose.Advance(Dt);}
                _boundary.StepAfterPhysics();Assert.That(_boundary.AliveCount,Is.EqualTo(count));
                c.MaximumDolly=15;for(var i=0;i<180;i++)c.AdvanceFixed(Dt);var row=new Framing{players=count,aspect=aspect,previousDolly=c.Dolly,previousOverflow=c.CapacityExceeded};
                if(count==4&&Mathf.Abs(aspect-16f/9)<.001f){yield return null;Capture(c.View,"RecoveryPrevious15");}
                c.MaximumDolly=22;for(var i=0;i<180;i++)c.AdvanceFixed(Dt);row.candidateDolly=c.Dolly;rows.Add(row);
                Assert.That(c.CapacityExceeded,Is.False);Assert.That(Margin(),Is.GreaterThanOrEqualTo(.15f-.0001f));
                if(count==4&&Mathf.Abs(aspect-16f/9)<.001f){yield return null;Capture(c.View,"RecoveryCandidate22");}
                var op=SceneManager.UnloadSceneAsync(_scene);while(op!=null&&!op.isDone)yield return null;
            }
            File.WriteAllText(Path.Combine(Output,"Framing.json"),JsonUtility.ToJson(new Report{status="PASS",framing=rows.ToArray()},true));
        }
        private void Capture(Camera camera,string name)
        {
            var rt=new RenderTexture(960,540,24);rt.Create();camera.targetTexture=rt;var image=new Texture2D(960,540,TextureFormat.RGB24,false);
            camera.Render();var previous=RenderTexture.active;RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,960,540),0,0);image.Apply();RenderTexture.active=previous;
            File.WriteAllBytes(Path.Combine(Output,name+".png"),image.EncodeToPNG());camera.targetTexture=null;rt.Release();UnityEngine.Object.Destroy(rt);UnityEngine.Object.Destroy(image);
        }
        private void MotionInput(int tick)
        {
            if(tick==430)Arena.ResetReview();
            foreach(var a in Arena.Actors.Where(a=>a.gameObject.activeInHierarchy))
            {var p=a.Motor.Body.position;var sign=tick<150?-1:1;var input=new Vector2(p.x,p.z).normalized*sign;a.Motor.MoveInput=tick>=60&&tick<420?input:Vector2.zero;a.Motor.SprintRequested=true;}
            if(tick==205)Arena.Actors[1].GetComponent<RagdollRecoveryReview>().BeginDown(Vector3.zero,new Vector3(.6f,.2f,0));
        }
        [UnityTest] public IEnumerator OobAndCameraHaveIdenticalTicksAcrossRenderSchedules()
        {
            var signatures=new List<string>();var positions=new List<Vector3[]>();var cameraPositions=new List<Vector3>();var rows=new List<Motion>();
            foreach(var schedule in new[]{"60","15","144","Hitch200ms"})
            {
                Create(4);var tick=0;var frame=0;var time=0d;var row=new Motion{profile=schedule};
                while(tick<480)
                {
                    time=Math.Min(8,time+(schedule=="Hitch200ms"?(frame%37==18?.2:1d/60):1d/int.Parse(schedule)));var due=Math.Min(480,(int)Math.Floor(time*60+1e-8));
                    while(tick<due)
                    {
                        MotionInput(tick);Tick();var signature=string.Join(",",_boundary.OutTick)+"|"+_boundary.AliveCount+"|"+Arena.SharedCamera.SubjectCount+"|"+_boundary.ResetVersion;
                        var roots=Arena.Actors.Select(a=>a.Motor.Body.position).ToArray();var camera=Arena.SharedCamera.View.transform.position;
                        if(schedule=="60"){signatures.Add(signature);positions.Add(roots);cameraPositions.Add(camera);}
                        else
                        {
                            Assert.That(signature,Is.EqualTo(signatures[tick]),schedule+" tick "+tick);
                            for(var i=0;i<roots.Length;i++)row.maximumRootError=Mathf.Max(row.maximumRootError,Vector3.Distance(roots[i],positions[tick][i]));
                            row.maximumCameraError=Mathf.Max(row.maximumCameraError,Vector3.Distance(camera,cameraPositions[tick]));
                        }
                        tick++;
                    }
                    var alpha=(float)Math.Max(0,Math.Min(1,(time-tick/60d)*60));
                    foreach(var a in Arena.Actors.Where(a=>a.gameObject.activeInHierarchy))a.FixedPose.Present(alpha);Arena.SharedCamera.Render(alpha);frame++;
                }
                Assert.That(row.maximumRootError,Is.LessThan(.0001f));Assert.That(row.maximumCameraError,Is.LessThan(.0001f));rows.Add(row);
                var op=SceneManager.UnloadSceneAsync(_scene);while(op!=null&&!op.isDone)yield return null;
            }
            File.WriteAllText(Path.Combine(Output,"FrameSchedules.json"),JsonUtility.ToJson(new Report{status="PASS",motion=rows.ToArray()},true));
        }
        [UnityTest] public IEnumerator FourActorRunFallLimpOutAndResetRemainFramed()
        {
            var rows=new List<Motion>();
            foreach(var maximum in new[]{15f,22f})
            {
                Create(4,maximum);var c=Arena.SharedCamera;c.View.aspect=16f/9;var dir=Path.Combine(Output,maximum==15?"Previous15":"Candidate22");Directory.CreateDirectory(dir);
                var rt=new RenderTexture(960,540,24);rt.Create();c.View.targetTexture=rt;var image=new Texture2D(960,540,TextureFormat.RGB24,false);var row=new Motion{profile=maximum.ToString(),minimumMargin=1};
                for(var tick=0;tick<480;tick++)
                {
                    if(tick==430){row.outs=_boundary.Out.Count(v=>v);row.outTicks=_boundary.OutTick.ToArray();}
                    MotionInput(tick);Tick();var margin=Margin();row.minimumMargin=Mathf.Min(row.minimumMargin,margin);row.maximumDolly=Mathf.Max(row.maximumDolly,c.Dolly);if(c.CapacityExceeded||margin<.15f-.0001f)row.unsafeFrames++;
                    if(tick>=60)
                    {
                        var frame=Time.frameCount;yield return null;Assert.That(Time.frameCount,Is.GreaterThan(frame));c.View.Render();var previous=RenderTexture.active;RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,960,540),0,0);image.Apply();RenderTexture.active=previous;
                        File.WriteAllBytes(Path.Combine(dir,$"Frame_{row.frames:D4}.png"),image.EncodeToPNG());row.frames++;
                    }
                }
                Assert.That(row.outs,Is.GreaterThan(0));Assert.That(_boundary.AliveCount,Is.EqualTo(4));if(maximum==22)Assert.That(row.unsafeFrames,Is.Zero,JsonUtility.ToJson(row));rows.Add(row);
                c.View.targetTexture=null;rt.Release();UnityEngine.Object.Destroy(rt);UnityEngine.Object.Destroy(image);var op=SceneManager.UnloadSceneAsync(_scene);while(op!=null&&!op.isDone)yield return null;
            }
            File.WriteAllText(Path.Combine(Output,"Motion.json"),JsonUtility.ToJson(new Report{status="PASS",motion=rows.ToArray()},true));
        }
    }
}
