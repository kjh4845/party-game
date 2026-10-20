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
    public sealed partial class LimpLiftCandidateTests
    {
        private const float Dt=1f/60;
        private Scene _scene;
        private ActiveGrabPairReview _pair;
        private string Output=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../.codex_tmp/limp-lift-20260917"));
        private string Candidate=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../.codex_tmp/shoulder-reach-20260916/candidate-r02/ShoulderCharacter.bytes"));
        private void Create(LimpGrabTargetReview.Orientation orientation=LimpGrabTargetReview.Orientation.FaceUp,bool low=true)
        {
            Directory.CreateDirectory(Output);
            _scene=SceneManager.CreateScene("Limp lift candidate",new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            _pair=ActiveGrabPairReview.Create(_scene,.5f,ActiveGrabPairReview.GrabMotion.AdaptiveReach,Candidate);
            _pair.ManualStep=true;_pair.ReadKeyboard=false;_pair.View.enabled=false;
            for(var i=0;i<60;i++)Tick();
            if(low){_pair.Players[1].Rig.gameObject.SetActive(false);_pair.Players[0].Rig.MatchHandSkinEnvelope();_pair.ExternalTarget=LimpGrabTargetReview.Create(_scene,Candidate,orientation);}
            for(var i=0;i<150;i++)Tick();_pair.EnableLiftThrow=true;
        }
        private void Tick(){_pair.Step(Dt);_scene.GetPhysicsScene().Simulate(Dt);_pair.CapturePhysics();}
        private void Buttons(int mask)=>_pair.Buttons(0,(mask&1)!=0,(mask&2)!=0);
        private void Approach(int desiredGrips=2)=>_pair.Players[0].Move=_pair.LogicalGrips<desiredGrips?Vector2.up*.25f:Vector2.zero;
        [UnityTearDown] public IEnumerator Cleanup(){if(_scene.IsValid()&&_scene.isLoaded){var op=SceneManager.UnloadSceneAsync(_scene);while(!op.isDone)yield return null;}}
        private void AssertLimp()
        {
            var target=_pair.ExternalTarget;Assert.That(target.Bodies.Sum(b=>b.mass),Is.EqualTo(20).Within(.0001f));
            foreach(var b in target.Bodies)
            {
                Assert.That(b.useGravity,Is.True);Assert.That(b.isKinematic,Is.False);
                foreach(var j in b.GetComponents<ConfigurableJoint>())
                {
                    Assert.That(j.rotationDriveMode,Is.EqualTo(RotationDriveMode.XYAndZ));
                    Assert.That(j.angularXDrive.positionSpring+j.angularXDrive.positionDamper+j.angularXDrive.maximumForce+j.angularYZDrive.positionSpring+j.angularYZDrive.positionDamper+j.angularYZDrive.maximumForce,Is.Zero);
                }
            }
        }
        [UnityTest] public IEnumerator LiftFourDirectionsAndRelease()
        {
            var rows=new List<string>();var failures=new List<string>();
            foreach(LimpGrabTargetReview.Orientation orientation in Enum.GetValues(typeof(LimpGrabTargetReview.Orientation)))
            {
                Create(orientation);var start=_pair.ExternalTarget.Center.y;Buttons(3);var maxRise=0f;var maxGrips=0;var maxSupport=0f;
                for(var tick=0;tick<360;tick++)
                {
                    if(tick<150)Approach();else _pair.Players[0].Move=Vector2.zero;
                    if(tick==270)Buttons(0);Tick();AssertLimp();
                    if(tick<270)maxRise=Mathf.Max(maxRise,_pair.ExternalTarget.Center.y-start);
                    maxGrips=Math.Max(maxGrips,_pair.LogicalGrips);maxSupport=Mathf.Max(maxSupport,_pair.LiftSupportForce);
                    if(tick==220){_pair.Present(1);yield return null;var rt=new RenderTexture(960,640,24);rt.Create();_pair.View.targetTexture=rt;_pair.View.aspect=1.5f;_pair.View.orthographicSize=1.1f;_pair.View.Render();var old=RenderTexture.active;RenderTexture.active=rt;var image=new Texture2D(960,640,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,960,640),0,0);image.Apply();RenderTexture.active=old;File.WriteAllBytes(Path.Combine(Output,"Probe_"+orientation+".png"),image.EncodeToPNG());_pair.View.targetTexture=null;rt.Release();UnityEngine.Object.Destroy(rt);UnityEngine.Object.Destroy(image);}
                    if(tick%20==0)rows.Add($"{orientation} t={tick} grips={_pair.LogicalGrips} com={_pair.ExternalTarget.Center:R} root={_pair.Players[0].Rig.Actor.Motor.Body.position:R} support={_pair.LiftSupportForce:R} hand={_pair.Players[0].Rig.HandPosition(0):R} upperError={_pair.Players[0].Rig.TrackingError("UpperArm_L"):R} foreError={_pair.Players[0].Rig.TrackingError("Forearm_L"):R} goal={_pair.Players[0].Hands[0].MotionTargetLocal:R} gap={_pair.MaximumGripGap:R} release={_pair.Players[0].LastRelease}");
                }
                rows.Add($"SUMMARY {orientation} rise={maxRise:R} grips={maxGrips} support={maxSupport:R} throws={_pair.Throws}");File.WriteAllLines(Path.Combine(Output,"LiftDirections.txt"),rows);
                if(maxRise<.15f)failures.Add(orientation+" insufficient lift "+maxRise);
                if(maxGrips<1||_pair.Throws!=1)failures.Add(orientation+" grip/release failure");
                Assert.That(_pair.LiftSupportForce,Is.Zero);Assert.That(_pair.LogicalGrips+_pair.PhysicalConnections,Is.Zero);
                var op=SceneManager.UnloadSceneAsync(_scene);while(!op.isDone)yield return null;
            }
            Assert.That(failures,Is.Empty,string.Join("\n",failures));
        }
    }
}
