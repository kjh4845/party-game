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
    public sealed partial class PhysicalPunchCandidateTests
    {
        private const float Dt=1f/60;
        private Scene _scene;private ActiveGrabPairReview _pair;
        private string Output=>Environment.GetEnvironmentVariable("PARTY_POLISH_OUTPUT") ?? Path.GetFullPath(Path.Combine(Application.dataPath,"../../.codex_tmp/punch-drive-compact-20260917"));
        private string Model=>Environment.GetEnvironmentVariable("PARTY_POLISH_MODEL") ?? Path.GetFullPath(Path.Combine(Application.dataPath,"../../BlenderSource/Characters/C1B-RW-046-slim-body/R43Character.bytes"));
        private void Create(float distance=.5f,float targetDrop=0,bool wall=false)
        {
            Directory.CreateDirectory(Output);_scene=SceneManager.CreateScene("Physical punch",new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            _pair=ActiveGrabPairReview.Create(_scene,distance,ActiveGrabPairReview.GrabMotion.AdaptiveReach,Model,matchHandEnvelope:true);
            if(Enum.TryParse<ActiveUpperBodyReview.ReviewStrikeStyle>(Environment.GetEnvironmentVariable("PARTY_STRIKE_STYLE"),out var style))
                foreach(var player in _pair.Players)player.Rig.StrikeStyle=style;
            if(Enum.TryParse<ActiveUpperBodyReview.BodyStrikeArmPath>(Environment.GetEnvironmentVariable("PARTY_BODY_ARM_PATH"),out var armPath))
                foreach(var player in _pair.Players)player.Rig.BodyArmPath=armPath;
            if(Environment.GetEnvironmentVariable("PARTY_LOWERED_CARRIAGE") is "0" or "1")
                foreach(var player in _pair.Players)player.Rig.LoweredCarriage=Environment.GetEnvironmentVariable("PARTY_LOWERED_CARRIAGE")=="1";
            foreach(var (name,apply) in new (string,Action<ActiveUpperBodyReview,float>)[]{("PARTY_BRACE",(r,v)=>r.BraceAngle=v),("PARTY_LUNGE",(r,v)=>r.LungeScale=v)})
                if(float.TryParse(Environment.GetEnvironmentVariable(name),System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out var value))
                    foreach(var player in _pair.Players)apply(player.Rig,value);
            if(Environment.GetEnvironmentVariable("PARTY_PUNCH_DEBUG")=="1"&&_pair.PhysicalPunches!=null)_pair.PhysicalPunches.ContactLog=new System.Collections.Generic.List<string>();
            if(Environment.GetEnvironmentVariable("PARTY_BALANCED_REVIEW")=="1")
            {
                foreach(var light in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None))if(light.gameObject.scene==_scene)light.enabled=false;
                var key=new GameObject("Symmetric review light");SceneManager.MoveGameObjectToScene(key,_scene);
                var keyLight=key.AddComponent<Light>();keyLight.type=LightType.Directional;keyLight.intensity=1.15f;keyLight.color=Color.white;
                key.transform.rotation=Quaternion.Euler(25,180,0);
            }
            _pair.EnableStandingSkinContacts=_pair.EnableLiftThrow=_pair.EnablePhysicalPunch=true;_pair.UseFightThrows();_pair.ManualStep=true;_pair.ReadKeyboard=false;_pair.View.enabled=false;
            if(targetDrop>0)
            {
                _pair.transform.Find("Stage2Floor").gameObject.SetActive(false);
                Block("Upper floor",new Vector3(0,-.1f,-1),new Vector3(8,.2f,2));Block("Lower floor",new Vector3(0,-.1f-targetDrop,2),new Vector3(8,.2f,4));
                var rig=_pair.Players[1].Rig;foreach(var body in rig.Links.Select(l=>l.Body).Append(rig.Actor.Motor.Body)){var p=body.position-Vector3.up*targetDrop;body.transform.position=p;body.position=p;}
            }
            if(wall)Block("Blocking wall",new Vector3(0,.5f,0),new Vector3(1,1,.04f));
            UnityEngine.Physics.SyncTransforms();for(var i=0;i<90;i++)Tick();
        }
        private GameObject Block(string name,Vector3 p,Vector3 scale)
        {var go=GameObject.CreatePrimitive(PrimitiveType.Cube);SceneManager.MoveGameObjectToScene(go,_scene);go.name=name;go.transform.position=p;go.transform.localScale=scale;return go;}
        private void Tick(){_pair.Step(Dt);_scene.GetPhysicsScene().Simulate(Dt);_pair.CapturePhysics();}
        private void Buttons(int player,int mask)=>_pair.Buttons(player,(mask&1)!=0,(mask&2)!=0);
        [UnityTearDown] public IEnumerator Cleanup(){if(_scene.IsValid()&&_scene.isLoaded){var op=SceneManager.UnloadSceneAsync(_scene);while(!op.isDone)yield return null;}yield return UnloadSkinQueries();}
        private IEnumerator Photo(string file)
        {
            _pair.Present(1);yield return null;var rt=new RenderTexture(960,640,24);rt.Create();var image=new Texture2D(960,640,TextureFormat.RGB24,false);_pair.View.targetTexture=rt;_pair.View.aspect=1.5f;_pair.View.orthographicSize=.8f;_pair.View.Render();var old=RenderTexture.active;RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,960,640),0,0);image.Apply();RenderTexture.active=old;File.WriteAllBytes(Path.Combine(Output,file+".png"),image.EncodeToPNG());_pair.View.targetTexture=null;rt.Release();UnityEngine.Object.Destroy(rt);UnityEngine.Object.Destroy(image);
        }
        [UnityTest] public IEnumerator ProbePhysicalPunchTiming()
        {
            var rows=new List<string>();
            foreach(var distance in new[]{.35f,.38f,.40f})
            {
                Create(distance);Buttons(0,1);Tick();Tick();Buttons(0,0);var rig=_pair.Players[0].Rig;
                var skin=rig.Actor.Pose.GetComponentsInChildren<SkinnedMeshRenderer>().Single(s=>s.name.EndsWith("SkinnedBody"));var mesh=new Mesh();var tri=skin.sharedMesh.triangles;var pairs=CameraGaitReviewTests.Pairs(tri);var surface=0f;
                for(var tick=0;tick<60;tick++)
                {
                    Tick();skin.BakeMesh(mesh);surface=Mathf.Max(surface,CameraGaitReviewTests.Surface(mesh.vertices,tri,pairs,out _));
                    if(tick<35)rows.Add($"distance={distance} t={tick} phase={_pair.PhysicalPunches.State(0).Phase(0)} sourceAge={_pair.PhysicalPunches.State(0).SourcePunchAge(0):R} tip={rig.HandPosition(0):R} goal={rig.GoalHand(0):R} tracking={rig.TrackingError("UpperArm_L"):R}/{rig.TrackingError("Forearm_L"):R} hits={_pair.PhysicalPunches.Hits.Count} contact={_pair.PhysicalPunches.PhysicalContactsInWindow} radiusReject={_pair.PhysicalPunches.RejectedOutsideTip} motionReject={_pair.PhysicalPunches.RejectedMotion}");
                    if(tick==3||tick==8||tick==12)yield return Photo($"Probe_{distance}_{tick}");
                }
                rows.Add($"SUMMARY {distance} punches={_pair.PhysicalPunches.State(0).PunchCount} hits={_pair.PhysicalPunches.Hits.Count} points={_pair.PhysicalPunches.Score(1).Points} skin={surface:R}");foreach(var hit in _pair.PhysicalPunches.Hits)rows.Add(JsonUtility.ToJson(hit));File.WriteAllLines(Path.Combine(Output,"Probe.txt"),rows);
                Assert.That(_pair.PhysicalPunches.State(0).PunchCount,Is.EqualTo(1));Assert.That(_pair.LogicalGrips,Is.Zero);UnityEngine.Object.Destroy(mesh);var op=SceneManager.UnloadSceneAsync(_scene);while(!op.isDone)yield return null;
            }
        }
    }
}
