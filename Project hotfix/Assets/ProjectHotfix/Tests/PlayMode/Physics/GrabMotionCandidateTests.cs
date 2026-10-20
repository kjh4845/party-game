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
    public sealed class GrabMotionCandidateTests
    {
        [Serializable] private sealed class CaptureFrame
        {public int tick,gripsA,gripsB;public float surfaceAngle,elbowDegrees,upperArmRaise,handHeight,gripGap,gripForce;}
        [Serializable] private sealed class CaptureCase
        {public string profile,scenario;public float maximumSurfaceAngle;public int maximumGrips;public CaptureFrame[] frames;}
        [Serializable] private sealed class CaptureReport
        {public string status="MOTION_COMPARISON_NOT_ADOPTED";public CaptureCase[] cases;}
        private const float Dt=1f/60;
        private Scene _scene;
        private ActiveGrabPairReview _pair;
        private string Output=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../.codex_tmp/grab-motion-20260916"));
        private void Create(ActiveGrabPairReview.GrabMotion motion,float distance=.5f)
        {
            _scene=SceneManager.CreateScene("Grab motion "+motion,new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            _pair=ActiveGrabPairReview.Create(_scene,distance,motion);_pair.ReadKeyboard=false;_pair.ManualStep=true;_pair.View.enabled=false;
            Directory.CreateDirectory(Output);for(var i=0;i<60;i++)Tick();
        }
        private void Tick(){_pair.Step(Dt);_scene.GetPhysicsScene().Simulate(Dt);_pair.CapturePhysics();}
        private void Buttons(int actor,int hands)=>_pair.Buttons(actor,(hands&1)!=0,(hands&2)!=0);
        [UnityTearDown] public IEnumerator Cleanup(){if(_scene.IsValid()&&_scene.isLoaded){var op=SceneManager.UnloadSceneAsync(_scene);while(op!=null&&!op.isDone)yield return null;}}
        private static float Elbow(ActiveUpperBodyReview rig,int side=1)
        {
            var suffix=side==0?"L":"R";var u=rig.Find("UpperArm_"+suffix).Body.position;var e=rig.Find("Forearm_"+suffix).Body.position;
            return Vector3.Angle(u-e,rig.HandPosition(side)-e);
        }
        private static float Raise(ActiveUpperBodyReview rig,int side=1)
        {var suffix=side==0?"L":"R";return Vector3.Angle(Vector3.down,rig.Find("Forearm_"+suffix).Body.position-rig.Find("UpperArm_"+suffix).Body.position);}
        private IEnumerator Photo(string path)
        {
            _pair.Present(1);yield return null;
            var rt=new RenderTexture(800,600,24);rt.Create();var image=new Texture2D(800,600,TextureFormat.RGB24,false);
            var view=_pair.View;view.targetTexture=rt;view.aspect=4f/3;view.orthographicSize=.76f;view.Render();
            var old=RenderTexture.active;RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,800,600),0,0);image.Apply();RenderTexture.active=old;
            File.WriteAllBytes(path,image.EncodeToPNG());view.targetTexture=null;rt.Release();UnityEngine.Object.Destroy(rt);UnityEngine.Object.Destroy(image);
        }
        [UnityTest] public IEnumerator InspectMotionProfiles()
        {
            var rows=new List<string>();
            foreach(ActiveGrabPairReview.GrabMotion profile in Enum.GetValues(typeof(ActiveGrabPairReview.GrabMotion)))
            {
                Create(profile,1.2f);Buttons(0,3);
                for(var tick=0;tick<180;tick++)Tick();var rig=_pair.Players[0].Rig;
                rows.Add($"{profile} empty elbow={Elbow(rig):R} raise={Raise(rig):R} hand={rig.HandPosition(1):R} target={rig.GoalHand(1):R}");
                yield return Photo(Path.Combine(Output,profile+"_Reach.png"));
                var op=SceneManager.UnloadSceneAsync(_scene);while(!op.isDone)yield return null;
                foreach(var mode in new[]{"OneHand","TwoHands","Mutual"})
                {
                    Create(profile);Buttons(0,mode=="OneHand"?2:3);if(mode=="Mutual")Buttons(1,3);
                    var skins=_pair.Players.Select(p=>p.Rig.Actor.Pose.GetComponentsInChildren<SkinnedMeshRenderer>().Single(s=>s.name.EndsWith("SkinnedBody"))).ToArray();
                    var mesh=new Mesh();var tri=skins[0].sharedMesh.triangles;var pairs=CameraGaitReviewTests.Pairs(tri);var maxSurface=0f;var maxGrips=0;
                    for(var tick=0;tick<240;tick++)
                    {
                        Input(mode,tick);Tick();maxGrips=Math.Max(maxGrips,_pair.LogicalGrips);
                        if(tick%5==0)foreach(var skin in skins){skin.BakeMesh(mesh);var surface=CameraGaitReviewTests.Surface(mesh.vertices,tri,pairs,out var where);if(surface>maxSurface){maxSurface=surface;rows.Add($"SKIN {profile} {mode} tick={tick} actor={Array.IndexOf(skins,skin)} angle={surface:R} point={where:R}");}}
                        if(tick==100||tick==155)yield return Photo(Path.Combine(Output,$"{profile}_{mode}_{tick}.png"));
                        if(tick%20==0)rows.Add($"{profile} {mode} tick={tick} grips={_pair.LogicalGrips} elbow={Elbow(_pair.Players[0].Rig):R} raise={Raise(_pair.Players[0].Rig):R} gap={_pair.MaximumGripGap:R} surface={maxSurface:R} force={_pair.MaximumGripForce:R}");
                    }
                    rows.Add($"SUMMARY {profile} {mode} maxGrips={maxGrips} finalGrips={_pair.LogicalGrips} maxSurface={maxSurface:R}");
                    UnityEngine.Object.Destroy(mesh);op=SceneManager.UnloadSceneAsync(_scene);while(!op.isDone)yield return null;
                }
            }
            File.WriteAllLines(Path.Combine(Output,"Probe.txt"),rows);
        }
        private void Input(string mode,int tick)
        {
            if(tick==30)_pair.Players[0].Move=Vector2.up*.25f;
            if(tick==60)_pair.Players[0].Move=Vector2.zero;
            if(tick==110)_pair.Players[0].Move=Vector2.down*.45f;
            if(tick==145&&mode=="TwoHands")_pair.Players[1].Move=Vector2.up*.45f;
            if(tick==170){_pair.Players[0].Move=_pair.Players[1].Move=Vector2.zero;Buttons(0,mode=="TwoHands"?2:0);}
            if(tick==200){Buttons(0,0);Buttons(1,0);}
        }
        [UnityTest] public IEnumerator CaptureComparisonAndValidateLoadedSkin()
        {
            var results=new List<CaptureCase>();var failures=new List<string>();
            foreach(ActiveGrabPairReview.GrabMotion profile in Enum.GetValues(typeof(ActiveGrabPairReview.GrabMotion)))
            foreach(var scenario in new[]{"ReachHeight","OneHand","TwoHands","Mutual"})
            {
                Create(profile,scenario=="ReachHeight"?1.2f:.5f);Buttons(0,scenario=="OneHand"?2:3);if(scenario=="Mutual")Buttons(1,3);
                var folder=Path.Combine(Output,"Capture",profile.ToString(),scenario);Directory.CreateDirectory(folder);
                var rt=new RenderTexture(800,600,24);rt.Create();var image=new Texture2D(800,600,TextureFormat.RGB24,false);
                _pair.View.targetTexture=rt;_pair.View.aspect=4f/3;_pair.View.orthographicSize=.76f;
                var skins=_pair.Players.Select(p=>p.Rig.Actor.Pose.GetComponentsInChildren<SkinnedMeshRenderer>().Single(s=>s.name.EndsWith("SkinnedBody"))).ToArray();
                var mesh=new Mesh();var tri=skins[0].sharedMesh.triangles;var pairs=CameraGaitReviewTests.Pairs(tri);
                var result=new CaptureCase{profile=profile.ToString(),scenario=scenario,frames=new CaptureFrame[240]};
                for(var tick=0;tick<240;tick++)
                {
                    if(scenario=="ReachHeight")
                    {
                        _pair.AimHeightOffset=tick<60?0:tick<120?.14f:tick<180?-.14f:0;
                        if(tick==210)Buttons(0,0);
                    }
                    else Input(scenario,tick);
                    Tick();var surface=0f;
                    foreach(var skin in skins){skin.BakeMesh(mesh);surface=Mathf.Max(surface,CameraGaitReviewTests.Surface(mesh.vertices,tri,pairs,out _));}
                    var frame=new CaptureFrame{tick=tick,gripsA=_pair.Players[0].Hands.Count(h=>h.Phase==ActiveGrabPairReview.HandPhase.Holding),gripsB=_pair.Players[1].Hands.Count(h=>h.Phase==ActiveGrabPairReview.HandPhase.Holding),surfaceAngle=surface,elbowDegrees=Elbow(_pair.Players[0].Rig),upperArmRaise=Raise(_pair.Players[0].Rig),handHeight=_pair.Players[0].Rig.HandPosition(1).y,gripGap=_pair.MaximumGripGap,gripForce=_pair.MaximumGripForce};
                    result.frames[tick]=frame;result.maximumSurfaceAngle=Mathf.Max(result.maximumSurfaceAngle,surface);result.maximumGrips=Math.Max(result.maximumGrips,_pair.LogicalGrips);
                    _pair.Present(1);yield return null;_pair.View.Render();var old=RenderTexture.active;RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,800,600),0,0);image.Apply();RenderTexture.active=old;
                    File.WriteAllBytes(Path.Combine(folder,$"Frame_{tick:D4}.png"),image.EncodeToPNG());
                }
                if(scenario!="ReachHeight"&&result.maximumGrips!=(scenario=="Mutual"?4:scenario=="TwoHands"?2:1))failures.Add($"{profile}/{scenario}: missing actual contact");
                if(_pair.LogicalGrips+_pair.PhysicalConnections!=0)failures.Add($"{profile}/{scenario}: release incomplete");
                if(profile!=ActiveGrabPairReview.GrabMotion.Original&&result.maximumSurfaceAngle>=60)failures.Add($"{profile}/{scenario}: skin angle {result.maximumSurfaceAngle:R} exceeds 60-degree gate");
                results.Add(result);File.WriteAllText(Path.Combine(Output,"CaptureMetrics.json"),JsonUtility.ToJson(new CaptureReport{cases=results.ToArray()},true));
                _pair.View.targetTexture=null;rt.Release();UnityEngine.Object.Destroy(rt);UnityEngine.Object.Destroy(image);UnityEngine.Object.Destroy(mesh);
                var op=SceneManager.UnloadSceneAsync(_scene);while(!op.isDone)yield return null;
            }
            Assert.That(failures,Is.Empty,string.Join("\n",failures));
        }
        [UnityTest] public IEnumerator CandidateHoldOwnershipAndCancellationRemainImmediate()
        {
            var rows=new List<string>();
            foreach(var profile in new[]{ActiveGrabPairReview.GrabMotion.ForwardReach,ActiveGrabPairReview.GrabMotion.AdaptiveReach})
            {
                Create(profile);Buttons(0,3);Buttons(1,3);for(var i=0;i<180;i++)Tick();
                Assert.That(_pair.LogicalGrips,Is.EqualTo(4),profile.ToString());
                for(var i=0;i<600;i++)Tick();
                rows.Add($"{profile} idleGrips={_pair.LogicalGrips} force={_pair.MaximumGripForce:R} maxGap={_pair.MaximumGripGap:R} stress={_pair.Players.SelectMany(p=>p.Hands).Max(h=>h.Stress.Value):R}");
                File.WriteAllLines(Path.Combine(Output,"Rules.txt"),rows);
                Assert.That(_pair.LogicalGrips,Is.EqualTo(4),profile+" idle hold");
                Assert.That(_pair.Players.SelectMany(p=>p.Hands).Max(h=>h.Stress.Value),Is.LessThan(.01f));
                foreach(var p in _pair.Players)Assert.That(p.Rig.Actor.Motor.Body.position.y,Is.LessThan(.04f));
                Buttons(0,2);Tick();Assert.That(_pair.LogicalGrips,Is.EqualTo(3));
                Buttons(0,0);Tick();Assert.That(_pair.LogicalGrips,Is.EqualTo(2));
                Buttons(1,0);Tick();Assert.That(_pair.PhysicalConnections,Is.Zero);
                Buttons(0,3);Buttons(1,3);for(var i=0;i<120;i++)Tick();
                _pair.SetSuppressed(true);Tick();Assert.That(_pair.LogicalGrips+_pair.PhysicalConnections,Is.Zero);
                _pair.SetSuppressed(false);for(var i=0;i<30;i++)Tick();Assert.That(_pair.LogicalGrips,Is.Zero);
                Buttons(0,0);Buttons(1,0);Tick();Buttons(0,3);Buttons(1,3);for(var i=0;i<120;i++)Tick();
                Assert.That(_pair.LogicalGrips,Is.EqualTo(4));
                _pair.Players[1].Rig.gameObject.SetActive(false);Tick();Assert.That(_pair.LogicalGrips+_pair.PhysicalConnections,Is.Zero);
                _pair.Players[1].Rig.gameObject.SetActive(true);_pair.ResetReview();for(var i=0;i<30;i++)Tick();Assert.That(_pair.LogicalGrips,Is.Zero);
                var op=SceneManager.UnloadSceneAsync(_scene);while(!op.isDone)yield return null;
            }
        }
        [UnityTest] public IEnumerator CandidatePhysicalReplayIsIndependentOfRenderRate()
        {
            var rows=new List<string>();
            foreach(var profile in new[]{ActiveGrabPairReview.GrabMotion.ForwardReach,ActiveGrabPairReview.GrabMotion.AdaptiveReach})
            {
                var baseline=new List<Vector3[]>();var states=new List<string>();
                foreach(var fps in new[]{60,15,144})
                {
                    Create(profile);Buttons(0,3);Buttons(1,3);var tick=0;var time=0d;var error=0f;
                    while(tick<240)
                    {
                        time=Math.Min(4,time+1d/fps);var due=Math.Min(240,(int)Math.Floor(time*60+1e-8));
                        while(tick<due)
                        {
                            Input("Mutual",tick);Tick();var points=_pair.Players.SelectMany(p=>p.Rig.Links.Select(l=>l.Body.position).Append(p.Rig.Actor.Motor.Body.position)).ToArray();
                            var state=string.Join(",",_pair.Players.SelectMany(p=>p.Hands).Select(h=>h.Phase.ToString()));
                            if(fps==60){baseline.Add(points);states.Add(state);}else{Assert.That(state,Is.EqualTo(states[tick]));for(var i=0;i<points.Length;i++)error=Mathf.Max(error,Vector3.Distance(points[i],baseline[tick][i]));}tick++;
                        }
                        _pair.Present((float)Math.Clamp((time-tick/60d)*60,0,1));
                    }
                    rows.Add($"{profile} fps={fps} maximumPositionDifference={error:R}");File.WriteAllLines(Path.Combine(Output,"FrameRates.txt"),rows);Assert.That(error,Is.LessThan(.0001f));
                    var op=SceneManager.UnloadSceneAsync(_scene);while(!op.isDone)yield return null;
                }
            }
        }
    }
}
