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
        [Serializable] private sealed class Frame {public int tick,punches,hits,points,grips,throws,cues,knockdowns,climbs;public bool targetDown;public float sourceAge,skin;public Vector3 tip,root,targetRoot,boneHandL,boneHandR,boneForearmL,boneForearmR;}
        [Serializable] private sealed class Clip {public string name;public float maxSkin;public int worstActor,worstTick;public Vector3 worstPoint;public Frame[] frames;public PhysicalPunchReview.Hit[] hits;}
        [Serializable] private sealed class Report {public Clip[] clips;}
        // Out of reach for the current rig: the r43 arm (shoulder -> fist tip .445 m, user decision
        // 2026-10-16 to keep the longer reach) reaches .0645 m further than the r40-era .70 m setup.
        // Extra stand-off (m) for the close-range scenes: start apart and stop the approach that much
        // further out. PARTY_SPACING, default 0 (the original touching distances).
        private static float Spacing=>float.TryParse(Environment.GetEnvironmentVariable("PARTY_SPACING"),System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out var value)?value:0;
        private int _downTick=-1,_kickTick=-1;private float _koRestHeight=-1;private float _zoom=.8f;private Vector2 _carry;
        private const int KOCycle=200;private const int GetUpCycle=420;
        private static readonly Vector3[] KOShoves={new Vector3(0,0,1.2f),new Vector3(0,0,-1.2f),new Vector3(1.2f,0,0),new Vector3(-1.2f,0,0),new Vector3(1.2f,0,1.2f),new Vector3(0,.4f,2.2f)};
        private static readonly Vector3[] KOSame=Enumerable.Repeat(new Vector3(0,0,1.2f),8).ToArray();private int _climbTick=-1,_topTick=-1;
        private const float HighWall=1.6f;
        // Root-to-root distance at which the Dropkick scene taps (PARTY_KICK_AT).
        private static float KickDistance=>float.TryParse(Environment.GetEnvironmentVariable("PARTY_KICK_AT"),System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out var value)?value:1.2f;
        // Climb test ledge (m). A pure arm-and-jump climb reaches about chest height on the .8 m body.
        private static float LedgeHeight=>float.TryParse(Environment.GetEnvironmentVariable("PARTY_LEDGE"),System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out var value)?value:.55f;
        // r43-proportion arms (thick C1B-RW-043 and slim C1B-RW-046) reach further than r40.
        private float FarSeparation=>Model.EndsWith("R43Character.bytes")?.765f:.70f;
        private void ExportSurfacePose(SkinnedMeshRenderer skin,string name)
        {
            if(Environment.GetEnvironmentVariable("PARTY_EXPORT_SURFACE")!="1")return;
            using(var writer=new BinaryWriter(File.Create(Path.Combine(Output,name+"_Matrices.bin"))))
            {
                writer.Write(skin.bones.Length);
                for(var bone=0;bone<skin.bones.Length;bone++)
                {
                    var matrix=skin.transform.worldToLocalMatrix*skin.bones[bone].localToWorldMatrix*skin.sharedMesh.bindposes[bone];
                    for(var row=0;row<4;row++)for(var col=0;col<4;col++)writer.Write(matrix[row,col]);
                }
            }
        }
        // Presented (skinned) bone position of player p, after Present: what the viewer sees, ragdoll or not.
        private Vector3 Bone(int p,string name)=>_pair.Players[p].Rig.Actor.Pose.GetComponentsInChildren<Transform>().First(t=>t.name==name).position;
        private void CaptureInput(string mode,int tick)
        {
            if(mode=="Exchange")
            {
                for(var player=0;player<2;player++)
                {
                    var due=player==0?(tick==10||tick==120):tick==65;
                    var release=player==0?(tick==12||tick==122):tick==67;
                    if(due)Buttons(player,player==0?1:2);if(release)Buttons(player,0);
                    var toward=_pair.Players[1-player].Rig.Actor.Motor.Body.position-_pair.Players[player].Rig.Actor.Motor.Body.position;toward.y=0;
                    _pair.Players[player].Move=new Vector2(toward.x,toward.z).normalized*Mathf.Clamp((toward.magnitude-.36f-Spacing)*4,0,.55f);
                }
                return;
            }
            if(mode=="Solo")
            {
                // No opponent in reach: left, right, then a left-right-left-right combo at the alternating cadence.
                foreach(var (at,hand) in new[]{(10,0),(60,1),(110,0),(126,1),(142,0),(158,1)})
                {if(tick==at)Buttons(0,1<<hand);if(tick==at+2)Buttons(0,0);}
                return;
            }
            if(mode=="GrabStanding")
            {
                // Walk in holding the left hand (grab), add the right hand, drag backwards, release. A standing
                // opponent is only held: the release must not throw it.
                var delta=_pair.Players[1].Rig.Actor.Motor.Body.position-_pair.Players[0].Rig.Actor.Motor.Body.position;delta.y=0;
                var forward=new Vector2(delta.x,delta.z).normalized;
                if(tick==5)Buttons(0,1);if(tick==50)Buttons(0,3);if(tick==170)Buttons(0,0);
                _pair.Players[0].Move=tick<50?forward*Mathf.Clamp((delta.magnitude-.36f)*4,0,.5f):tick>=90&&tick<165?-forward*.5f:Vector2.zero;return;
            }
            if(mode=="GrabDown")
            {
                // Knock the target down with a head-height combo, then walk to the limp body, grab it with
                // both hands (it is lifted overhead), carry it a few steps and release to throw it forward.
                var target=_pair.Players[1].Rig;
                var delta=target.Actor.Motor.Body.position-_pair.Players[0].Rig.Actor.Motor.Body.position;delta.y=0;
                var forward=new Vector2(delta.x,delta.z).normalized;
                if(_downTick<0&&target.KnockedDown)_downTick=tick;
                if(_downTick<0)
                {
                    _pair.AimHeightOffset=.14f;var age=tick-10;
                    if(age>=0){var strikeSide=(age/16)%2;if(age%16==0)Buttons(0,1<<strikeSide);if(age%16==2)Buttons(0,0);}
                    _pair.Players[0].Move=forward*Mathf.Clamp((delta.magnitude-.40f)*6,0,1);return;
                }
                var since=tick-_downTick;
                // Carry back toward the middle of the floor (the combo drove both near its edge).
                if(since==30){Buttons(0,3);var home=-_pair.Players[0].Rig.Actor.Motor.Body.position;_carry=new Vector2(home.x,home.z).normalized;}if(since==210)Buttons(0,0);
                // Walk up to the limp body's chest (where it lies differs per fall), until a hand holds it.
                var bodies=new Rigidbody[16];var count=target.Knockdown.CopyDynamicBodies(bodies);
                var chest=bodies.Take(count).FirstOrDefault(b=>b.name.StartsWith("Chest"));
                var toChest=(chest!=null?chest.position:target.Actor.Motor.Body.position)-_pair.Players[0].Rig.Actor.Motor.Body.position;toChest.y=0;
                var approach=since<130&&_pair.LogicalGrips==0?new Vector2(toChest.x,toChest.z).normalized*Mathf.Clamp((toChest.magnitude-.32f)*4,0,.6f):Vector2.zero;
                _pair.Players[0].Move=since>=150&&since<200?_carry*.4f:approach;return;
            }
            if(mode=="KOVariety"||mode=="KORandom")
            {
                // Knockdowns of the same standing body, each from a fresh reset at the spawn. KOVariety
                // pushes a different way each time (world m/s); KORandom repeats one push, so only the
                // knockdown variation makes the falls differ.
                var shoves=mode=="KORandom"?KOSame:KOShoves;var cycle=tick/KOCycle;var at=tick%KOCycle;
                if(at==0&&cycle<shoves.Length){if(cycle>0)_pair.ResetReview();for(var i=0;i<20;i++)Tick();_pair.Players[1].Rig.BeginKnockdown(shoves[cycle]);}
                _pair.Players[0].Move=Vector2.zero;return;
            }
            if(mode=="GetUp")
            {
                // The limp body laid still face down, face up, on its left and on its right side, head along its
                // facing; each is left to get up (2.5 s Down wait, then the get-up).
                var cycle=tick/GetUpCycle;var at=tick%GetUpCycle;var rig=_pair.Players[1].Rig;
                if(at==0&&cycle<4)
                {
                    if(cycle>0)_pair.ResetReview();for(var i=0;i<20;i++)Tick();
                    var head=Vector3.ProjectOnPlane(rig.Actor.Motor.Body.rotation*Vector3.forward,Vector3.up).normalized;var flank=Vector3.Cross(Vector3.up,head);
                    var face=cycle==0?Vector3.down:cycle==1?Vector3.up:cycle==2?flank:-flank;
                    rig.BeginLyingForReview(Quaternion.LookRotation(face,head));
                }
                _pair.Players[0].Move=Vector2.zero;return;
            }
            if(mode=="Dropkick"||mode=="DropkickMiss")
            {
                // Run (Shift) at the opponent and tap a hand at ~1 m: a dropkick, never a punch. The miss runs at a
                // far opponent and kicks the air with the right hand (the left-hand kick looks left, the right one right). Then stand still and watch the landing and get-up.
                var runner=_pair.Players[0];var delta=_pair.Players[1].Rig.Actor.Motor.Body.position-runner.Rig.Actor.Motor.Body.position;delta.y=0;
                if(_kickTick<0&&tick>=10&&(mode=="Dropkick"?delta.magnitude<KickDistance:tick>=80)){_kickTick=tick;Buttons(0,mode=="DropkickMiss"?2:1);}
                if(_kickTick>=0&&tick==_kickTick+2)Buttons(0,0);
                runner.Sprint=tick>=10&&(_kickTick<0||tick<=_kickTick+2);runner.Move=runner.Sprint?new Vector2(delta.x,delta.z).normalized:Vector2.zero;return;
            }
            if(mode=="Gait")
            {
                // Walk toward the far opponent, stop, then run (Shift) the same way, and stop.
                _pair.Players[0].Sprint=tick>=150&&tick<240;
                _pair.Players[0].Move=tick>=20&&tick<110||tick>=150&&tick<240?new Vector2(0,1):Vector2.zero;return;
            }
            if(mode=="WallClimbHigh")
            {
                // A wall twice the body's height: walk up with the hands raised until they catch the face and the
                // body hangs, then jump about every .75 s: each hop lets go and catches the wall higher up, and a
                // jump with the hands on the top edge pulls over (leaning forward throughout). Let go on top.
                var rig=_pair.Players[0].Rig;var climber=rig.Actor.Motor.Body;var hanging=rig.ClimbingRagdoll;
                if(tick==15)Buttons(0,3);
                if(hanging&&_climbTick<0)_climbTick=tick;
                if(_topTick<0&&!rig.RagdollActive&&climber.position.y>HighWall-.05f)_topTick=tick;
                if(_topTick>=0&&tick==_topTick+10)Buttons(0,0);
                _pair.Players[0].Jump=hanging&&tick>=_climbTick+40&&(tick-_climbTick)%45<2;
                var approach=tick>=40&&_climbTick<0&&-.70f-climber.position.z>.30f;
                _pair.Players[0].Move=approach?new Vector2(0,.35f):_climbTick>=0&&(_topTick<0||tick<_topTick+60)?new Vector2(0,.6f):Vector2.zero;return;
            }
            if(mode=="WallClimb")
            {
                // Raise both hands toward the ledge (grab), walk in until they catch it, jump while holding and
                // keep pressing forward: the arms pull the body up and over. Let go once standing on top.
                var climber=_pair.Players[0].Rig.Actor.Motor.Body;
                // Keep holding until standing on top again (the climb itself is a hanging ragdoll).
                var rigNow=_pair.Players[0].Rig;
                if(tick==15)Buttons(0,3);if(_topTick<0&&!rigNow.RagdollActive&&climber.position.y>LedgeHeight-.03f)_topTick=tick;if(_topTick>=0&&tick==_topTick+10||tick==480)Buttons(0,0);
                var holding=_pair.Players[0].Hands.Count(h=>h.Phase==ActiveGrabPairReview.HandPhase.Holding);
                if((holding>0||rigNow.ClimbingRagdoll)&&_climbTick<0)_climbTick=tick;
                _pair.Players[0].Jump=_climbTick>=0&&tick>=_climbTick+30&&(rigNow.ClimbingRagdoll||tick<_climbTick+34)&&(tick-_climbTick-30)%45<2;
                // Stop short of the face (z -.70) until both hands are above the top, then step in.
                var handsUp=_pair.Players[0].Rig.HandPosition(0).y>LedgeHeight+.03f&&_pair.Players[0].Rig.HandPosition(1).y>LedgeHeight+.03f;
                var approach=holding==0&&_climbTick<0&&(-.70f-climber.position.z>.45f||handsUp);
                // Lean forward while hanging (it only acts once the chest is over the top) and walk on after.
                _pair.Players[0].Move=tick>=40&&approach||_climbTick>=0&&tick>=_climbTick+30&&(_topTick<0||tick<_topTick+60)?new Vector2(0,.6f):Vector2.zero;return;
            }
            if(mode=="Knockout")
            {
                // KO limp check: the pelvis height when the Down wait ends (lying flat, not kneeling on the feet).
                var down=_pair.Players[1].Rig.Knockdown;
                if(_pair.Players[1].Rig.KnockedDown&&down.Down.State.Phase==ProjectHotfix.Simulation.RecoveryPresentationPhase.PhysicsTumble)_koRestHeight=_pair.Players[1].Rig.Actor.Motor.Body.position.y;
                // Head-height left/right combo while following the target: shows the PRD point threshold
                // knocking it down, the limp wait and the GetUp. Punches into a downed target only push it.
                _pair.AimHeightOffset=.14f;
                // Once the target is down the attacker steps off and waits, leaving the GetUp clearance.
                if(_pair.Players[1].Rig.KnockedDown){Buttons(0,0);_pair.Players[0].Move=Vector2.zero;return;}
                var age=tick-10;if(age>=0){var strikeSide=(age/16)%2;if(age%16==0)Buttons(0,1<<strikeSide);if(age%16==2)Buttons(0,0);}
                var delta=_pair.Players[1].Rig.Actor.Motor.Body.position-_pair.Players[0].Rig.Actor.Motor.Body.position;delta.y=0;
                _pair.Players[0].Move=new Vector2(delta.x,delta.z).normalized*Mathf.Clamp((delta.magnitude-.40f)*6,0,1);return;
            }
            if(mode=="Alternating"||mode=="Moving")
            {
                var age=tick-10;if(age>=0){var strikeSide=(age/16)%2;if(age%16==0)Buttons(0,1<<strikeSide);if(age%16==2)Buttons(0,0);}
                var delta=_pair.Players[1].Rig.Actor.Motor.Body.position-_pair.Players[0].Rig.Actor.Motor.Body.position;delta.y=0;
                // The stronger strike displaces the opponent. Pursue at a
                // normal walking input rather than the old small shuffle.
                var chase=_pair.Players[0].Rig.StrikeStyle==ActiveUpperBodyReview.ReviewStrikeStyle.BodyStrike?.65f:.35f;
                var along=new Vector2(delta.x,delta.z).normalized;var approach=Mathf.Clamp((delta.magnitude-.36f-Spacing)*(chase>.35f?6:4),0,chase);
                var strafe=mode=="Moving"?Mathf.Sin(tick*Dt*3)*.20f:0;
                _pair.Players[0].Move=along*approach+new Vector2(along.y,-along.x)*strafe;return;
            }
            if(mode=="GrabPunch")
            {
                if(tick==0)Buttons(0,1);var delta=_pair.Players[1].Rig.Actor.Motor.Body.position-_pair.Players[0].Rig.Actor.Motor.Body.position;delta.y=0;var amount=tick<65?Mathf.Clamp((delta.magnitude-.38f)*4,0,.25f):0;_pair.Players[0].Move=new Vector2(delta.x,delta.z).normalized*amount;
                var release=_pair.Players[0].Rig.StrikeStyle==ActiveUpperBodyReview.ReviewStrikeStyle.Straight?145:105;
                if(tick==70)Buttons(0,3);if(tick==72)Buttons(0,1);if(tick==release)Buttons(0,0);return;
            }
            var side=mode=="RightBody"?1:0;var phase=tick%60;
            if(phase==10)Buttons(0,1<<side);if(phase==12)Buttons(0,0);
            var displacement=_pair.Players[0].Rig.Actor.Motor.Body.position-_pair.Players[1].Rig.Actor.Motor.Body.position;displacement.y=0;var distance=displacement.magnitude;
            // A landed body strike now shoves the opponent ~0.3 m: step back in before
            // the next strike, as a player would. Far/Wall stay in place.
            var shoved=_pair.Players[0].Rig.StrikeStyle==ActiveUpperBodyReview.ReviewStrikeStyle.BodyStrike;
            var stepIn=shoved?Mathf.Clamp((distance-.38f-Spacing)*5,0,.6f):.15f;
            var stepWindow=shoved?phase>28&&phase<58:phase>43&&phase<55;
            var stepDirection=new Vector2(-displacement.x,-displacement.z).normalized;
            _pair.Players[0].Move=mode!="Far"&&mode!="Wall"&&stepWindow&&distance>.39f+Spacing?stepDirection*stepIn:Vector2.zero;
        }
        [UnityTest, Timeout(600000)] public IEnumerator CapturePhysicalPunchScenarios()
        {
            var results=new List<Clip>();var failures=new List<string>();
            var modes=Environment.GetEnvironmentVariable("PARTY_CAPTURE_CASES")?.Split(',')??new[]{"LeftBody","RightBody","Head","Far","Wall","GrabPunch","Alternating","Moving"};
            foreach(var mode in modes)
            {
                _downTick=-1;_kickTick=-1;_koRestHeight=-1;_zoom=.8f;_climbTick=-1;_topTick=-1;
                Create(mode=="GrabStanding"?.5f:mode=="Solo"||mode=="WallClimb"||mode=="WallClimbHigh"?3f:mode=="Gait"||mode=="DropkickMiss"?9f:mode=="Dropkick"?3f:mode=="KOVariety"||mode=="KORandom"||mode=="GetUp"?3f:mode=="Far"?FarSeparation:mode=="GrabPunch"?.5f:(mode=="Head"?.42f:.38f)+Spacing,mode=="Head"?.22f:0,mode=="Wall");
                // Ledge LedgeHeight high, its face .8 m ahead of the climber (roots at z -1.5 and 1.5).
                if(mode=="WallClimbHigh"){Block("High wall",new Vector3(0,HighWall*.5f,.4f),new Vector3(3f,HighWall,2.2f));UnityEngine.Physics.SyncTransforms();}
                if(mode=="WallClimb"){Block("Ledge",new Vector3(0,LedgeHeight*.5f,.4f),new Vector3(3f,LedgeHeight,2.2f));UnityEngine.Physics.SyncTransforms();}
                var folder=Path.Combine(Output,"Frames",mode);Directory.CreateDirectory(folder);var rt=new RenderTexture(800,600,24);rt.Create();var image=new Texture2D(800,600,TextureFormat.RGB24,false);_pair.View.targetTexture=rt;_pair.View.aspect=4f/3;_pair.View.orthographicSize=.80f;
                var renderers=_pair.Players.Select(p=>p.Rig.Actor.Pose.GetComponentsInChildren<SkinnedMeshRenderer>().Single(s=>s.name.EndsWith("SkinnedBody"))).ToArray();var mesh=new Mesh();var tri=renderers[0].sharedMesh.triangles;var pairs=CameraGaitReviewTests.Pairs(tri);var ticks=mode=="Knockout"||mode=="GrabDown"?720:mode=="GrabStanding"?420:mode=="WallClimb"?540:mode=="WallClimbHigh"?1200:mode=="Gait"?300:mode.StartsWith("Dropkick")?420:mode=="KOVariety"?KOCycle*6:mode=="KORandom"?KOCycle*8:mode=="GetUp"?GetUpCycle*4:180;var clip=new Clip{name=mode,frames=new Frame[ticks]};
                for(var tick=0;tick<ticks;tick++)
                {
                    if(Environment.GetEnvironmentVariable("PARTY_PUNCH_DEBUG")=="1"&&_pair.PhysicalPunches!=null&&_pair.PhysicalPunches.ContactLog==null)_pair.PhysicalPunches.ContactLog=new List<string>{$"start tick {_pair.Tick} = capture tick {tick}"};CaptureInput(mode,tick);Tick();var angle=0f;for(var actor=0;actor<renderers.Length;actor++){var skin=renderers[actor];skin.BakeMesh(mesh);var measured=CameraGaitReviewTests.Surface(mesh.vertices,tri,pairs,out var point);angle=Mathf.Max(angle,measured);if(tick==0&&actor==0)ExportSurfacePose(skin,mode+"_Tick0");if(actor==0&&Environment.GetEnvironmentVariable("PARTY_EXPORT_ALL_TICKS")=="1")ExportSurfacePose(skin,$"{mode}_T{tick:D3}");if(measured>clip.maxSkin){clip.maxSkin=measured;clip.worstPoint=point;clip.worstActor=actor;clip.worstTick=tick;ExportSurfacePose(skin,mode+"_Worst");}}
                    var punch=_pair.PhysicalPunches;clip.maxSkin=Mathf.Max(clip.maxSkin,angle);clip.frames[tick]=new Frame{tick=tick,punches=punch.State(0).PunchCount,hits=punch.Hits.Count,points=punch.Score(1).Points,grips=_pair.LogicalGrips,throws=_pair.Throws,cues=punch.ConfirmedCues,sourceAge=(float)punch.State(0).SourcePunchAge(mode=="RightBody"||mode=="GrabPunch"?1:0),skin=angle,tip=_pair.Players[0].Rig.HandPosition(mode=="RightBody"||mode=="GrabPunch"?1:0),root=_pair.Players[0].Rig.Actor.Motor.Body.position,targetRoot=_pair.Players[1].Rig.Actor.Motor.Body.position,targetDown=_pair.Players[1].Rig.KnockedDown,knockdowns=_pair.Players[1].Rig.Knockdowns,climbs=_pair.Players[0].Climbs,boneHandL=Bone(0,"HandLogical_L"),boneHandR=Bone(0,"HandLogical_R"),boneForearmL=Bone(0,"Forearm_L"),boneForearmR=Bone(0,"Forearm_R")};
                    _pair.Present(1);var center=(mode=="KOVariety"||mode=="KORandom"||mode=="GetUp"?_pair.Players[1].Rig.Actor.Motor.Body.position:mode=="Solo"||mode=="WallClimb"||mode=="WallClimbHigh"||mode=="Gait"||mode=="DropkickMiss"?_pair.Players[0].Rig.Actor.Motor.Body.position:(_pair.Players[0].Rig.Actor.Motor.Body.position+_pair.Players[1].Rig.Actor.Motor.Body.position)*.5f)+Vector3.up*.52f;var offset=mode=="WallClimbHigh"?new Vector3(-2.6f,.6f,-1.9f):mode=="Gait"||mode=="GetUp"||mode.StartsWith("Dropkick")?new Vector3(2.6f,.6f,0):mode=="WallClimb"?new Vector3(-2.2f,.8f,-1.7f):new Vector3(mode=="RightBody"||mode=="GrabPunch"?2.3f:-2.3f,1.05f,1.25f);if(mode=="WallClimb")_pair.View.orthographicSize=1.05f;if(mode.StartsWith("Dropkick")){_pair.View.orthographicSize=1.1f;center-=Vector3.up*.15f;}if(mode=="WallClimbHigh"){_pair.View.orthographicSize=1.35f;center+=Vector3.up*.3f;}if(mode=="KOVariety"||mode=="KORandom"){center-=Vector3.up*.35f;_pair.View.orthographicSize=.75f;}if(mode=="GetUp"){center=(Bone(1,"Head")+Bone(1,"Foot_L")+Bone(1,"Foot_R"))/3;center.y=.42f;_pair.View.orthographicSize=.62f;}if(mode.StartsWith("Grab")&&mode!="GrabPunch"){var span=Vector3.Distance(_pair.Players[0].Rig.Actor.Motor.Body.position,_pair.Players[1].Rig.Actor.Motor.Body.position);_zoom=Mathf.Max(_zoom,Mathf.Clamp(.45f+span*.45f,.8f,2.6f));_pair.View.orthographicSize=_zoom;}_pair.View.transform.SetPositionAndRotation(center+offset,Quaternion.LookRotation(-offset));
                    yield return null;_pair.View.Render();var old=RenderTexture.active;RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,800,600),0,0);image.Apply();RenderTexture.active=old;File.WriteAllBytes(Path.Combine(folder,$"Frame_{tick:D4}.png"),image.EncodeToPNG());
                }
                clip.hits=_pair.PhysicalPunches.Hits.ToArray();results.Add(clip);if(_pair.PhysicalPunches.ContactLog!=null)File.WriteAllLines(Path.Combine(Output,mode+"_Contacts.tsv"),_pair.PhysicalPunches.ContactLog);File.WriteAllText(Path.Combine(Output,"Capture.json"),JsonUtility.ToJson(new Report{clips=results.ToArray()},true));
                if(clip.maxSkin>=60)failures.Add(mode+" skin "+clip.maxSkin);
                if((mode=="LeftBody"||mode=="RightBody"||mode=="Head")&&clip.hits.Length==0)failures.Add(mode+" no hit");
                if((mode=="Far"||mode=="Wall")&&clip.hits.Length!=0)failures.Add(mode+" false hit");
                // A standing opponent is only held: releasing the grab never throws it (2026-10-17).
                if(mode=="GrabPunch"&&(_pair.PhysicalPunches.State(0).PunchCount!=1||_pair.Throws!=0))failures.Add(mode+" action arbitration");
                if(mode=="GrabStanding"&&(_pair.Throws!=0||clip.frames.Any(f=>f.targetDown)))failures.Add(mode+" standing target thrown");
                if(mode=="GrabDown"&&_pair.Throws!=1)failures.Add(mode+" downed target not thrown");
                if(mode=="WallClimbHigh"&&(clip.frames[ticks-1].climbs<1||clip.frames[ticks-1].root.y<HighWall-.05f))failures.Add(mode+" did not climb the high wall");
                if(mode=="WallClimb"&&(clip.frames[ticks-1].climbs<1||clip.frames[ticks-1].root.y<LedgeHeight-.05f))failures.Add(mode+" did not climb the ledge");
                // Running hands only dropkick (2026-10-20): no punch while running; a landed kick knocks the target down,
                // and the kicker is standing again by the end.
                if(mode.StartsWith("Dropkick")&&(clip.frames[ticks-1].punches!=0||_pair.Players[0].Rig.Dropkicks!=1))failures.Add(mode+" running tap was not exactly one dropkick");
                if(mode=="Dropkick"&&(!clip.hits.Any(h=>h.region=="Dropkick")||!clip.frames.Any(f=>f.targetDown)))failures.Add(mode+" did not knock the target down");
                // Knocked out = fully limp (every ragdoll drive 0) and lying on the floor, not propped on its feet.
                if(mode=="Knockout")
                {
                    var limp=_pair.Players[1].Rig.Knockdown.MaximumLimpDriveSpring;Debug.Log($"KO limp check: max drive spring {limp}, pelvis height at end of wait {_koRestHeight:F3}");
                    if(limp>0)failures.Add(mode+" knocked-out body still has joint drive "+limp);
                    if(_koRestHeight<0||_koRestHeight>.16f)failures.Add(mode+" knocked-out body not lying flat (pelvis "+_koRestHeight.ToString("F3")+" m)");
                }
                if(mode=="DropkickMiss"&&clip.hits.Length!=0)failures.Add(mode+" false hit");
                if(mode.StartsWith("Dropkick")&&_pair.Players[0].Rig.RagdollActive)failures.Add(mode+" kicker still down at the end");
                if(mode=="GrabPunch"&&!clip.frames.Any(f=>f.grips>0&&f.hits>0))failures.Add("GrabPunch missed while held");
                if(mode=="Exchange"&&!clip.hits.Any(h=>h.attacker==1))failures.Add("Exchange missing return strike");
                if((mode=="Alternating"||mode=="Moving")&&(clip.frames[179].punches!=11||clip.hits.Length<8))failures.Add(mode+" close-range alternating contact is unreliable");
                if(clip.frames.Any(f=>f.cues!=f.hits))failures.Add(mode+" false feedback");
                _pair.View.targetTexture=null;rt.Release();UnityEngine.Object.Destroy(rt);UnityEngine.Object.Destroy(image);UnityEngine.Object.Destroy(mesh);var op=SceneManager.UnloadSceneAsync(_scene);while(!op.isDone)yield return null;
            }
            Assert.That(failures,Is.Empty,string.Join("\n",failures));
        }
    }
}
