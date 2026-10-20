using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using ProjectHotfix.Contracts;
using ProjectHotfix.PhysicsJump;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace ProjectHotfix.Physics.Tests
{
    public sealed class SkinningCaptureTests
    {
        private Scene _scene;
        [Serializable] private sealed class Report
        {
            public string status;
            public int firstFrame, sameFrame, refreshedFrame;
            public float sameFrameMotion, expectedPoseMotion, sameFrameMismatch, refreshedMismatch;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (!_scene.IsValid() || !_scene.isLoaded) yield break;
            var unload = SceneManager.UnloadSceneAsync(_scene);
            while (unload != null && !unload.isDone) yield return null;
        }

        [UnityTest]
        public IEnumerator RenderedSkin_AfterPlayerLoop_MatchesCurrentBakedPose()
        {
            if (!File.Exists(PhysicsJumpReview.TransportPath)) Assert.Ignore("Local review transport missing.");
            var output = Path.GetFullPath(Path.Combine(Application.dataPath, "../../.codex_tmp/render-fix-20260906/Parity"));
            Directory.CreateDirectory(output);
            _scene = SceneManager.CreateScene("SkinningCaptureParity", new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            var actor = PhysicsJumpReview.Create(Vector3.zero); SceneManager.MoveGameObjectToScene(actor.gameObject, _scene);
            actor.ReadKeyboard = false; actor.Motor.ManualStep = true; actor.Motor.Body.isKinematic = true;
            actor.Pose.SpeedMatchedGait = true; actor.Pose.WalkReferenceSpeed = .4f; actor.Pose.WalkPelvisDrop = .02f;
            var material = new Material(Shader.Find("Universal Render Pipeline/Unlit")); material.SetColor("_BaseColor", Color.white);
            var skins = actor.GetComponentsInChildren<SkinnedMeshRenderer>();
            foreach (var skin in skins) { skin.gameObject.layer = 31; skin.sharedMaterial = material; }
            var cameraObject = new GameObject("ParityCamera"); SceneManager.MoveGameObjectToScene(cameraObject, _scene);
            var camera = cameraObject.AddComponent<Camera>(); camera.enabled = false; camera.orthographic = true;
            camera.orthographicSize = .65f; camera.cullingMask = 1 << 31; camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black; camera.transform.position = new Vector3(2f,.65f,3f); camera.transform.LookAt(new Vector3(0,.5f,0));
            var render = new RenderTexture(512,512,24); render.Create(); camera.targetTexture = render;
            var texture = new Texture2D(512,512,TextureFormat.RGB24,false);
            var state = new JumpPresentationState(0f,true,100f,0f,0f,.4f,false,0f);
            for (var step=0;step<180 && actor.Pose.GaitPhase<.25f;step++)
            {
                actor.Pose.Apply(state,1f/60f);actor.Arms.Apply(actor.Pose,1f/60f,0);
            }
            yield return null;
            var report = new Report { status = "OBSERVED", firstFrame = Time.frameCount };
            var first = Capture(camera,render,texture,Path.Combine(output,"A_live.png"));
            for (var step=0;step<180 && actor.Pose.GaitPhase<.5f;step++)
            {
                actor.Pose.Apply(state,1f/60f);actor.Arms.Apply(actor.Pose,1f/60f,0);
            }
            report.sameFrame = Time.frameCount;
            var same = Capture(camera,render,texture,Path.Combine(output,"B_same_frame.png"));
            // Refresh before any BakeMesh or renderer visibility changes, so the
            // frame boundary is the only intervention being tested.
            yield return null;
            report.refreshedFrame=Time.frameCount;
            var refreshed=Capture(camera,render,texture,Path.Combine(output,"B_after_frame.png"));
            var references = new List<GameObject>(); var meshes = new List<Mesh>();
            foreach (var skin in skins)
            {
                var mesh = new Mesh();skin.BakeMesh(mesh);meshes.Add(mesh);
                var reference = new GameObject("BakedReference");reference.transform.SetParent(skin.transform.parent,false);
                reference.transform.localPosition=skin.transform.localPosition;reference.transform.localRotation=skin.transform.localRotation;
                reference.transform.localScale=skin.transform.localScale;reference.layer=31;
                reference.AddComponent<MeshFilter>().sharedMesh=mesh;reference.AddComponent<MeshRenderer>().sharedMaterial=material;
                references.Add(reference);skin.enabled=false;
            }
            var expected = Capture(camera,render,texture,Path.Combine(output,"B_cpu_reference.png"));
            foreach(var reference in references) reference.SetActive(false);
            foreach(var skin in skins) skin.enabled=true;
            report.sameFrameMotion=Mismatch(first,same);report.expectedPoseMotion=Mismatch(first,expected);
            report.sameFrameMismatch=Mismatch(same,expected);report.refreshedMismatch=Mismatch(refreshed,expected);
            File.WriteAllText(Path.Combine(output,"SkinningCaptureParity.json"),JsonUtility.ToJson(report,true));
            Debug.Log($"SKINNING_CAPTURE sameMotion={report.sameFrameMotion:F5} expectedMotion={report.expectedPoseMotion:F5} stale={report.sameFrameMismatch:F5} refreshed={report.refreshedMismatch:F5}");
            Assert.That(report.firstFrame,Is.EqualTo(report.sameFrame));
            Assert.That(report.refreshedFrame,Is.GreaterThan(report.sameFrame));
            Assert.That(report.expectedPoseMotion,Is.GreaterThan(.01f),"Selected poses must visibly differ");
            Assert.That(report.refreshedMismatch,Is.LessThan(.005f),"Rendered skin does not match its current bones");
            report.status="PASS";File.WriteAllText(Path.Combine(output,"SkinningCaptureParity.json"),JsonUtility.ToJson(report,true));
            camera.targetTexture=null;render.Release();UnityEngine.Object.Destroy(render);UnityEngine.Object.Destroy(texture);
            foreach(var mesh in meshes)UnityEngine.Object.Destroy(mesh);UnityEngine.Object.Destroy(material);
        }

        private static Color32[] Capture(Camera camera, RenderTexture render, Texture2D texture, string path)
        {
            camera.Render();var old=RenderTexture.active;RenderTexture.active=render;
            texture.ReadPixels(new Rect(0,0,512,512),0,0);texture.Apply();RenderTexture.active=old;
            File.WriteAllBytes(path,texture.EncodeToPNG());return texture.GetPixels32();
        }

        private static float Mismatch(Color32[] a, Color32[] b)
        {
            var union=0;var difference=0;
            for(var i=0;i<a.Length;i++)
            {
                var x=a[i].r>120;var y=b[i].r>120;
                if(x||y)union++;if(x!=y)difference++;
            }
            return (float)difference/Mathf.Max(1,union);
        }
    }
}
