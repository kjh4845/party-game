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
    public sealed partial class StandingSkinContactTests
    {
        private const float Dt=1f/60;
        private Scene _scene;
        private ActiveGrabPairReview _pair;
        private string Output=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../.codex_tmp/standing-contact-20260917"));
        private string Model=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../.codex_tmp/shoulder-reach-20260916/candidate-r02/ShoulderCharacter.bytes"));
        private void Create(bool candidate=true,bool lift=false,float distance=.5f)
        {
            Directory.CreateDirectory(Output);_scene=SceneManager.CreateScene("Standing contact",new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            _pair=ActiveGrabPairReview.Create(_scene,distance,ActiveGrabPairReview.GrabMotion.AdaptiveReach,Model,matchHandEnvelope:true);
            _pair.ManualStep=true;_pair.ReadKeyboard=false;_pair.View.enabled=false;_pair.EnableStandingSkinContacts=candidate;_pair.EnableLiftThrow=lift;
            for(var i=0;i<60;i++)Tick();
        }
        private void Tick(bool reverse=false){_pair.Step(Dt,reverse);_scene.GetPhysicsScene().Simulate(Dt);_pair.CapturePhysics();}
        private void Buttons(int player,int mask)=>_pair.Buttons(player,(mask&1)!=0,(mask&2)!=0);
        [UnityTearDown] public IEnumerator Cleanup(){if(_scene.IsValid()&&_scene.isLoaded){var op=SceneManager.UnloadSceneAsync(_scene);while(!op.isDone)yield return null;}}
        private void Input(string mode,int tick)
        {
            if(tick==0){Buttons(0,mode=="LeftHand"?1:mode=="RightHand"?2:3);if(mode.StartsWith("Mutual"))Buttons(1,mode=="MutualOne"?2:3);if(mode=="MutualOne")Buttons(0,1);}
            if(tick==30)_pair.Players[0].Move=Vector2.up*.25f;if(tick==60)_pair.Players[0].Move=Vector2.zero;
            if(tick==130)_pair.Players[0].Move=Vector2.down*.3f;
            if(tick==180){_pair.Players[0].Move=Vector2.zero;if(mode=="TwoHands"||mode=="Carry")Buttons(0,2);}
            if(tick==220)Buttons(0,0);if(tick==260)Buttons(1,0);
        }
        private IEnumerator Photo(string name)
        {
            _pair.Present(1);yield return null;var rt=new RenderTexture(960,640,24);rt.Create();var image=new Texture2D(960,640,TextureFormat.RGB24,false);
            _pair.View.targetTexture=rt;_pair.View.aspect=1.5f;_pair.View.orthographicSize=.8f;_pair.View.Render();var previous=RenderTexture.active;RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,960,640),0,0);image.Apply();RenderTexture.active=previous;
            File.WriteAllBytes(Path.Combine(Output,name+".png"),image.EncodeToPNG());_pair.View.targetTexture=null;rt.Release();UnityEngine.Object.Destroy(rt);UnityEngine.Object.Destroy(image);
        }
        [UnityTest] public IEnumerator ProbeStandingContacts()
        {
            var rows=new List<string>();var failures=new List<string>();
            foreach(var mode in new[]{"LeftHand","RightHand","TwoHands","MutualOne","MutualBoth","MutualLift","Carry"})
            {
                Create(lift:mode=="MutualLift"||mode=="Carry");var maximum=0;var skin=0f;var mesh=new Mesh();var renderers=_pair.Players.Select(p=>p.Rig.Actor.Pose.GetComponentsInChildren<SkinnedMeshRenderer>().Single(s=>s.name.EndsWith("SkinnedBody"))).ToArray();var tri=renderers[0].sharedMesh.triangles;var pairs=CameraGaitReviewTests.Pairs(tri);
                for(var tick=0;tick<300;tick++)
                {
                    Input(mode,tick);Tick();maximum=Math.Max(maximum,_pair.LogicalGrips);
                    if(tick%5==0)foreach(var renderer in renderers){renderer.BakeMesh(mesh);skin=Mathf.Max(skin,CameraGaitReviewTests.Surface(mesh.vertices,tri,pairs,out _));}
                    if(tick%20==0)rows.Add($"{mode} t={tick} logical={_pair.LogicalGrips} physical={_pair.PhysicalConnections} mutual={_pair.Players[0].Mutual} rolling={_pair.Players.SelectMany(p=>p.Hands).Count(h=>h.Connection!=null&&h.Connection.RollingHands)} targets={string.Join(",",_pair.Players.SelectMany(p=>p.Hands).Select(h=>h.TargetBody!=null?h.TargetBody.name:"-"))} gap={_pair.MaximumGripGap:R} force={_pair.MaximumGripForce:R} last={_pair.Players[0].LastRelease}/{_pair.Players[1].LastRelease}");
                    if(tick==110||tick==210)yield return Photo(mode+"_"+tick);
                }
                rows.Add($"SUMMARY {mode} maxGrips={maximum} skin={skin:R} throws={_pair.Throws}");File.WriteAllLines(Path.Combine(Output,"Probe.txt"),rows);
                var expected=mode=="LeftHand"||mode=="RightHand"?1:mode=="TwoHands"||mode=="MutualOne"||mode=="Carry"?2:4;
                if(maximum<expected)failures.Add(mode+" grips "+maximum);if(skin>=60)failures.Add(mode+" skin "+skin);Assert.That(_pair.LogicalGrips+_pair.PhysicalConnections,Is.Zero);
                UnityEngine.Object.Destroy(mesh);var op=SceneManager.UnloadSceneAsync(_scene);while(!op.isDone)yield return null;
            }
            Assert.That(failures,Is.Empty,string.Join("\n",failures));
        }
    }
}
