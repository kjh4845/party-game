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
    public sealed partial class ShoulderReachCandidateTests
    {
        private const float Dt=1f/60;
        private Scene _scene;
        private ActiveGrabPairReview _pair;
        private string Output=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../.codex_tmp/shoulder-reach-20260916"));
        private string Candidate=>Path.Combine(Output,"candidate-r02/ShoulderCharacter.bytes");
        private void Create(bool repaired,float distance=.5f)
        {
            _scene=SceneManager.CreateScene("Shoulder repair "+repaired,new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            _pair=ActiveGrabPairReview.Create(_scene,distance,ActiveGrabPairReview.GrabMotion.AdaptiveReach,repaired?Candidate:null);
            _pair.ManualStep=true;_pair.ReadKeyboard=false;_pair.View.enabled=false;for(var i=0;i<60;i++)Tick();
        }
        private void Tick(){_pair.Step(Dt);_scene.GetPhysicsScene().Simulate(Dt);_pair.CapturePhysics();}
        private void Buttons(int actor,int mask)=>_pair.Buttons(actor,(mask&1)!=0,(mask&2)!=0);
        [UnityTearDown] public IEnumerator Cleanup(){if(_scene.IsValid()&&_scene.isLoaded){var op=SceneManager.UnloadSceneAsync(_scene);while(!op.isDone)yield return null;}}
        private void Input(string mode,int tick)
        {
            if(mode=="ReachHeight")
            {
                _pair.AimHeightOffset=tick<60?0:tick<120?.14f:tick<180?-.14f:0;if(tick==210)Buttons(0,0);return;
            }
            if(tick==30)_pair.Players[0].Move=Vector2.up*.25f;if(tick==60)_pair.Players[0].Move=Vector2.zero;
            if(tick==110)_pair.Players[0].Move=Vector2.down*.45f;if(tick==145&&mode=="TwoHands")_pair.Players[1].Move=Vector2.up*.45f;
            if(tick==170){_pair.Players[0].Move=_pair.Players[1].Move=Vector2.zero;Buttons(0,mode=="TwoHands"?2:0);}if(tick==200){Buttons(0,0);Buttons(1,0);}
        }
        [UnityTest] public IEnumerator ShoulderHelpersPreservePhysicsAndPassSkin()
        {
            var rows=new List<string>();var failures=new List<string>();
            foreach(var mode in new[]{"ReachHeight","OneHand","TwoHands","Mutual"})
            {
                var positions=new List<Vector3[]>();var states=new List<string>();
                foreach(var repaired in new[]{false,true})
                {
                    Create(repaired,mode=="ReachHeight"?1.2f:.5f);Buttons(0,mode=="OneHand"?2:3);if(mode=="Mutual")Buttons(1,3);
                    var skins=_pair.Players.Select(p=>p.Rig.Actor.Pose.GetComponentsInChildren<SkinnedMeshRenderer>().Single(s=>s.name.EndsWith("SkinnedBody"))).ToArray();var mesh=new Mesh();var tri=skins[0].sharedMesh.triangles;var pairs=CameraGaitReviewTests.Pairs(tri);var worst=0f;var error=0f;var at=-1;var maxGrips=0;
                    for(var tick=0;tick<240;tick++)
                    {
                        Input(mode,tick);Tick();maxGrips=Math.Max(maxGrips,_pair.LogicalGrips);
                        var points=_pair.Players.SelectMany(p=>p.Rig.Links.Select(l=>l.Body.position).Append(p.Rig.Actor.Motor.Body.position)).ToArray();var state=string.Join(",",_pair.Players.SelectMany(p=>p.Hands).Select(h=>h.Phase.ToString()));
                        if(!repaired){positions.Add(points);states.Add(state);}else{Assert.That(state,Is.EqualTo(states[tick]));for(var i=0;i<points.Length;i++)error=Mathf.Max(error,Vector3.Distance(points[i],positions[tick][i]));}
                        foreach(var skin in skins){skin.BakeMesh(mesh);var angle=CameraGaitReviewTests.Surface(mesh.vertices,tri,pairs,out var where);if(angle>worst){worst=angle;at=tick;if(repaired&&angle>60)rows.Add($"SKIN {mode} tick={tick} actor={Array.IndexOf(skins,skin)} angle={angle:R} point={where:R}");}}
                    }
                    rows.Add($"{mode} repaired={repaired} maxSurface={worst:R} tick={at} physicsDifference={error:R} maxGrips={maxGrips} finalGrips={_pair.LogicalGrips}");File.WriteAllLines(Path.Combine(Output,"NativeSkin.txt"),rows);
                    if(repaired&&worst>=60)failures.Add(mode+" skin="+worst);
                    Assert.That(error,Is.LessThan(.0001f));Assert.That(_pair.LogicalGrips,Is.Zero);foreach(var p in _pair.Players){Assert.That(p.Rig.TotalMass,Is.EqualTo(20));Assert.That(p.Rig.Links.Count,Is.EqualTo(7));}
                    UnityEngine.Object.Destroy(mesh);var op=SceneManager.UnloadSceneAsync(_scene);while(!op.isDone)yield return null;
                }
            }
            Assert.That(failures,Is.Empty,string.Join("\n",failures));
        }
    }
}
