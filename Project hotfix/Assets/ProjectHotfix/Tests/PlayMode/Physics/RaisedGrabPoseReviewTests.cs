using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using ProjectHotfix.Contracts;
using ProjectHotfix.PhysicsJump;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace ProjectHotfix.Physics.Tests
{
    // Isolated reach/skin feasibility, deliberately separate from grip admission
    // and any decision to lift a held player automatically.
    public sealed class RaisedGrabPoseReviewTests
    {
        private Scene _scene;
        private string Output=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../.codex_tmp/raised-grab-20260911"));
        [Serializable] private sealed class Row { public float height,forward,clavicleLift,surface,handError,reachCorrection;public Vector3 worstPoint; }
        [Serializable] private sealed class Report { public string status;public Row[] cases; }
        [UnityTearDown] public IEnumerator Cleanup()
        {if(!_scene.IsValid()||!_scene.isLoaded)yield break;var op=SceneManager.UnloadSceneAsync(_scene);while(op!=null&&!op.isDone)yield return null;}
        [UnityTest] public IEnumerator MeasureCurrentRigAtForwardAndRaisedContacts()
        {
            Directory.CreateDirectory(Output);var rows=new List<Row>();
            foreach(var target in new[]{new Vector2(.54f,.32f),new Vector2(.72f,.27f),new Vector2(.84f,.22f),new Vector2(.90f,.18f)})foreach(var lift in new[]{0f,1f})
            {
                _scene=SceneManager.CreateScene("Raised hand reach",new CreateSceneParameters(LocalPhysicsMode.Physics3D));
                var actor=PhysicsJumpReview.Create(Vector3.zero,true,PhysicsJumpReview.LatWeightTransportPath);SceneManager.MoveGameObjectToScene(actor.gameObject,_scene);
                actor.ReadKeyboard=false;actor.Motor.ManualStep=true;actor.Motor.Body.isKinematic=true;actor.EnableGrabReview();
                var skin=actor.Pose.GetComponentsInChildren<SkinnedMeshRenderer>().Single(x=>x.name.EndsWith("SkinnedBody"));var mesh=new Mesh();var triangles=skin.sharedMesh.triangles;var pairs=CameraGaitReviewTests.Pairs(triangles);
                var row=new Row{height=target.x,forward=target.y,clavicleLift=lift};
                for(var tick=0;tick<60;tick++)
                {
                    var weight=Mathf.SmoothStep(0,1,Mathf.Clamp01(tick/36f));
                    actor.GrabPose.Apply(new GrabPresentationState(new GrabHandPresentationState(true,true,-.12f,target.x,target.y,weight,lift),new GrabHandPresentationState(true,true,.12f,target.x,target.y,weight,lift),0),1f/60,true,true,false,false);
                    if(tick%3==0)
                    {
                        skin.BakeMesh(mesh);var angle=CameraGaitReviewTests.Surface(mesh.vertices,triangles,pairs,out var point);
                        if(angle>row.surface){row.surface=angle;row.worstPoint=point;}
                    }
                    row.handError=Mathf.Max(row.handError,actor.GrabPose.MaximumHeldTargetError);row.reachCorrection=Mathf.Max(row.reachCorrection,actor.GrabPose.MaximumReachCorrection);
                }
                if((target.x<.6f&&lift==0)||(target.x>.8f&&lift==1))
                {
                    var viewObject=new GameObject("PoseReviewCamera");SceneManager.MoveGameObjectToScene(viewObject,_scene);var camera=viewObject.AddComponent<Camera>();camera.enabled=false;
                    camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.10f,.15f,.18f);camera.orthographic=true;camera.orthographicSize=.63f;camera.aspect=.75f;
                    camera.transform.position=new Vector3(1.4f,1.0f,1.5f);camera.transform.LookAt(new Vector3(0,.51f,0));
                    var lightObject=new GameObject("PoseReviewLight");SceneManager.MoveGameObjectToScene(lightObject,_scene);var light=lightObject.AddComponent<Light>();light.type=LightType.Directional;light.intensity=1.1f;light.transform.rotation=Quaternion.Euler(35,-25,0);
                    foreach(var x in new[]{-.12f,.12f})
                    {
                        var marker=GameObject.CreatePrimitive(PrimitiveType.Sphere);SceneManager.MoveGameObjectToScene(marker,_scene);marker.name="PoseOnlyContactMarker";
                        marker.transform.position=new Vector3(x,target.x,target.y);marker.transform.localScale=Vector3.one*.028f;marker.GetComponent<Collider>().enabled=false;
                        marker.GetComponent<Renderer>().material.SetColor("_BaseColor",new Color(.06f,.85f,.8f));
                    }
                    var rt=new RenderTexture(480,640,24);rt.Create();camera.targetTexture=rt;var image=new Texture2D(480,640,TextureFormat.RGB24,false);
                    yield return null;camera.Render();var old=RenderTexture.active;RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,480,640),0,0);image.Apply();RenderTexture.active=old;
                    File.WriteAllBytes(Path.Combine(Output,$"Pose_{Mathf.RoundToInt(target.x*100):D2}.png"),image.EncodeToPNG());
                    camera.targetTexture=null;rt.Release();UnityEngine.Object.Destroy(rt);UnityEngine.Object.Destroy(image);
                }
                rows.Add(row);File.WriteAllText(Path.Combine(Output,"ReachFeasibility.json"),JsonUtility.ToJson(new Report{status="MEASURED_NOT_ADOPTED",cases=rows.ToArray()},true));
                Assert.That(row.handError,Is.LessThan(.001f));
                UnityEngine.Object.Destroy(mesh);var op=SceneManager.UnloadSceneAsync(_scene);while(op!=null&&!op.isDone)yield return null;
            }
        }
    }
}
