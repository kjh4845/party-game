using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using ProjectHotfix.PhysicsJump;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace ProjectHotfix.Physics.Tests
{
    public sealed class GetUpHandoffTests
    {
        private Scene _scene;
        [Serializable] private sealed class Case { public string name; public float postYawRange, postAngularSpeed, postHeightRange, moveDistance; public int completionStep; }
        [Serializable] private sealed class Report { public Case[] cases; }
        [UnityTearDown] public IEnumerator Cleanup() { if (!_scene.IsValid() || !_scene.isLoaded) yield break; var op=SceneManager.UnloadSceneAsync(_scene); while(op!=null&&!op.isDone) yield return null; }
        [UnityTest] public IEnumerator CompletedGetUpHasNoResidualSpinAndImmediatelyAllowsMovement()
        {
            var cases=new List<Case>();
            foreach(var name in new[] { "FaceUp", "FaceDown", "LeftSide", "RightSide" })
            {
                _scene=SceneManager.CreateScene(name,new CreateSceneParameters(LocalPhysicsMode.Physics3D)); var physics=_scene.GetPhysicsScene();
                var actor=PhysicsJumpReview.Create(new Vector3(0,.65f,0)); SceneManager.MoveGameObjectToScene(actor.gameObject,_scene);
                actor.ReadKeyboard=false; actor.Motor.ManualStep=true; actor.Motor.Body.interpolation=RigidbodyInterpolation.None;
                actor.transform.rotation=Quaternion.Euler(0,25,0)*(name=="FaceUp" ? Quaternion.Euler(-90,0,0) : name=="FaceDown" ? Quaternion.Euler(90,0,0) : Quaternion.Euler(0,0,name=="LeftSide" ? 90 : -90)); actor.Motor.Body.rotation=actor.transform.rotation;
                var floor=GameObject.CreatePrimitive(PrimitiveType.Cube); SceneManager.MoveGameObjectToScene(floor,_scene); floor.transform.position=new Vector3(0,-.1f,0); floor.transform.localScale=new Vector3(20,.2f,20); UnityEngine.Physics.SyncTransforms();
                var rag=actor.gameObject.AddComponent<RagdollRecoveryReview>(); rag.Bind(actor); rag.BeginDown(Vector3.zero,Vector3.zero);
                var result=new Case { name=name, completionStep=-1 }; float yaw=0,minYaw=0,maxYaw=0,minY=0,maxY=0; var startMove=Vector3.zero;
                for(var step=0;step<720;step++)
                {
                    if(result.completionStep>=0 && step==result.completionStep+60) startMove=actor.Motor.Body.position;
                    if(result.completionStep>=0 && step>=result.completionStep+60) actor.Motor.MoveInput=Vector2.right;
                    rag.Down.Step(1f/60); rag.Step(1f/60); actor.Motor.Step(1f/60); actor.Hands.Step(1f/60); physics.Simulate(1f/60);
                    if(rag.Active) rag.ApplyPose(); else actor.ApplyPresentation(1f/60);
                    if(rag.Recoveries==1 && result.completionStep<0) { result.completionStep=step; yaw=actor.Motor.Body.rotation.eulerAngles.y; minY=maxY=actor.Motor.Body.position.y; }
                    if(result.completionStep>=0 && step<result.completionStep+60)
                    {
                        var delta=Mathf.DeltaAngle(yaw,actor.Motor.Body.rotation.eulerAngles.y); minYaw=Mathf.Min(minYaw,delta); maxYaw=Mathf.Max(maxYaw,delta);
                        result.postAngularSpeed=Mathf.Max(result.postAngularSpeed,actor.Motor.Body.angularVelocity.magnitude);
                        minY=Mathf.Min(minY,actor.Motor.Body.position.y); maxY=Mathf.Max(maxY,actor.Motor.Body.position.y);
                    }
                    if(result.completionStep>=0 && step==result.completionStep+90) { result.moveDistance=Vector3.Distance(startMove,actor.Motor.Body.position); break; }
                }
                Assert.That(result.completionStep,Is.GreaterThanOrEqualTo(0)); Assert.That(result.moveDistance,Is.GreaterThan(.2f));
                result.postYawRange=maxYaw-minYaw; result.postHeightRange=maxY-minY; cases.Add(result);
                Assert.That(result.postYawRange,Is.LessThan(1f),name+" post-recovery yaw");
                Assert.That(result.postAngularSpeed,Is.LessThan(.2f),name+" post-recovery spin");
                Assert.That(result.postHeightRange,Is.LessThan(.001f),name+" post-recovery bob");
                var op=SceneManager.UnloadSceneAsync(_scene); while(op!=null&&!op.isDone) yield return null;
            }
            var folder=Path.GetFullPath(Path.Combine(Application.dataPath,"../../.codex_tmp/getup-handoff-20260907")); Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder,"Handoff.json"),JsonUtility.ToJson(new Report { cases=cases.ToArray() },true));
        }
    }
}
