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
    public sealed class ActiveGrabStageTwoTests
    {
        private const float Dt=1f/60;
        private Scene _scene;private ActiveGrabPairReview _pair;
        private string Output=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../.codex_tmp/active-grab-stage2-20260915"));
        private void Create(bool native=false,bool warm=true,float distance=.5f)
        {
            _scene=SceneManager.CreateScene("Active grab stage two",new CreateSceneParameters(native?LocalPhysicsMode.None:LocalPhysicsMode.Physics3D));
            _pair=ActiveGrabPairReview.Create(_scene,distance);_pair.ManualStep=!native;_pair.ReadKeyboard=false;_pair.View.enabled=false;Directory.CreateDirectory(Output);
            if(!native&&warm)for(var i=0;i<60;i++)Tick();
        }
        private void Tick(bool reverse=false){_pair.Step(Dt,reverse);_scene.GetPhysicsScene().Simulate(Dt);_pair.CapturePhysics();}
        private void Buttons(int player,int hands)=>_pair.Buttons(player,(hands&1)!=0,(hands&2)!=0);
        [UnityTearDown] public IEnumerator Cleanup()
        {if(!_scene.IsValid()||!_scene.isLoaded)yield break;var op=SceneManager.UnloadSceneAsync(_scene);while(op!=null&&!op.isDone)yield return null;}
        [UnityTest] public IEnumerator InspectRealUnilateralAndMutualContacts()
        {
            var rows=new List<string>();
            foreach(var mutual in new[]{false,true})foreach(var hands in new[]{1,3})
            {
                Create();Buttons(0,hands);if(mutual)Buttons(1,hands==1?2:3);
                var saw=false;
                for(var tick=0;tick<200;tick++)
                {
                    if(tick==35&&!mutual)_pair.Players[0].Move=Vector2.up*.25f;
                    if(_pair.Players[0].Acquisitions>0)_pair.Players[0].Move=Vector2.zero;
                    Tick();saw|=_pair.LogicalGrips>0;
                    if(tick%10==0)rows.Add($"mutual={mutual} hands={hands} tick={tick} grips={_pair.LogicalGrips} constraints={_pair.PhysicalConnections} gap={_pair.MaximumGripGap:R} force={_pair.MaximumGripForce:R} "+string.Join(" | ",_pair.Players.Select(p=>$"root={p.Rig.Actor.Motor.Body.position:R} acq={p.Acquisitions} rel={p.LastRelease} states={p.Hands[0].Phase}/{p.Hands[1].Phase} target={p.Hands[0].TargetBody?.name}/{p.Hands[1].TargetBody?.name} hands={p.Rig.HandPosition(0):R}/{p.Rig.HandPosition(1):R} span={p.Hands[0].Connection?.Span}/{p.Hands[1].Connection?.Span} currentForce={p.Hands[0].Connection?.Force}/{p.Hands[1].Connection?.Force} palm={p.PalmTouches} rejected={p.RejectedArmTouches} nearest={p.ClosestRejectedTouch:R} errors={p.Rig.TrackingError("UpperArm_R"):R}/{p.Rig.TrackingError("Forearm_R"):R}")));
                }
                File.WriteAllLines(Path.Combine(Output,"InitialContacts.txt"),rows);
                Assert.That(saw,Is.True,$"mutual={mutual}, hands={hands}");
                if(mutual)Assert.That(_pair.Players.All(p=>p.Acquisitions>0),Is.True);
                Assert.That(_pair.MaximumGripGap,Is.LessThan(.04f));
                var op=SceneManager.UnloadSceneAsync(_scene);while(op!=null&&!op.isDone)yield return null;
            }
        }
        [UnityTest] public IEnumerator InspectSpawnConstraints()
        {
            Create(false,false);var rows=new List<string>();
            for(var tick=0;tick<15;tick++)
            {
                foreach(var player in _pair.Players)
                {
                    var rig=player.Rig;rows.Add($"tick={tick} root={rig.Actor.Motor.Body.position:R} heading={rig.Actor.Motor.Body.rotation.eulerAngles:R}");
                    foreach(var link in rig.Links)
                    {
                        var gap=Vector3.Distance(link.Body.position+link.Body.rotation*link.Joint.anchor,link.ParentBody.position+link.ParentBody.rotation*link.Joint.connectedAnchor);
                        Assert.That(gap,Is.LessThan(.005f),$"Spawn tick={tick}, {link.Name}");
                        rows.Add($"  {link.Name} P={link.Body.position:R} gap={gap:R} error={rig.TrackingError(link.Name):R} target={link.Joint.targetRotation.eulerAngles:R}");
                    }
                }
                var shapes=_pair.Players.SelectMany(p=>p.Rig.Shapes).ToArray();
                for(var a=0;a<shapes.Length;a++)for(var b=a+1;b<shapes.Length;b++)
                {
                    var x=shapes[a];var y=shapes[b];if(UnityEngine.Physics.GetIgnoreCollision(x,y))continue;
                    if(UnityEngine.Physics.ComputePenetration(x,x.transform.position,x.transform.rotation,y,y.transform.position,y.transform.rotation,out _,out var depth))rows.Add($"  overlap {x.transform.parent.name}/{x.name} with {y.transform.parent.name}/{y.name} = {depth:R}");
                }
                Tick();
            }
            File.WriteAllLines(Path.Combine(Output,"Spawn.txt"),rows);yield return null;
        }
        [UnityTest] public IEnumerator FacingOppositeDirectionsPreservesReferenceTracking()
        {
            Create(distance:1.2f);Buttons(0,3);Buttons(1,3);for(var i=0;i<180;i++)Tick();
            var rows=_pair.Players.Select(p=>$"root={p.Rig.Actor.Motor.Body.position:R}, L={p.Rig.HandPosition(0):R}/{p.Rig.GoalHand(0):R}, R={p.Rig.HandPosition(1):R}/{p.Rig.GoalHand(1):R}, "+string.Join(" ",p.Rig.Links.Select(l=>$"{l.Name}={p.Rig.TrackingError(l.Name):F3}"))).ToArray();
            File.WriteAllLines(Path.Combine(Output,"Facing.txt"),rows);
            Assert.That(_pair.LogicalGrips,Is.Zero);
            foreach(var player in _pair.Players)Assert.That(player.Rig.Links.Max(l=>player.Rig.TrackingError(l.Name)),Is.LessThan(15));yield return null;
        }
        private bool AwaitGrips(int count,int maximum=150,bool approach=false,bool reverse=false)
        {
            for(var i=0;i<maximum;i++)
            {
                if(approach&&i>=30)_pair.Players[0].Move=Vector2.up*.25f;
                Tick(reverse);if(_pair.LogicalGrips>=count){_pair.Players[0].Move=Vector2.zero;return true;}
            }
            _pair.Players[0].Move=Vector2.zero;return false;
        }
        [UnityTest] public IEnumerator SharedGripOwnershipAndImmediateReleaseAreIndependent()
        {
            foreach(var reverse in new[]{false,true})foreach(var hands in new[]{1,3})
            {
                Create();Buttons(0,hands);Buttons(1,hands==1?2:3);Assert.That(AwaitGrips(hands==1?2:4,reverse:reverse),Is.True);
                var physical=_pair.PhysicalConnections;Assert.That(physical,Is.EqualTo(hands==1?1:2));
                for(var i=0;i<180;i++)Tick(reverse);
                Assert.That(_pair.LogicalGrips,Is.EqualTo(hands==1?2:4));
                foreach(var p in _pair.Players){Assert.That(p.Breaks,Is.Zero);Assert.That(p.Rig.Actor.Motor.Body.position.y,Is.LessThan(.04f));}
                Buttons(0,0);Tick(reverse);Assert.That(_pair.Players[0].Hands.All(h=>h.Phase==ActiveGrabPairReview.HandPhase.Idle),Is.True);
                Assert.That(_pair.PhysicalConnections,Is.EqualTo(physical));Assert.That(_pair.Players[1].Hands.Count(h=>h.Phase==ActiveGrabPairReview.HandPhase.Holding),Is.EqualTo(hands==1?1:2));
                Buttons(1,0);Tick(reverse);Assert.That(_pair.LogicalGrips+_pair.PhysicalConnections,Is.Zero);
                var op=SceneManager.UnloadSceneAsync(_scene);while(op!=null&&!op.isDone)yield return null;
            }
        }
        [UnityTest] public IEnumerator UnilateralTwoHandsReleaseOneAtATime()
        {
            Create();Buttons(0,3);Assert.That(AwaitGrips(2,approach:true),Is.True);var h=_pair.Players[0].Hands;
            Assert.That(h[0].TargetRig,Is.EqualTo(_pair.Players[1].Rig));Assert.That(h[1].TargetRig,Is.EqualTo(_pair.Players[1].Rig));
            for(var i=0;i<120;i++)Tick();Buttons(0,2);Tick();Assert.That(h[0].Phase,Is.EqualTo(ActiveGrabPairReview.HandPhase.Idle));
            Assert.That(h[1].Phase,Is.EqualTo(ActiveGrabPairReview.HandPhase.Holding));Buttons(0,0);Tick();Assert.That(_pair.PhysicalConnections,Is.Zero);yield return null;
        }
        [UnityTest] public IEnumerator NoContactNoGripAndMenuRequiresFreshInput()
        {
            Create(distance:1.2f);Buttons(0,3);for(var i=0;i<120;i++)Tick();Assert.That(_pair.LogicalGrips,Is.Zero);
            _pair.SetSuppressed(true);Buttons(1,3);Tick();_pair.SetSuppressed(false);
            for(var i=0;i<30;i++)Tick();Assert.That(_pair.Players.SelectMany(p=>p.Hands).All(h=>h.Phase==ActiveGrabPairReview.HandPhase.AwaitRelease),Is.True);
            Buttons(0,0);Buttons(1,0);Tick();Buttons(0,1);for(var i=0;i<20;i++)Tick();Assert.That(_pair.Players[0].Hands[0].Phase,Is.EqualTo(ActiveGrabPairReview.HandPhase.Seeking));yield return null;
        }
        [UnityTest] public IEnumerator DraggingTransfersForceAndResetRemovesAllConnections()
        {
            Create();Buttons(0,3);Assert.That(AwaitGrips(2,approach:true),Is.True);for(var i=0;i<20;i++)Tick();
            var start=_pair.Players[1].Rig.Actor.Motor.Body.position;_pair.Players[0].Move=Vector2.down*.5f;
            var samples=new List<string>();for(var i=0;i<90;i++){Tick();if(i%10==0)samples.Add($"tick={i} A={_pair.Players[0].Rig.Actor.Motor.Body.position:R} B={_pair.Players[1].Rig.Actor.Motor.Body.position:R} grips={_pair.LogicalGrips} force={_pair.MaximumGripForce:R}");}
            var distance=Vector3.Distance(start,_pair.Players[1].Rig.Actor.Motor.Body.position);File.WriteAllLines(Path.Combine(Output,"Drag.txt"),samples);
            Assert.That(distance,Is.GreaterThan(.08f));_pair.ResetReview();Assert.That(_pair.PhysicalConnections+_pair.LogicalGrips,Is.Zero);
            for(var i=0;i<30;i++)Tick();Assert.That(_pair.LogicalGrips,Is.Zero);foreach(var p in _pair.Players)Assert.That(p.Rig.TotalMass,Is.EqualTo(20));yield return null;
        }
        [UnityTest] public IEnumerator CapturePhysicalGrabsAndSkin()
        {
            var failures=new List<string>();
            foreach(var mode in new[]{"OneHand","TwoHands","Mutual"})
            {
                Create();var hands=mode=="OneHand"?2:3;Buttons(0,hands);if(mode=="Mutual")Buttons(1,3);
                var folder=Path.Combine(Output,mode);Directory.CreateDirectory(folder);
                var rt=new RenderTexture(960,540,24);rt.Create();var image=new Texture2D(960,540,TextureFormat.RGB24,false);_pair.View.targetTexture=rt;_pair.View.aspect=16f/9;
                var skins=_pair.Players.Select(p=>p.Rig.Actor.Pose.GetComponentsInChildren<SkinnedMeshRenderer>().Single(s=>s.name.EndsWith("SkinnedBody"))).ToArray();
                var mesh=new Mesh();var triangles=skins[0].sharedMesh.triangles;var pairs=CameraGaitReviewTests.Pairs(triangles);var surface=0f;var saw=false;var rows=new List<string>();
                for(var tick=0;tick<240;tick++)
                {
                    if(mode!="Mutual"&&tick>=30&&_pair.Players[0].Acquisitions==0)_pair.Players[0].Move=Vector2.up*.25f;
                    if(_pair.Players[0].Acquisitions>0&&tick<100)_pair.Players[0].Move=Vector2.zero;
                    if(tick==100)_pair.Players[0].Move=Vector2.down*.45f;
                    if(tick==130&&mode=="TwoHands")_pair.Players[1].Move=Vector2.up*.45f;
                    if(tick==160){_pair.Players[0].Move=_pair.Players[1].Move=Vector2.zero;if(mode=="TwoHands")Buttons(0,2);else Buttons(0,0);}
                    if(tick==190){Buttons(0,0);Buttons(1,0);}
                    Tick();saw|=_pair.LogicalGrips>0;
                    foreach(var skin in skins){skin.BakeMesh(mesh);var angle=CameraGaitReviewTests.Surface(mesh.vertices,triangles,pairs,out var where);surface=Mathf.Max(surface,angle);if(angle>60)rows.Add($"SKIN actor={Array.IndexOf(skins,skin)} tick={tick} angle={angle:R} at={where:R}");}
                    rows.Add($"tick={tick}, grips={_pair.LogicalGrips}, constraints={_pair.PhysicalConnections}, A={_pair.Players[0].Rig.Actor.Motor.Body.position:R}, B={_pair.Players[1].Rig.Actor.Motor.Body.position:R}, gap={_pair.MaximumGripGap:R}, force={_pair.MaximumGripForce:R}, surface={surface:R}");
                    _pair.Present(1);yield return null;_pair.View.Render();var old=RenderTexture.active;RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,960,540),0,0);image.Apply();RenderTexture.active=old;
                    File.WriteAllBytes(Path.Combine(folder,$"Frame_{tick:D4}.png"),image.EncodeToPNG());
                    if(mode=="TwoHands"&&tick==140)
                    {
                        var cameraPosition=_pair.View.transform.position;var cameraRotation=_pair.View.transform.rotation;var size=_pair.View.orthographicSize;
                        var focus=skins[0].transform.TransformPoint(new Vector3(-.15f,.61f,.09f));
                        var offsets=new[]{new Vector3(-.7f,.13f,-.15f),new Vector3(-.7f,.1f,.4f)};
                        for(var side=0;side<offsets.Length;side++)
                        {
                            _pair.View.orthographicSize=.24f;_pair.View.transform.SetPositionAndRotation(focus+offsets[side],Quaternion.LookRotation(-offsets[side]));
                            _pair.View.Render();old=RenderTexture.active;RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,960,540),0,0);image.Apply();RenderTexture.active=old;
                            File.WriteAllBytes(Path.Combine(folder,$"Shoulder_{side}.png"),image.EncodeToPNG());
                        }
                        _pair.View.orthographicSize=size;_pair.View.transform.SetPositionAndRotation(cameraPosition,cameraRotation);
                    }
                }
                File.WriteAllLines(Path.Combine(folder,"Trace.txt"),rows);_pair.View.targetTexture=null;rt.Release();UnityEngine.Object.Destroy(rt);UnityEngine.Object.Destroy(image);UnityEngine.Object.Destroy(mesh);
                if(!saw||surface>=60||_pair.LogicalGrips!=0)failures.Add($"{mode}: contact={saw}, maximumSurfaceAngle={surface:R}, remainingGrips={_pair.LogicalGrips}");
                var op=SceneManager.UnloadSceneAsync(_scene);while(op!=null&&!op.isDone)yield return null;
            }
            Assert.That(failures,Is.Empty,string.Join("\n",failures));
        }
        [UnityTest] public IEnumerator IdleHoldAndLoadedStressAreDifferent()
        {
            Create();Buttons(0,3);Buttons(1,3);Assert.That(AwaitGrips(4),Is.True);
            for(var i=0;i<600;i++)Tick();Assert.That(_pair.LogicalGrips,Is.EqualTo(4));
            var idleStress=_pair.Players.SelectMany(p=>p.Hands).Max(h=>h.Stress.Value);
            Assert.That(idleStress,Is.LessThan(.01f));
            // A sustained, measured load exercises fatigue. Abrupt full-speed opposition
            // can hit the independent contact-extension safety limit first.
            var loadedStress=0f;var fatigueRelease=false;
            for(var i=0;i<600;i++)
            {
                _pair.Players[0].Rig.Actor.Motor.Body.AddForce(Vector3.back*240,ForceMode.Force);
                _pair.Players[1].Rig.Actor.Motor.Body.AddForce(Vector3.forward*240,ForceMode.Force);
                Tick();loadedStress=Mathf.Max(loadedStress,_pair.Players.SelectMany(p=>p.Hands).Max(h=>h.Stress.Value));
                fatigueRelease|=_pair.Players.Any(p=>p.LastRelease=="GripStress");
                if(_pair.LogicalGrips==0)break;
            }
            File.WriteAllText(Path.Combine(Output,"Stress.txt"),$"idleStress={idleStress:R}, loadedStress={loadedStress:R}, fatigueRelease={fatigueRelease}, breaks={_pair.Players.Sum(p=>p.Breaks)}, gap={_pair.MaximumGripGap:R}");
            Assert.That(loadedStress,Is.GreaterThan(.95f));Assert.That(fatigueRelease,Is.True);
            Assert.That(_pair.Players.Sum(p=>p.Breaks),Is.GreaterThan(0));yield return null;
        }
        [UnityTest] public IEnumerator TargetLossAndResetCannotReviveHeldButtons()
        {
            Create();Buttons(0,3);Assert.That(AwaitGrips(2,approach:true),Is.True);
            _pair.Players[1].Rig.gameObject.SetActive(false);Tick();Assert.That(_pair.LogicalGrips+_pair.PhysicalConnections,Is.Zero);
            Assert.That(_pair.Players[0].Hands.All(h=>h.Phase==ActiveGrabPairReview.HandPhase.AwaitRelease),Is.True);
            for(var i=0;i<20;i++)Tick();Assert.That(_pair.LogicalGrips,Is.Zero);yield return null;
        }
        [UnityTest] public IEnumerator FixedContactReplayDoesNotDependOnRenderRate()
        {
            var poses=new List<Vector3[]>();var states=new List<string>();var rows=new List<string>();
            foreach(var fps in new[]{60,15,144})
            {
                Create();Buttons(0,3);var time=0d;var tick=0;var error=0f;
                while(tick<240)
                {
                    time=Math.Min(4,time+1d/fps);var due=Math.Min(240,(int)Math.Floor(time*60+1e-8));
                    while(tick<due)
                    {
                        if(tick>=30&&tick<100)_pair.Players[0].Move=_pair.LogicalGrips<2?Vector2.up*.25f:Vector2.zero;
                        if(tick==100)_pair.Players[0].Move=Vector2.down*.4f;
                        if(tick==140){_pair.Players[0].Move=Vector2.zero;Buttons(0,2);}if(tick==180)Buttons(0,0);
                        Tick();var points=_pair.Players.SelectMany(p=>p.Rig.Links.Select(l=>l.Body.position).Append(p.Rig.Actor.Motor.Body.position)).ToArray();
                        var state=$"{_pair.LogicalGrips}/{_pair.PhysicalConnections}/"+string.Join(",",_pair.Players.SelectMany(p=>p.Hands).Select(h=>h.Phase.ToString()));
                        if(fps==60){poses.Add(points);states.Add(state);}else{Assert.That(state,Is.EqualTo(states[tick]));for(var i=0;i<points.Length;i++)error=Mathf.Max(error,Vector3.Distance(points[i],poses[tick][i]));}tick++;
                    }
                    _pair.Present((float)Math.Clamp((time-tick/60d)*60,0,1));
                }
                rows.Add($"fps={fps}, maximumPhysicalPositionDifference={error:R}");File.WriteAllLines(Path.Combine(Output,"FrameRates.txt"),rows);Assert.That(error,Is.LessThan(.0001f));
                var op=SceneManager.UnloadSceneAsync(_scene);while(op!=null&&!op.isDone)yield return null;
            }
        }
        [UnityTest] public IEnumerator NativePairLoopRunsInputAndPhysicsOnce()
        {
            var saved=Time.captureDeltaTime;
            try
            {
                Time.captureDeltaTime=Dt;Create(true);Buttons(0,3);Buttons(1,3);
                for(var i=0;i<120;i++)yield return null;
                Assert.That(_pair.Tick,Is.GreaterThan(80));Assert.That(_pair.LogicalGrips,Is.EqualTo(4));
                foreach(var player in _pair.Players)Assert.That(player.Rig.Steps,Is.EqualTo(_pair.Tick));
            }
            finally{Time.captureDeltaTime=saved;}
        }
        [UnityTest] public IEnumerator ExportLoadedShoulderSamplesForWeightReview()
        {
            Create();Buttons(0,3);var skins=_pair.Players.Select(p=>p.Rig.Actor.Pose.GetComponentsInChildren<SkinnedMeshRenderer>().Single(s=>s.name.EndsWith("SkinnedBody"))).ToArray();
            var file=Path.Combine(Output,"LoadedSkinMatrices.bin");
            using(var writer=new BinaryWriter(File.Create(file)))
            {
                writer.Write(240);writer.Write(2);writer.Write(skins[0].bones.Length);
                for(var tick=0;tick<240;tick++)
                {
                    if(tick>=30&&tick<100)_pair.Players[0].Move=_pair.LogicalGrips<2?Vector2.up*.25f:Vector2.zero;
                    if(tick==100)_pair.Players[0].Move=Vector2.down*.45f;
                    if(tick==130)_pair.Players[1].Move=Vector2.up*.45f;
                    if(tick==160){_pair.Players[0].Move=_pair.Players[1].Move=Vector2.zero;Buttons(0,2);}if(tick==190)Buttons(0,0);
                    Tick();
                    foreach(var skin in skins)
                    {
                        var bind=skin.sharedMesh.bindposes;
                        for(var bone=0;bone<skin.bones.Length;bone++)
                        {
                            var matrix=skin.worldToLocalMatrix*skin.bones[bone].localToWorldMatrix*bind[bone];
                            for(var row=0;row<4;row++)for(var col=0;col<4;col++)writer.Write(matrix[row,col]);
                        }
                    }
                }
            }
            yield return null;
        }
    }
}
