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
    public sealed class ContactMotionReviewTests
    {
        private const float Dt=1f/60;
        private Scene _scene;
        private SharedArenaReview _arena;
        private ArenaBoundaryReview _boundary;
        private string Output=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../.codex_tmp/contact-motion-20260913"));
        private PhysicsJumpReview A=>_arena.Actors[0];
        [Serializable] private sealed class BoneRow {public string name;public Vector3 position;public Quaternion rotation;}
        [Serializable] private sealed class PoseRow {public Vector3 root;public Quaternion rotation;public BoneRow[] bones;public Vector3[] tips,contacts,vertices;public int[] armVertices;}
        [Serializable] private sealed class PoseReport {public PoseRow[] actors;}
        [UnityTearDown] public IEnumerator Cleanup()
        {if(!_scene.IsValid()||!_scene.isLoaded)yield break;var op=SceneManager.UnloadSceneAsync(_scene);while(op!=null&&!op.isDone)yield return null;}
        private void Create(bool wall)
        {
            _scene=SceneManager.CreateScene("Contact motion review",new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            if(wall){_boundary=LedgeRecoveryReview.Create(_scene,2);_boundary.ManualStep=true;_arena=_boundary.Arena;}
            else _arena=SharedArenaReview.Create(_scene,2,true,2,new[]{new Vector3(0,.02f,-.22f),new Vector3(0,.02f,.22f)});
            _arena.ReadKeyboard=_arena.ShowHelp=false;_arena.SharedCamera.ManualStep=true;_arena.SharedCamera.View.enabled=false;
            foreach(var a in _arena.Actors){a.ReadKeyboard=false;a.Motor.ManualStep=true;a.Hands.Grabs.EnableRaisedGrab();a.Hands.SubmitButtons(false,false);}
            _arena.InitializeParticipants();Directory.CreateDirectory(Output);for(var i=0;i<60;i++)Tick();
        }
        private void Tick()
        {
            foreach(var a in _arena.Actors){var r=a.GetComponent<PunchDownReview>();r.Ragdoll.Down.Step(Dt);r.Ragdoll.Step(Dt);r.Step();}
            foreach(var a in _arena.Actors)a.Motor.Step(Dt);
            foreach(var a in _arena.Actors)a.Hands.Step(Dt);
            GrabPhysicsReview.StepAutomaticLifts(_scene,Dt);_scene.GetPhysicsScene().Simulate(Dt);
            foreach(var a in _arena.Actors)a.GetComponent<LedgeGrabReview>()?.AfterPhysics();
            _boundary?.StepAfterPhysics();foreach(var a in _arena.Actors)a.FixedPose.Advance(Dt);
        }
        private void Dump(string name)
        {
            var rows=new List<PoseRow>();
            foreach(var a in _arena.Actors)
            {
                var skin=a.Pose.GetComponentsInChildren<SkinnedMeshRenderer>().Single(x=>x.name.EndsWith("SkinnedBody"));
                var mesh=new Mesh();skin.BakeMesh(mesh);var weights=skin.sharedMesh.boneWeights;
                bool Arm(int index)=>skin.bones[index].name.StartsWith("Forearm")||skin.bones[index].name.StartsWith("UpperArm")||skin.bones[index].name.StartsWith("HandLogical");
                rows.Add(new PoseRow{root=a.Motor.Body.position,rotation=a.Motor.Body.rotation,
                    bones=skin.bones.Select(b=>new BoneRow{name=b.name,position=b.position,rotation=b.rotation}).ToArray(),
                    tips=new[]{a.GrabPose.Terminal(0),a.GrabPose.Terminal(1)},contacts=new[]{a.Hands.Grabs.ContactPoint(0),a.Hands.Grabs.ContactPoint(1)},
                    vertices=mesh.vertices.Select(skin.transform.TransformPoint).ToArray(),armVertices=Enumerable.Range(0,weights.Length).Where(i=>
                        (Arm(weights[i].boneIndex0)?weights[i].weight0:0)+(Arm(weights[i].boneIndex1)?weights[i].weight1:0)+(Arm(weights[i].boneIndex2)?weights[i].weight2:0)+(Arm(weights[i].boneIndex3)?weights[i].weight3:0)>.5f).ToArray()});
                UnityEngine.Object.Destroy(mesh);
            }
            File.WriteAllText(Path.Combine(Output,name+".json"),JsonUtility.ToJson(new PoseReport{actors=rows.ToArray()},true));
        }
        [UnityTest] public IEnumerator RecordApprovedContactGeometry()
        {
            Create(false);foreach(var a in _arena.Actors)a.Hands.SubmitButtons(true,true);for(var i=0;i<50;i++)Tick();
            Assert.That(A.Hands.Grabs.MutualLiftBlocked(0),Is.True);Dump("BaselineMutual");
            var op=SceneManager.UnloadSceneAsync(_scene);while(op!=null&&!op.isDone)yield return null;_boundary=null;
            Create(true);A.Hands.SubmitButtons(true,true);for(var i=0;i<20;i++)Tick();
            var position=new Vector3(0,-.6f,-_boundary.DepthCU*.5f-.23f);A.transform.SetPositionAndRotation(position,Quaternion.identity);
            A.Motor.Body.position=position;A.Motor.Body.rotation=Quaternion.identity;A.Motor.Body.linearVelocity=Vector3.down*2;
            A.Motor.MoveInput=Vector2.up;A.Motor.SprintRequested=true;UnityEngine.Physics.SyncTransforms();A.FixedPose.RestoreForEvaluation();
            var ledge=A.GetComponent<LedgeGrabReview>();for(var i=0;i<50&&!ledge.IsHolding(0);i++)Tick();Assert.That(ledge.IsHolding(0),Is.True);
            A.Motor.MoveInput=Vector2.zero;for(var i=0;i<20;i++)Tick();Dump("BaselineWall");yield return null;
        }
    }
}
