using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
namespace ProjectHotfix.Physics.Tests
{
    public sealed partial class PhysicalPunchCandidateTests
    {
        [UnityTest] public IEnumerator ProbeJointVelocityConvention()
        {
            _scene=SceneManager.CreateScene("Velocity convention",new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            var rows=new System.Collections.Generic.List<string>();
            foreach(var mode in new[]{"Rotation","Velocity"})
            {
                var go=new GameObject(mode);SceneManager.MoveGameObjectToScene(go,_scene);var body=go.AddComponent<Rigidbody>();body.useGravity=false;body.maxAngularVelocity=20;
                var joint=go.AddComponent<ConfigurableJoint>();joint.rotationDriveMode=RotationDriveMode.Slerp;
                joint.slerpDrive=new JointDrive{positionSpring=mode=="Rotation"?100:0,positionDamper=10,maximumForce=100};
                joint.targetRotation=mode=="Rotation"?Quaternion.Euler(30,0,0):Quaternion.identity;
                joint.targetAngularVelocity=mode=="Velocity"?Vector3.right:Vector3.zero;
                for(var i=0;i<30;i++)_scene.GetPhysicsScene().Simulate(1f/60);
                Assert.That(Mathf.DeltaAngle(0,body.rotation.eulerAngles.x),Is.LessThan(-10),"Positive joint targets must rotate the body in the inverse direction");
                rows.Add(mode+" angle="+Mathf.DeltaAngle(0,body.rotation.eulerAngles.x)+" angularVelocity="+body.angularVelocity.x);
            }
            Directory.CreateDirectory(Output);File.WriteAllLines(Path.Combine(Output,"VelocityConvention.txt"),rows);yield return null;
        }
        [UnityTest] public IEnumerator PunchVelocityIsBoundedAndClearsOnCancelAndReset()
        {
            foreach(var reset in new[]{false,true})
            {
                Create(.7f);Tap(0,0);var peak=0f;
                for(var i=0;i<8;i++)
                {
                    Tick();foreach(var name in new[]{"UpperArm_L","Forearm_L"})
                    {
                        var speed=_pair.Players[0].Rig.Find(name).Joint.targetAngularVelocity.magnitude;
                        peak=Mathf.Max(peak,speed);Assert.That(speed,Is.LessThanOrEqualTo(50.001f));
                    }
                }
                Assert.That(peak,Is.GreaterThan(1));
                if(reset)_pair.ResetReview();else _pair.SetSuppressed(true);
                foreach(var player in _pair.Players)foreach(var link in player.Rig.Links)
                    Assert.That(link.Joint.targetAngularVelocity.sqrMagnitude,Is.LessThan(1e-8f));
                var op=SceneManager.UnloadSceneAsync(_scene);while(!op.isDone)yield return null;
            }
        }
        private IEnumerator SidePhoto(string file)
        {
            yield return null;_pair.Present(1);
            var center=(_pair.Players[0].Rig.Actor.Motor.Body.position+_pair.Players[1].Rig.Actor.Motor.Body.position)*.5f+Vector3.up*.46f;
            _pair.View.transform.SetPositionAndRotation(center+new Vector3(-2.2f,.30f,0),Quaternion.LookRotation(new Vector3(2.2f,-.30f,0)));
            var rt=new RenderTexture(960,640,24);rt.Create();var image=new Texture2D(960,640,TextureFormat.RGB24,false);
            _pair.View.targetTexture=rt;_pair.View.aspect=1.5f;_pair.View.orthographicSize=.62f;_pair.View.Render();
            var previous=RenderTexture.active;RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,960,640),0,0);image.Apply();RenderTexture.active=previous;
            File.WriteAllBytes(Path.Combine(Output,file+".png"),image.EncodeToPNG());_pair.View.targetTexture=null;rt.Release();UnityEngine.Object.Destroy(rt);UnityEngine.Object.Destroy(image);
        }
        [UnityTest,Timeout(300000)] public IEnumerator CapturePolishMotionAndSurface()
        {
            foreach(var mode in new[]{"AirMotion","ContactMotion"})
            {
                Create(mode=="AirMotion"?.7f:.38f);
                _pair.View.transform.position=new Vector3(-2.2f,.95f,.5f);
                _pair.View.transform.LookAt(new Vector3(0,.45f,0));
                var skin=System.Linq.Enumerable.Single(_pair.Players[0].Rig.Actor.Pose.GetComponentsInChildren<SkinnedMeshRenderer>(),x=>x.name.EndsWith("SkinnedBody"));
                var mesh=new Mesh();var triangles=skin.sharedMesh.triangles;var pairs=CameraGaitReviewTests.Pairs(triangles);var surface=0f;
                Directory.CreateDirectory(Path.Combine(Output,mode));
                for(var tick=0;tick<90;tick++)
                {
                    if(tick==10)Buttons(0,1);if(tick==12)Buttons(0,0);
                    if(tick==50)Buttons(0,2);if(tick==52)Buttons(0,0);
                    Tick();skin.BakeMesh(mesh);surface=Mathf.Max(surface,CameraGaitReviewTests.Surface(mesh.vertices,triangles,pairs,out _));
                    yield return SidePhoto(mode+"/Frame_"+tick.ToString("D3"));
                }
                File.WriteAllText(Path.Combine(Output,mode+"_Surface.txt"),"maximumAdjacentFaceAngle="+surface);
                Assert.That(surface,Is.LessThan(60),"No sharp folded skin during either physical punch");
                UnityEngine.Object.Destroy(mesh);
                var op=SceneManager.UnloadSceneAsync(_scene);while(!op.isDone)yield return null;
            }
        }
        [UnityTest] public IEnumerator CapturePolishComparison()
        {
            foreach(var mode in new[]{"Air","Contact","Right","Wall","NonTip"})
            {
                var side=mode=="Right"?1:0;Create(mode=="Air"?.70f:.38f,mode=="NonTip"?.25f:0,mode=="Wall");
                _pair.View.transform.position=new Vector3(-1.7f,.9f,1.6f);
                _pair.View.transform.LookAt(new Vector3(0,.47f,0));
                yield return Photo(mode+"_00");
                Tap(0,side);
                for(var tick=0;tick<40;tick++)
                {
                    Tick();
                    if(tick==3||tick==6||tick==8||tick==11||tick==15||tick==23||tick==35)
                        yield return Photo(mode+"_"+(tick+1).ToString("D2"));
                }
                File.WriteAllText(Path.Combine(Output,mode+"_Hits.json"),"["+string.Join(",",System.Linq.Enumerable.Select(_pair.PhysicalPunches.Hits,h=>JsonUtility.ToJson(h)))+"]");
                Assert.That(_pair.PhysicalPunches.State(0).PunchCount,Is.EqualTo(1));
                var op=SceneManager.UnloadSceneAsync(_scene);while(!op.isDone)yield return null;
            }
        }
    }
}
