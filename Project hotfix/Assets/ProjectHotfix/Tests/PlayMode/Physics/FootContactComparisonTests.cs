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
    public sealed class FootContactComparisonTests
    {
        private Scene _scene;
        [Serializable] private sealed class Result
        {
            public string style, mode;
            public float configuredSpeed, configuredCadence, meanRootSpeed, meanStanceSlipSpeed;
            public float maximumJointStep, maximumReachCorrection, maximumFootError, maximumPenetration;
            public int measuredStanceIntervals;
        }
        [Serializable] private sealed class Report
        {
            public string status, outputDirectory;
            public bool appliedToDefault;
            public Result[] cases;
        }

        [UnityTearDown]
        public IEnumerator CleanUp()
        {
            if (!_scene.IsValid() || !_scene.isLoaded) yield break;
            var unload = SceneManager.UnloadSceneAsync(_scene);
            while (unload != null && !unload.isDone) yield return null;
        }

        [UnityTest]
        public IEnumerator CompareSteadyContactStyles_WithoutChangingDefaults()
        {
            if (!File.Exists(PhysicsJumpReview.TransportPath)) Assert.Ignore("Local review transport missing.");
            var baseDir = Environment.GetEnvironmentVariable("PHX_FOOT_CONTACT_OUTPUT")
                ?? Path.GetFullPath(Path.Combine(Application.dataPath, "../../.codex_tmp/foot-contact-20260905"));
            var output = Path.Combine(baseDir, "Compare-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));
            Directory.CreateDirectory(output); var results = new List<Result>();
            foreach (var mode in new[] { "Walk", "Sprint" })
            foreach (var style in new[] { "Current", "FastFeet", "SlowerTravel" })
            {
                _scene = SceneManager.CreateScene("FootContact-"+style+mode, new CreateSceneParameters(LocalPhysicsMode.Physics3D));
                var physics = _scene.GetPhysicsScene();
                var actor = PhysicsJumpReview.Create(new Vector3(0,.02f,0), false); SceneManager.MoveGameObjectToScene(actor.gameObject,_scene);
                actor.ReadKeyboard = false; actor.Motor.ManualStep = true; actor.Motor.Body.interpolation = RigidbodyInterpolation.None;
                actor.Pose.ReadableGait = false; // Historical speed/rhythm comparison, not the revised default.
                if (style != "Current")
                {
                    actor.Pose.SpeedMatchedGait = true;
                    actor.Pose.WalkPelvisDrop = actor.Pose.SprintPelvisDrop = .02f;
                    if (style == "FastFeet") { actor.Pose.WalkCadence = 6f; actor.Pose.SprintCadence = 9f; }
                    else
                    {
                        actor.Motor.MoveSpeed = .4f;
                        actor.Pose.WalkReferenceSpeed = .4f; actor.Pose.SprintReferenceSpeed = .6f;
                    }
                }
                actor.Motor.SprintRequested = mode == "Sprint";
                var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); SceneManager.MoveGameObjectToScene(floor,_scene);
                floor.transform.position = new Vector3(0,-.1f,0); floor.transform.localScale = new Vector3(30,.2f,30);
                var material = new Material(Shader.Find("Universal Render Pipeline/Lit")); material.SetColor("_BaseColor",new Color(.13f,.2f,.28f)); floor.GetComponent<Renderer>().sharedMaterial = material;
                var grid = new Material(Shader.Find("Universal Render Pipeline/Unlit")); grid.SetColor("_BaseColor",new Color(.07f,.11f,.17f));
                for (var i=-20;i<=20;i++)
                {
                    var line=GameObject.CreatePrimitive(PrimitiveType.Cube);SceneManager.MoveGameObjectToScene(line,_scene);
                    line.GetComponent<Collider>().enabled=false;line.transform.position=new Vector3(0,.002f,i*.25f);
                    line.transform.localScale=new Vector3(10,.002f,.004f);line.GetComponent<Renderer>().sharedMaterial=grid;
                }
                var lightObj=new GameObject("Light");SceneManager.MoveGameObjectToScene(lightObj,_scene);var light=lightObj.AddComponent<Light>();light.type=LightType.Directional;light.intensity=2.5f;light.shadows=LightShadows.Soft;light.transform.rotation=Quaternion.Euler(45,-35,0);
                var cameraObj=new GameObject("Camera");SceneManager.MoveGameObjectToScene(cameraObj,_scene);var camera=cameraObj.AddComponent<Camera>();camera.enabled=false;camera.orthographic=true;camera.orthographicSize=.75f;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.07f,.09f,.13f);
                var targetTexture=new RenderTexture(384,448,24);targetTexture.Create();camera.targetTexture=targetTexture;
                var image=new Texture2D(384,448,TextureFormat.RGB24,false);var baked=new Mesh();
                var fixedObject=new GameObject("FixedCamera");SceneManager.MoveGameObjectToScene(fixedObject,_scene);
                var fixedCamera=fixedObject.AddComponent<Camera>();fixedCamera.enabled=false;fixedCamera.orthographic=true;
                fixedCamera.orthographicSize=1f;fixedCamera.clearFlags=CameraClearFlags.SolidColor;fixedCamera.backgroundColor=camera.backgroundColor;
                var fixedTarget=new RenderTexture(1280,512,24);fixedTarget.Create();fixedCamera.targetTexture=fixedTarget;
                var fixedImage=new Texture2D(1280,512,TextureFormat.RGB24,false);
                var transforms=actor.GetComponentsInChildren<Transform>();
                var feet=new[]{transforms.Single(t=>t.name=="Foot_L"),transforms.Single(t=>t.name=="Foot_R")};
                var joints=transforms.Where(t=>t.name.StartsWith("Thigh_")||t.name.StartsWith("Calf_")||t.name.StartsWith("Foot_")).ToArray();
                var previousRotations=joints.Select(t=>t.localRotation).ToArray();var previousFeet=feet.Select(t=>t.position).ToArray();var wasStance=new bool[2];
                var skin=actor.GetComponentsInChildren<SkinnedMeshRenderer>().Single(s=>s.name.EndsWith("SkinnedBody"));
                var result=new Result {style=style,mode=mode,configuredSpeed=actor.Motor.MoveSpeed*(mode=="Sprint"?1.5f:1f),configuredCadence=mode=="Sprint"?actor.Pose.SprintCadence:actor.Pose.WalkCadence};
                float slip=0,rootSpeed=0;int measuredFrames=0;
                for(var step=0;step<240;step++)
                {
                    actor.Motor.MoveInput=step>=20?Vector2.up:Vector2.zero;
                    actor.Motor.Step(1f/60f);physics.Simulate(1f/60f);actor.ApplyPresentation(1f/60f);
                    var poseFrame = Time.frameCount;
                    // Bone transforms and Camera.Render alone do not refresh the
                    // rendered skin repeatedly within one Unity player-loop frame.
                    yield return null;
                    for(var i=0;i<joints.Length;i++)
                    {
                        if(step>=120)result.maximumJointStep=Mathf.Max(result.maximumJointStep,Quaternion.Angle(previousRotations[i],joints[i].localRotation));
                        previousRotations[i]=joints[i].localRotation;
                    }
                    for(var i=0;i<2;i++)
                    {
                        var stance=actor.Motor.Grounded && Mathf.Cos((actor.Pose.GaitPhase+i*.5f)*Mathf.PI*2f)<=0f;
                        if(step>=120 && stance && wasStance[i])
                        {
                            slip+=Vector3.ProjectOnPlane(feet[i].position-previousFeet[i],Vector3.up).magnitude*60f;
                            result.measuredStanceIntervals++;
                        }
                        previousFeet[i]=feet[i].position;wasStance[i]=stance;
                    }
                    if(step>=120)
                    {
                        rootSpeed+=Vector3.ProjectOnPlane(actor.Motor.Body.linearVelocity,Vector3.up).magnitude;measuredFrames++;
                        result.maximumReachCorrection=Mathf.Max(result.maximumReachCorrection,actor.Pose.MaximumReachCorrection);
                        result.maximumFootError=Mathf.Max(result.maximumFootError,actor.Pose.MaximumFootTargetError);
                    }
                    if(step>=60 && step<180)
                    {
                        Assert.That(Time.frameCount,Is.GreaterThan(poseFrame),"Capture must follow a skinning frame, not just a bone update");
                        var center=new Vector3(actor.Motor.Body.position.x,.55f,actor.Motor.Body.position.z);
                        camera.transform.position=center+new Vector3(3f,.45f,1.6f);camera.transform.LookAt(center);
                        camera.Render();var old=RenderTexture.active;RenderTexture.active=targetTexture;
                        image.ReadPixels(new Rect(0,0,384,448),0,0);image.Apply();RenderTexture.active=old;
                        File.WriteAllBytes(Path.Combine(output,$"{style}_{mode}_{step-60:D3}.png"),image.EncodeToPNG());
                        if(style=="Current")
                        {
                            if(step==60)
                            {
                                var fixedCenter=new Vector3(0,.55f,actor.Motor.Body.position.z+result.configuredSpeed);
                                fixedCamera.transform.position=fixedCenter+new Vector3(5f,.8f,1f);fixedCamera.transform.LookAt(fixedCenter);
                            }
                            fixedCamera.Render();old=RenderTexture.active;RenderTexture.active=fixedTarget;
                            fixedImage.ReadPixels(new Rect(0,0,1280,512),0,0);fixedImage.Apply();RenderTexture.active=old;
                            File.WriteAllBytes(Path.Combine(output,$"Fixed_{mode}_{step-60:D3}.png"),fixedImage.EncodeToPNG());
                        }
                    }
                    if(step>=120 && step%3==0)
                    {
                        skin.BakeMesh(baked);var min=baked.vertices.Min(v=>skin.transform.TransformPoint(v).y);
                        result.maximumPenetration=Mathf.Max(result.maximumPenetration,-min);
                    }
                }
                result.meanRootSpeed=rootSpeed/measuredFrames;result.meanStanceSlipSpeed=slip/result.measuredStanceIntervals;results.Add(result);
                Debug.Log($"FOOT_STYLE {style} {mode} root={result.meanRootSpeed:F3} slip={result.meanStanceSlipSpeed:F5} joint={result.maximumJointStep:F2} reach={result.maximumReachCorrection:G3} ground={result.maximumPenetration:G3}");
                File.WriteAllText(Path.Combine(output,"FootContactComparison.json"),JsonUtility.ToJson(new Report {status="OBSERVED",outputDirectory=output,appliedToDefault=false,cases=results.ToArray()},true));
                camera.targetTexture=null;targetTexture.Release();UnityEngine.Object.Destroy(targetTexture);UnityEngine.Object.Destroy(image);UnityEngine.Object.Destroy(baked);UnityEngine.Object.Destroy(material);UnityEngine.Object.Destroy(grid);
                fixedCamera.targetTexture=null;fixedTarget.Release();UnityEngine.Object.Destroy(fixedTarget);UnityEngine.Object.Destroy(fixedImage);
                var unload=SceneManager.UnloadSceneAsync(_scene);while(unload!=null&&!unload.isDone)yield return null;
            }
            File.WriteAllText(Path.Combine(baseDir,"LatestComparisonPath.txt"),output);
            foreach(var c in results)
            {
                Assert.That(c.measuredStanceIntervals,Is.GreaterThan(20),c.style+c.mode);
                Assert.That(c.maximumFootError,Is.LessThan(.00001f),c.style+c.mode);
                Assert.That(c.maximumReachCorrection,Is.LessThan(.003f),c.style+c.mode);
                if(c.style!="Current")Assert.That(c.meanStanceSlipSpeed,Is.LessThan(.02f),c.style+c.mode);
            }
            File.WriteAllText(Path.Combine(output,"FootContactComparison.json"),JsonUtility.ToJson(new Report {status="USER_STYLE_DECISION_REQUIRED",outputDirectory=output,appliedToDefault=false,cases=results.ToArray()},true));
        }
    }
}
