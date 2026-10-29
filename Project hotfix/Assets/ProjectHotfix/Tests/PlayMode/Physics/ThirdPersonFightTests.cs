using System;
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using ProjectHotfix.PhysicsJump;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace ProjectHotfix.Physics.Tests
{
    // Third-person fight scenes (2026-10-28): the body follows the mouse view, WASD moves in the view's frame, the camera
    // never loses the character behind a wall, and a click punches at once wherever the feet are.
    public sealed class ThirdPersonFightTests
    {
        private const float Dt=1f/60;
        private Scene _scene;private ActiveGrabPairReview _pair;
        private static string Model=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../BlenderSource/Characters/C1B-RW-046-slim-body/R43Character.bytes"));
        // PARTY_TPS_OUTPUT: where the camera views are saved for review (none when unset).
        private static string Output=>Environment.GetEnvironmentVariable("PARTY_TPS_OUTPUT");
        private void Create(float separation=1.5f,bool punchOnPress=true)
        {
            _scene=SceneManager.CreateScene("Third person",new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            _pair=ActiveGrabPairReview.Create(_scene,separation,ActiveGrabPairReview.GrabMotion.AdaptiveReach,Model,matchHandEnvelope:true);
            _pair.EnableStandingSkinContacts=_pair.EnableLiftThrow=_pair.EnablePhysicalPunch=true;
            _pair.UseStrikeStyle(ActiveUpperBodyReview.ReviewStrikeStyle.BodyStrike);_pair.UseFightThrows();
            _pair.ThirdPerson=true;_pair.PunchOnPress=punchOnPress;_pair.ManualStep=true;_pair.ReadKeyboard=false;
            UnityEngine.Physics.SyncTransforms();for(var i=0;i<60;i++)Tick();
        }
        private void Tick(Vector2 mouse=default){_pair.Look(mouse,Dt);_pair.Step(Dt);_scene.GetPhysicsScene().Simulate(Dt);_pair.CapturePhysics();}
        private Rigidbody Root=>_pair.Players[0].Rig.Actor.Motor.Body;
        private float BodyYaw{get{var f=Vector3.ProjectOnPlane(Root.rotation*Vector3.forward,Vector3.up);return Mathf.Atan2(f.x,f.z)*Mathf.Rad2Deg;}}
        private float YawError=>Mathf.Abs(Mathf.DeltaAngle(BodyYaw,_pair.LookYaw));
        private GameObject Block(string name,Vector3 p,Vector3 scale)
        {var go=GameObject.CreatePrimitive(PrimitiveType.Cube);SceneManager.MoveGameObjectToScene(go,_scene);go.name=name;go.transform.position=p;go.transform.localScale=scale;return go;}
        private void Photo(string file)
        {
            if(string.IsNullOrEmpty(Output))return;Directory.CreateDirectory(Output);
            var rt=new RenderTexture(960,540,24);var image=new Texture2D(960,540,TextureFormat.RGB24,false);_pair.View.targetTexture=rt;_pair.View.aspect=16f/9;_pair.View.Render();
            var old=RenderTexture.active;RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,960,540),0,0);image.Apply();RenderTexture.active=old;
            File.WriteAllBytes(Path.Combine(Output,file+".png"),image.EncodeToPNG());_pair.View.targetTexture=null;UnityEngine.Object.Destroy(rt);UnityEngine.Object.Destroy(image);
        }
        [UnityTearDown] public IEnumerator Cleanup(){if(_scene.IsValid()&&_scene.isLoaded){var op=SceneManager.UnloadSceneAsync(_scene);while(!op.isDone)yield return null;}}

        [UnityTest] public IEnumerator BodyFollowsTheMouseView()
        {
            Create();
            var counts=1/_pair.LookSensitivity;
            // A quick 90 degree turn of the mouse over 4 ticks.
            for(var i=0;i<4;i++)Tick(new Vector2(22.5f*counts,0));
            var settle=-1;var peak=0f;
            for(var i=1;i<=60;i++){Tick();if(settle<0&&YawError<3)settle=i;if(settle>=0)peak=Mathf.Max(peak,YawError);}
            // A steady sweep at 360 deg/s for 1.5 s, then at 90 deg/s.
            float sweepMean=0,sweepMax=0;var n=0;
            for(var i=0;i<90;i++){Tick(new Vector2(6*counts,0));if(i>=15){sweepMean+=YawError;sweepMax=Mathf.Max(sweepMax,YawError);n++;}}
            sweepMean/=n;
            float slowMax=0;for(var i=0;i<90;i++){Tick(new Vector2(1.5f*counts,0));if(i>=30)slowMax=Mathf.Max(slowMax,YawError);}
            var tilt=Vector3.Angle(Root.rotation*Vector3.up,Vector3.up);
            var fastest=_pair.Players[0].Rig.Links.Max(l=>l.Body.linearVelocity.magnitude);
            Debug.Log($"TPS turn: 90 deg turn settles <3 deg {settle} ticks after the mouse stops, after that max {peak:F2} deg; 360/s sweep error mean {sweepMean:F2} max {sweepMax:F2} deg; 90/s max {slowMax:F2} deg; tilt {tilt:F1} deg; fastest link {fastest:F2} m/s");
            Assert.That(settle,Is.InRange(1,20),"a quick 90 degree turn settles within a third of a second");
            Assert.That(peak,Is.LessThan(4f),"no swing after settling");
            Assert.That(sweepMax,Is.LessThan(8f),"a 360 deg/s sweep is tracked");
            Assert.That(slowMax,Is.LessThan(4f),"a 90 deg/s turn is tracked closely");
            Assert.That(_pair.Players[0].Rig.RagdollActive,Is.False);Assert.That(tilt,Is.LessThan(10f));
            yield return null;
        }

        [UnityTest] public IEnumerator WasdMovesInTheViewFrame()
        {
            Create(4);
            var counts=1/_pair.LookSensitivity;Tick(new Vector2(90*counts,0));for(var i=0;i<30;i++)Tick();
            foreach(var (input,expected) in new[]{(new Vector2(0,1),Vector3.right),(new Vector2(1,0),Vector3.back),(new Vector2(1,1).normalized,(Vector3.right+Vector3.back).normalized)})
            {
                var start=Root.position;_pair.Players[0].Move=_pair.ViewMove(input);
                for(var i=0;i<45;i++)Tick();
                _pair.Players[0].Move=Vector2.zero;for(var i=0;i<30;i++)Tick();
                var travel=Vector3.ProjectOnPlane(Root.position-start,Vector3.up);var off=Vector3.Angle(travel,expected);
                Debug.Log($"TPS move {input}: travel {travel.magnitude:F2} m, {off:F1} deg off the view direction; facing error {YawError:F1} deg");
                Assert.That(travel.magnitude,Is.GreaterThan(.5f));Assert.That(off,Is.LessThan(8f));Assert.That(YawError,Is.LessThan(5f),"moving sideways keeps facing the view");
            }
            yield return null;
        }

        [UnityTest] public IEnumerator CameraKeepsTheCharacterInView()
        {
            Create(4);
            var own=_pair.Players[0].Rig.Links.Select(l=>l.Body).Append(Root).ToHashSet();
            bool Clear(out string blocker)
            {
                blocker=null;var eye=_pair.View.transform.position;
                foreach(var name in new[]{"Head","Chest","Spine"})
                {
                    var point=_pair.Players[0].Rig.Find(name).Body.worldCenterOfMass;
                    var hits=new RaycastHit[32];var count=_scene.GetPhysicsScene().Raycast(eye,(point-eye).normalized,hits,Vector3.Distance(eye,point));
                    for(var i=0;i<count;i++)if(hits[i].rigidbody==null||!own.Contains(hits[i].rigidbody)){blocker=hits[i].collider.name;return false;}
                }
                return true;
            }
            _pair.Present(1);Photo("tps_open");
            var open=Vector3.Distance(_pair.View.transform.position,Root.position);
            Assert.That(Clear(out var b0),Is.True,b0);
            // A wall right behind the character, between it and the camera.
            Block("Wall behind",Root.position+Vector3.back*.6f+Vector3.up*1f,new Vector3(3,2,.2f));UnityEngine.Physics.SyncTransforms();
            _pair.Present(1);Photo("tps_wall_behind");var walled=Vector3.Distance(_pair.View.transform.position,Root.position);
            Assert.That(Clear(out var b1),Is.True,b1);
            // Looking up from beside the wall: the floor is behind the camera's path.
            var counts=1/_pair.LookSensitivity;Tick(new Vector2(0,100*counts));_pair.Present(1);Photo("tps_look_up");
            Assert.That(Clear(out var b2),Is.True,b2);Assert.That(_pair.View.transform.position.y,Is.GreaterThan(0f),"never under the floor");
            // The other player standing between camera and character.
            Tick(new Vector2(0,-100*counts));
            Debug.Log($"TPS camera: open {open:F2} m, wall behind {walled:F2} m, looking up y {_pair.View.transform.position.y:F2}");
            Assert.That(walled,Is.LessThan(open-.5f),"pulled in front of the wall");
            yield return null;
        }

        // Pressed together (spawned .24 m apart, the skins hold the roots at .27) the straight stroke ran past the body and
        // every punch missed; the hook lands with either hand at .27 and .30 m.
        [UnityTest] public IEnumerator CloseHookLandsWhenPressedTogether()
        {
            foreach(var gap in new[]{.24f,.30f})for(var side=0;side<2;side++)
            {
                Create(gap);
                _pair.SubmitPhysicalHandEdge(0,side,true);Tick();_pair.SubmitPhysicalHandEdge(0,side,false,Dt);
                var hook=0f;for(var i=0;i<40;i++){Tick();hook=Mathf.Max(hook,_pair.Players[0].Rig.PunchHook(side));}
                var hit=_pair.PhysicalPunches.Hits.FirstOrDefault();
                Debug.Log($"TPS close hook {gap:F2} {(side==0?"L":"R")}: hook {hook:F2}, "+(hit==null?"miss":$"{hit.region}/{hit.part} closing {hit.closingSpeed:F1}"));
                Assert.That(hook,Is.EqualTo(1).Within(.01f),"pressed close the punch is a hook");
                Assert.That(hit,Is.Not.Null,"the hook lands");
                Assert.That(hit.closingSpeed,Is.GreaterThan(1f));
                yield return Cleanup();
            }
        }

        // Alternating hands every .2 s while walking in on a player who only stands there: every third hit in a row pushes
        // it clear, so it is never hit more than three times running (without the break: 6 in a row). Standing still it can
        // still be knocked out; hits on it lying there count for nothing and are not part of a run.
        [UnityTest] public IEnumerator ThirdHitInARowPushesAOneSidedTargetClear()
        {
            Create(.24f);_pair.LightPunchLegs=true;var target=_pair.Players[1].Rig;
            int run=0,longest=0,seen=0;var last=-9f;var gapAfterBreak=-1f;var breakTick=-1;
            for(var i=0;i<300;i++)
            {
                var d=Vector3.ProjectOnPlane(target.Actor.Motor.Body.position-Root.position,Vector3.up);
                _pair.Players[0].Move=d.magnitude>.30f?new Vector2(d.x,d.z).normalized:Vector2.zero;
                var side=(i/12)%2;if(i%12==0)_pair.SubmitPhysicalHandEdge(0,side,true);if(i%12==1)_pair.SubmitPhysicalHandEdge(0,side,false,Dt);
                Tick(new Vector2(Mathf.DeltaAngle(_pair.LookYaw,Mathf.Atan2(d.x,d.z)*Mathf.Rad2Deg)/_pair.LookSensitivity*.5f,0));
                if(breakTick>=0&&i-breakTick==36&&gapAfterBreak<0)gapAfterBreak=Vector3.ProjectOnPlane(target.Actor.Motor.Body.position-Root.position,Vector3.up).magnitude;
                var hits=_pair.PhysicalPunches.Hits;
                for(;seen<hits.Count;seen++)
                {
                    if(!hits[seen].counted)continue;
                    var t=i*Dt;run=t-last<=.8f?run+1:1;last=t;longest=Mathf.Max(longest,run);
                    if(hits[seen].chainBreak){run=0;last=-9;if(breakTick<0)breakTick=i;}
                }
            }
            Debug.Log($"TPS chain break: hits {_pair.PhysicalPunches.Hits.Count}, breaks {_pair.PhysicalPunches.ChainBreaks}, longest run {longest}, apart .6 s after the first break {gapAfterBreak:F2} m, knockdowns {target.Knockdowns}");
            Assert.That(_pair.PhysicalPunches.ChainBreaks,Is.GreaterThan(0));
            Assert.That(longest,Is.LessThanOrEqualTo(3));
            Assert.That(gapAfterBreak,Is.GreaterThan(.9f),"pushed clear of reach");
            yield return null;
        }

        [UnityTest] public IEnumerator LightPunchLegsKeepTheHipsStillAndTheReach()
        {
            foreach(var gap in new[]{.32f,.55f})for(var side=0;side<2;side++)
            {
                Create(gap);_pair.LightPunchLegs=true;var pose=_pair.Players[0].Rig.Actor.Pose;float yaw=0,drop=0;
                _pair.SubmitPhysicalHandEdge(0,side,true);Tick();_pair.SubmitPhysicalHandEdge(0,side,false,Dt);
                for(var i=0;i<40;i++){Tick();yaw=Mathf.Max(yaw,Mathf.Abs(pose.AdditionalPelvisYaw));drop=Mathf.Max(drop,pose.AdditionalPelvisDrop);}
                Debug.Log($"TPS light legs {gap:F2} {(side==0?"L":"R")}: hits {_pair.PhysicalPunches.Hits.Count}, pelvis yaw max {yaw:F1} deg, drop {drop*100:F1} cm");
                Assert.That(_pair.PhysicalPunches.Hits.Count,Is.EqualTo(1),"the reach is kept");
                Assert.That(yaw,Is.LessThan(3f));Assert.That(drop,Is.LessThan(.001f));
                yield return Cleanup();
            }
        }

        [UnityTest] public IEnumerator JumpKickSwingsStraightLegsAndLandsUpright()
        {
            // Either button kicks the same way. The legs swing out and back straight (the straight-line blend folded the knees
            // ~100 deg on the way down) and the trunk is upright again by touchdown (it came down ~30 deg back, 2026-10-30).
            for(var side=0;side<2;side++)
            {
                Create(.5f);_pair.LightPunchLegs=true;var rig=_pair.Players[0].Rig;var pose=rig.Actor.Pose.transform;
                Transform Bone(string name)=>pose.GetComponentsInChildren<Transform>().First(t=>t.name==name);
                var thigh=Bone("Thigh_R");var calf=Bone("Calf_R");var foot=Bone("Foot_R");
                float knee=0,touchdownLean=-1;var airborne=false;var chest=rig.Find("Chest").Body;var standing=chest.rotation;
                for(var i=0;i<70;i++)
                {
                    _pair.Players[0].Jump=i==0;
                    if(i==8)_pair.SubmitPhysicalHandEdge(0,side,true);if(i==9)_pair.SubmitPhysicalHandEdge(0,side,false,Dt);
                    Tick();_pair.Present(1);
                    if(_pair.PhysicalPunches.Kicking(0))knee=Mathf.Max(knee,Vector3.Angle(calf.position-thigh.position,foot.position-calf.position));
                    if(!rig.Supported)airborne=true;
                    else if(airborne&&touchdownLean<0)touchdownLean=Quaternion.Angle(standing,chest.rotation);
                }
                Debug.Log($"TPS jump kick {(side==0?"L":"R")}: kicks {_pair.PhysicalPunches.Kicks}, hits {_pair.PhysicalPunches.KickHits}, knee max {knee:F0} deg, trunk at touchdown {touchdownLean:F0} deg");
                Assert.That(_pair.PhysicalPunches.Kicks,Is.EqualTo(1));Assert.That(_pair.PhysicalPunches.KickHits,Is.EqualTo(1));
                Assert.That(knee,Is.LessThan(60f),"legs stay straight through the kick");
                Assert.That(touchdownLean,Is.InRange(0f,15f),"upright at touchdown");
                yield return Cleanup();
            }
        }

        [UnityTest] public IEnumerator RunningJumpPressFliesTheDropkickToTheClickedSide()
        {
            // A jump out of a run is still the run: either button flies the dropkick (not the standing jump kick), turned to the
            // side of the clicked hand (2026-10-30).
            for(var side=0;side<2;side++)
            {
                Create(3.2f);var rig=_pair.Players[0].Rig;var p=_pair.Players[0];
                var forward=Vector3.ProjectOnPlane(_pair.Players[1].Rig.Actor.Motor.Body.position-Root.position,Vector3.up).normalized;
                p.Sprint=true;p.Move=new Vector2(forward.x,forward.z);
                for(var i=0;i<40;i++)Tick();
                p.Jump=true;Tick();p.Jump=false;for(var i=0;i<5;i++)Tick();
                Assert.That(rig.Supported,Is.False,"in the air");
                _pair.SubmitPhysicalHandEdge(0,side,true);Tick();
                p.Sprint=false;p.Move=Vector2.zero;
                for(var i=0;i<12;i++)Tick();
                var turn=Vector3.SignedAngle(forward,Vector3.ProjectOnPlane(Root.rotation*Vector3.forward,Vector3.up),Vector3.up);
                _pair.SubmitPhysicalHandEdge(0,side,false,13*Dt);
                Debug.Log($"TPS running jump {(side==0?"L":"R")}: dropkicks {rig.Dropkicks}, jump kicks {_pair.PhysicalPunches.Kicks}, punches {_pair.PhysicalPunches.State(0).PunchCount}, turned {turn:F0} deg");
                Assert.That(rig.Dropkicks,Is.EqualTo(1));Assert.That(_pair.PhysicalPunches.Kicks,Is.Zero);Assert.That(_pair.PhysicalPunches.State(0).PunchCount,Is.Zero);
                Assert.That(side==0?turn<-45:turn>45,Is.True,"turned to the clicked side");
                yield return Cleanup();
            }
        }

        [UnityTest] public IEnumerator RunningOnTheGroundAPressPunches()
        {
            // On the ground a run's press is the plain punch (2026-10-30: chasing with Shift and clicking flew a dropkick),
            // and a bump that lifts the feet without a jump is no dropkick either.
            for(var side=0;side<2;side++)
            {
                Create(3.2f);var rig=_pair.Players[0].Rig;var p=_pair.Players[0];
                var forward=Vector3.ProjectOnPlane(_pair.Players[1].Rig.Actor.Motor.Body.position-Root.position,Vector3.up).normalized;
                p.Sprint=true;p.Move=new Vector2(forward.x,forward.z);
                var press=-1;
                for(var i=0;i<140;i++)
                {
                    var gap=Vector3.ProjectOnPlane(_pair.Players[1].Rig.Actor.Motor.Body.position-Root.position,Vector3.up).magnitude;
                    if(press<0&&gap<.8f){Assert.That(_pair.Running(0),Is.True,"running at the press");press=i;_pair.SubmitPhysicalHandEdge(0,side,true);}
                    if(press>=0&&i==press+2)_pair.SubmitPhysicalHandEdge(0,side,false,2*Dt);
                    if(press>=0&&i==press+18){p.Sprint=false;p.Move=Vector2.zero;}
                    Tick();
                }
                Debug.Log($"TPS running press {(side==0?"L":"R")}: punches {_pair.PhysicalPunches.State(0).PunchCount}, hits {_pair.PhysicalPunches.Hits.Count(h=>h.attacker==0)}, dropkicks {rig.Dropkicks}");
                Assert.That(_pair.PhysicalPunches.State(0).PunchCount,Is.EqualTo(1));Assert.That(rig.Dropkicks,Is.Zero);
                Assert.That(_pair.PhysicalPunches.Hits.Count(h=>h.attacker==0),Is.EqualTo(1),"the running punch lands");
                yield return Cleanup();
            }
        }

        [UnityTest] public IEnumerator ClickPunchesAtOnceEvenOnALedgeTop()
        {
            // On the floor: the punch is on in the tick the press arrives.
            Create();
            _pair.SubmitPhysicalHandEdge(0,0,true);Tick();
            Assert.That(_pair.PhysicalPunches.IsPunching(0,0),Is.True,"punching in the first tick");
            _pair.SubmitPhysicalHandEdge(0,0,false);for(var i=0;i<40;i++)Tick();
            yield return Cleanup();
            // Standing on a .55 m block: the old fixed floor height refused this press.
            foreach(var press in new[]{true,false})
            {
                Create(punchOnPress:press);
                Block("Ledge",new Vector3(0,.275f,-.75f),new Vector3(1.2f,.55f,1.2f));
                var rig=_pair.Players[0].Rig;foreach(var body in rig.Links.Select(l=>l.Body).Append(rig.Actor.Motor.Body)){var p=body.position+Vector3.up*.56f;body.transform.position=p;body.position=p;}
                UnityEngine.Physics.SyncTransforms();for(var i=0;i<60;i++)Tick();
                Assert.That(rig.Supported,Is.True,"standing on the ledge");
                var before=_pair.PhysicalPunches.State(0).PunchCount;
                _pair.SubmitPhysicalHandEdge(0,1,true);Tick();Tick();_pair.SubmitPhysicalHandEdge(0,1,false,2*Dt);for(var i=0;i<3;i++)Tick();
                var punches=_pair.PhysicalPunches.State(0).PunchCount-before;
                Debug.Log($"TPS ledge top, press-to-punch {press}: {punches} punch(es) from a tap");
                Assert.That(punches,Is.EqualTo(press?1:0));
                yield return Cleanup();
            }
        }
    }
}
