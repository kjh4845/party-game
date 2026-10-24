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
        private int _downTick=-1,_kickTick=-1;private float _koRestHeight=-1;private int _upTick=-1;private bool _protectHeld,_protectExpired;private float _zoom=.8f;private Vector2 _carry;
        private const int KOCycle=200,CrowdCycle=540,CrowdCases=8;private bool _riseGrabPressed,_grabbedWhileRising,_risingKickIgnored;private int _risingTicks,_riseKickDowns=-1,_risePoints=-1;private Vector3 _pushStart;private int _stage,_releaseTick=-1,_gripTick=-1,_throwTick=-1,_throwCount;private Vector3 _throwFrom;private float _throwDistance=-1;private readonly HashSet<string> _heldParts=new HashSet<string>();private float _pushDrift=-1,_pushDepth,_skinDeepest;private int _pushInside,_skinInside;private string _skinWhere="";private GameObject _crowdProp;private bool _crowdArrived;private readonly int[] _crowdInside=new int[8];private GameObject _crowdProp2;private SkinnedMeshRenderer[] _crowdRenderers;
        private const int LowSpotTicks=190,LowSpotStart=60;private readonly int[] _lowSpotHits=new int[3];private float _lowFistMin=float.PositiveInfinity,_lowLeanMax;private readonly float[] _lowPunchGap=new float[12];private int _lowDownsAtStart=-1,_lowPointsAtStart=-1;
        // Crowd: the most skin vertices of the downed body (every 4th) inside the prop or the walker's capsule in any one
        // frame of each case while its get-up plays (a sunk-in arm or chest).
        // While one character is down: how many of either one's skin vertices (every 4th) sit more than PushSkinTolerance
        // inside the other's colliders (the downed ragdoll's proxies, or the standing one's root capsule and links).
        // Touching skins overlap a little at the surface; going deeper is passing through.
        private const float PushSkinTolerance=.02f;
        // A throw's carry: how far the thrown body's chest lands (horizontally) from where it was let go, 1.5 s later;
        // and which of its parts the hands held.
        private void TrackThrow(int tick)
        {
            var target=_pair.Players[1].Rig;
            foreach(var hand in _pair.Players[0].Hands)if(hand.Phase==ActiveGrabPairReview.HandPhase.Holding&&hand.TargetRig==target&&hand.TargetBody!=null)_heldParts.Add(hand.TargetBody.name.Replace("_Physics",""));
            if(!target.RagdollActive)return;
            var bodies=new Rigidbody[16];var count=target.Knockdown.CopyDynamicBodies(bodies);var chest=bodies.Take(count).FirstOrDefault(b=>b.name.StartsWith("Chest"));if(chest==null)return;
            if(_pair.Throws>_throwCount){_throwCount=_pair.Throws;_throwTick=tick;_throwFrom=chest.position;}
            if(_throwTick>=0&&tick==_throwTick+90){var d=chest.position-_throwFrom;d.y=0;_throwDistance=d.magnitude;}
        }
        private void PushSkinInside(int actor,SkinnedMeshRenderer skin,Mesh mesh)
        {
            var downed=_pair.Players[1].Rig;if(!downed.RagdollActive)return;
            var standing=_pair.Players[0].Rig;var blockers=new List<Collider>();
            // A landing punch's fist sinks into the downed skin (its proxies are thinner than the skin); punching arms are
            // left out here, as SeparateFromDowned leaves them out.
            if(actor==1){blockers.AddRange(standing.Actor.Motor.Body.GetComponents<Collider>());blockers.AddRange(standing.Links.Where(l=>!(l.Name.StartsWith("Forearm")||l.Name.StartsWith("UpperArm"))||!_pair.PhysicalPunches.IsPunching(0,l.Name.EndsWith("_L")?0:1)).Select(l=>l.Shape));}
            else{var bodies=new Rigidbody[32];var count=downed.Knockdown.CopyDynamicBodies(bodies);for(var i=0;i<count;i++)blockers.AddRange(bodies[i].GetComponents<Collider>());}
            blockers.RemoveAll(b=>b==null||!b.enabled||b.isTrigger);
            var world=Matrix4x4.TRS(skin.transform.position,skin.transform.rotation,Vector3.one);var vertices=mesh.vertices;var inside=0;
            for(var i=0;i<vertices.Length;i+=4)
            {
                var point=world.MultiplyPoint3x4(vertices[i]);var depth=0f;
                foreach(var b in blockers)depth=Mathf.Max(depth,Depth(b,point));
                _pushDepth=Mathf.Max(_pushDepth,depth);if(depth>PushSkinTolerance)inside++;
            }
            _pushInside=Mathf.Max(_pushInside,inside);
        }
        // How deep a point is inside a capsule, sphere or box collider (0 outside).
        private static float Depth(Collider collider,Vector3 point)
        {
            if(collider is CapsuleCollider capsule)
            {
                var t=capsule.transform;var axis=capsule.direction==0?Vector3.right:capsule.direction==1?Vector3.up:Vector3.forward;
                var scale=t.lossyScale;var radius=capsule.radius*Mathf.Max(Mathf.Abs(scale[(capsule.direction+1)%3]),Mathf.Abs(scale[(capsule.direction+2)%3]));
                var half=Mathf.Max(0,capsule.height*.5f*Mathf.Abs(scale[capsule.direction])-radius);
                var a=t.TransformPoint(capsule.center)+t.TransformDirection(axis)*half;var b=t.TransformPoint(capsule.center)-t.TransformDirection(axis)*half;
                var ab=b-a;var s=ab.sqrMagnitude>1e-8f?Mathf.Clamp01(Vector3.Dot(point-a,ab)/ab.sqrMagnitude):0;
                return Mathf.Max(0,radius-Vector3.Distance(point,a+ab*s));
            }
            if(collider is SphereCollider sphere){var t=sphere.transform;var scale=t.lossyScale;return Mathf.Max(0,sphere.radius*Mathf.Max(Mathf.Abs(scale.x),Mathf.Abs(scale.y),Mathf.Abs(scale.z))-Vector3.Distance(point,t.TransformPoint(sphere.center)));}
            if(collider is BoxCollider box)
            {
                var local=box.transform.InverseTransformPoint(point)-box.center;var extent=box.size*.5f;var scale=box.transform.lossyScale;
                var dx=(extent.x-Mathf.Abs(local.x))*Mathf.Abs(scale.x);var dy=(extent.y-Mathf.Abs(local.y))*Mathf.Abs(scale.y);var dz=(extent.z-Mathf.Abs(local.z))*Mathf.Abs(scale.z);
                return dx>0&&dy>0&&dz>0?Mathf.Min(dx,Mathf.Min(dy,dz)):0;
            }
            return (collider.ClosestPoint(point)-point).sqrMagnitude<1e-8f?PushSkinTolerance*2:0;
        }
        private void CrowdSkinInside(SkinnedMeshRenderer skin,Mesh mesh,int tick)
        {
            var cycle=tick/CrowdCycle;var getUp=_pair.Players[1].Rig.Knockdown.GetUpPose;if(cycle>=CrowdCases||!_pair.Players[1].Rig.RagdollActive||getUp==null||!getUp.Playing)return;
            // Props are judged by their colliders (a box is what is drawn); the standing neighbour by its skin
            // (SkinGapNow): its root capsule is ~5 cm wider than the legs it covers (2026-10-22).
            var blockers=new List<Collider>();foreach(var prop in new[]{_crowdProp,_crowdProp2})if(prop!=null)blockers.Add(prop.GetComponent<Collider>());
            var world=Matrix4x4.TRS(skin.transform.position,skin.transform.rotation,Vector3.one);var vertices=mesh.vertices;var inside=0;
            for(var i=0;i<vertices.Length;i+=4){var point=world.MultiplyPoint3x4(vertices[i]);if(blockers.Any(b=>b!=null&&b.enabled&&(b.ClosestPoint(point)-point).sqrMagnitude<1e-8f))inside++;}
            if(tick%2==0)inside+=SkinGapNow(_crowdRenderers).inside;
            _crowdInside[cycle]=Mathf.Max(_crowdInside[cycle],inside);
        }private const int GetUpCycle=480,GetUpCases=6;
        // How far the last two GetUp cases are tipped from the side onto the back (tan of the angle; PARTY_GETUP_TILT).
        private static float GetUpTilt=>float.TryParse(Environment.GetEnvironmentVariable("PARTY_GETUP_TILT"),System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out var value)?value:.2f;
        private static readonly Vector3[] KOShoves={new Vector3(0,0,1.2f),new Vector3(0,0,-1.2f),new Vector3(1.2f,0,0),new Vector3(-1.2f,0,0),new Vector3(1.2f,0,1.2f),new Vector3(0,.4f,2.2f)};
        private static readonly Vector3[] KOSame=Enumerable.Repeat(new Vector3(0,0,1.2f),8).ToArray();private int _climbTick=-1,_topTick=-1;
        private const float HighWall=1.6f;
        // Root-to-root distance at which the Dropkick scene taps (PARTY_KICK_AT).
        private static int RiseKickDelay=>int.TryParse(Environment.GetEnvironmentVariable("PARTY_RISE_KICK_DELAY"),out var value)?value:5;
        private static float RisePunchDistance=>float.TryParse(Environment.GetEnvironmentVariable("PARTY_RISE_PUNCH_AT"),System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out var value)?value:.9f;
        private static float RiseStepIn=>float.TryParse(Environment.GetEnvironmentVariable("PARTY_RISE_STEP_IN"),System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out var value)?value:.6f;
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
            if(mode=="GrabHead"||mode=="GrabLegs"||mode=="GrabArm")
            {
                // Grabs away from the trunk (2026-10-22: any part may be held, the throw must still work): knock the target
                // straight down, walk round the body (past its side) to stand beyond its head or its feet, or beside its
                // nearer arm, and only then reach (both hands; one for the arm); let go as soon as it is lifted. A 5 s
                // Down wait: these check that a hold on the part lifts and throws, not whether the walk round fits 2.5 s.
                var target=_pair.Players[1].Rig;var me=_pair.Players[0].Rig.Actor.Motor.Body.position;
                if(tick==1)target.Knockdown.Down.Bind(target.Actor,new ProjectHotfix.Simulation.DownTimingProfile(5,.5,6),true);
                if(tick==5)target.BeginKnockdown(new Vector3(0,0,.6f));
                if(_downTick<0&&target.KnockedDown)_downTick=tick;
                if(_downTick<0||tick-_downTick<40){_pair.Players[0].Move=Vector2.zero;return;}
                var bodies=new Rigidbody[16];var count=target.Knockdown.CopyDynamicBodies(bodies);
                if(!new[]{"Head","Chest","Calf_L","Calf_R","Forearm_L","Forearm_R"}.All(n=>bodies.Take(count).Any(b=>b.name.StartsWith(n)))){_pair.Players[0].Move=Vector2.zero;return;}
                Vector3 Part(string name)=>bodies.Take(count).First(b=>b.name.StartsWith(name)).position;
                Vector3 Flat(Vector3 v){v.y=0;return v;}
                Vector2 Walk(Vector3 to,float stop,float speed){var d=Flat(to-me);return new Vector2(d.x,d.z).normalized*Mathf.Clamp((d.magnitude-stop)*4,0,speed);}
                var pelvis=target.Actor.Motor.Body.position;var along=Flat(Part("Head")-pelvis).normalized;var across=new Vector3(along.z,0,-along.x);if(Vector3.Dot(me-pelvis,across)<0)across=-across;
                Vector3 part,spot,waypoint;
                if(mode=="GrabHead"){part=Part("Head");spot=part+along*.36f;waypoint=part+across*.55f;}
                else if(mode=="GrabLegs"){part=(Part("Calf_L")+Part("Calf_R"))*.5f;spot=part-along*.36f;waypoint=part+across*.55f;}
                else{var l=Part("Forearm_L");var r=Part("Forearm_R");part=Vector3.Dot(l-r,across)>0?l:r;spot=part+across*.36f;waypoint=spot;}
                if(_gripTick<0&&_pair.LogicalGrips>0)_gripTick=tick;
                if(_gripTick<0)
                {
                    // Stage 0: to the body's side by the part (skipped when already nearer the part than the hips);
                    // 1: round to the spot beyond it; 2: reach and close in.
                    if(_stage==0&&(Flat(waypoint-me).magnitude<.15f||Flat(part-me).magnitude<Flat(pelvis-me).magnitude-.1f))_stage=1;
                    if(_stage==1&&Flat(spot-me).magnitude<.1f){_stage=2;Buttons(0,mode=="GrabArm"?1:3);}
                    if(_stage<2&&tick-_downTick>200){_stage=2;Buttons(0,mode=="GrabArm"?1:3);}
                    _pair.Players[0].Move=_stage==0?Walk(waypoint,0,1):_stage==1?Walk(spot,0,.8f):Walk(part,.3f,.4f);
                    return;
                }
                var held=tick-_gripTick;var lifted=_pair.Players[0].Hands.Where(h=>h.Phase==ActiveGrabPairReview.HandPhase.Holding).Select(h=>h.LiftRise).DefaultIfEmpty(0).Min()>=_pair.LiftHeight*.95f;
                if(_releaseTick<0&&(lifted||held>=90)){_releaseTick=tick;Buttons(0,0);}
                _pair.Players[0].Move=Vector2.zero;return;
            }
            if(mode=="GrabDown"||mode=="GrabHoldLong"||mode=="GrabRise")
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
                // GrabHoldLong never lets go: what happens when the Down time runs out in the carrier's hands.
                if(since==30&&mode!="GrabRise"){Buttons(0,3);var home=-_pair.Players[0].Rig.Actor.Motor.Body.position;_carry=new Vector2(home.x,home.z).normalized;}if(since==210&&mode=="GrabDown")Buttons(0,0);
                // Walk up to the limp body's chest (where it lies differs per fall), until a hand holds it.
                var bodies=new Rigidbody[16];var count=target.Knockdown.CopyDynamicBodies(bodies);
                var chest=bodies.Take(count).FirstOrDefault(b=>b.name.StartsWith("Chest"));
                var toChest=(chest!=null?chest.position:target.Actor.Motor.Body.position)-_pair.Players[0].Rig.Actor.Motor.Body.position;toChest.y=0;
                var approach=since<130&&_pair.LogicalGrips==0?new Vector2(toChest.x,toChest.z).normalized*Mathf.Clamp((toChest.magnitude-.32f)*4,0,.6f):Vector2.zero;
                // GrabRise: both hands reach for the body only once its get-up is moving, and keep reaching while it
                // rises; none may take hold before it stands (get-up invulnerability, 2026-10-21).
                if(mode=="GrabRise")
                {
                    // Only holds on the rising body count (a hand reaching low can take hold of the floor).
                    if(target.GettingUp){_risingTicks++;if(_pair.Players[0].Hands.Any(h=>h.Phase==ActiveGrabPairReview.HandPhase.Holding&&h.TargetRig==target))_grabbedWhileRising=true;}
                    if(target.GettingUp&&!_riseGrabPressed){_riseGrabPressed=true;Buttons(0,3);}
                    if(_riseGrabPressed&&!target.RagdollActive)Buttons(0,0);
                    _pair.Players[0].Move=target.RagdollActive?new Vector2(toChest.x,toChest.z).normalized*Mathf.Clamp((toChest.magnitude-.32f)*4,0,.6f):Vector2.zero;return;
                }
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
            if(mode=="StandOver")
            {
                // Knock the target down toward the other player, who walks onto its feet and stays there: the
                // get-up must shove the standing player off instead of waiting for room forever.
                var target=_pair.Players[1].Rig;if(tick==5)target.BeginKnockdown(new Vector3(0,0,-.6f));
                var bodies=new Rigidbody[16];var count=target.Knockdown.CopyDynamicBodies(bodies);
                // Onto the spot it will stand up in: the feet (a face-down get-up rises over them).
                var feet=target.RagdollActive?bodies.Take(count).Where(b=>b.name.StartsWith("Calf")).Aggregate(Vector3.zero,(sum,b)=>sum+b.position)/2:target.Actor.Motor.Body.position;
                var to=feet-_pair.Players[0].Rig.Actor.Motor.Body.position;to.y=0;
                // Walk up to the feet, then keep leaning on them slowly until the get-up starts: the skin-fitted legs
                // (2026-10-22) keep a walker that stops at them just outside the get-up's room.
                if(_upTick<0&&to.magnitude<.3f)_upTick=tick;
                _pair.Players[0].Move=tick>20&&target.RagdollActive&&!target.Knockdown.Rising?new Vector2(to.x,to.z).normalized*(_upTick<0?Mathf.Clamp(to.magnitude*4,0,.6f):.25f):Vector2.zero;return;
            }
            if(mode=="Jump")
            {
                // Plain jumps (Space): twice standing still, once walking, once running (Shift), then stop.
                var jumper=_pair.Players[0];
                jumper.Jump=tick==20||tick==80||tick==170||tick==260;
                jumper.Sprint=tick>=220&&tick<300;
                jumper.Move=tick>=140&&tick<300?new Vector2(0,1):Vector2.zero;return;
            }
            if(mode=="Protect")
            {
                // Knock the target down; a dropkick while it rises only pushes it (uncounted), the moment it stands it only pushes it
                // (get-up protection), the same hit .75 s later downs it again.
                var rig=_pair.Players[1].Rig;var chest=rig.Find("Chest").Body;
                if(tick==5)rig.BeginKnockdown(new Vector3(0,0,1.2f));
                // A kick while it is rising lands uncounted: no knockdown, it keeps rising.
                if(rig.GettingUp&&!_risingKickIgnored){var downs=rig.Knockdowns;_pair.PhysicalPunches.ConfirmDropkick(0,chest,chest.position,Vector3.forward,3);_risingKickIgnored=rig.Knockdowns==downs&&rig.GettingUp;}
                if(tick>5&&_upTick<0&&!rig.RagdollActive){_upTick=tick;_pair.PhysicalPunches.ConfirmDropkick(0,chest,chest.position,Vector3.forward,3);_protectHeld=!rig.RagdollActive;}
                if(_upTick>=0&&tick==_upTick+45){_pair.PhysicalPunches.ConfirmDropkick(0,chest,chest.position,Vector3.forward,3);_protectExpired=rig.RagdollActive;}
                _pair.Players[0].Move=Vector2.zero;return;
            }
            if(mode=="GetUp")
            {
                // The limp body laid still face down, face up, on its left and on its right side (these settle onto a face
                // by themselves during the wait), then on its left side tipped onto its back and on its right side tipped
                // onto its front (GetUpTilt), its pelvis held from turning until the 2.5 s Down wait ends, so the get-up
                // itself rolls it to the nearer face (face up, face down) before rising.
                var cycle=tick/GetUpCycle;var at=tick%GetUpCycle;var rig=_pair.Players[1].Rig;
                if(at==0&&cycle<GetUpCases)
                {
                    if(cycle>0)_pair.ResetReview();for(var i=0;i<20;i++)Tick();
                    var head=Vector3.ProjectOnPlane(rig.Actor.Motor.Body.rotation*Vector3.forward,Vector3.up).normalized;var flank=Vector3.Cross(Vector3.up,head);
                    var face=cycle==0?Vector3.down:cycle==1?Vector3.up:cycle==2?flank:cycle==3?-flank:cycle==4?(flank+Vector3.up*GetUpTilt).normalized:(-flank-Vector3.up*GetUpTilt).normalized;
                    rig.BeginLyingForReview(Quaternion.LookRotation(face,head));
                }
                if(cycle>=4&&rig.RagdollActive)rig.Actor.Motor.Body.constraints=rig.Knockdown.Down.State.RemainingSeconds>0?RigidbodyConstraints.FreezeRotation:RigidbodyConstraints.None;
                _pair.Players[0].Move=Vector2.zero;return;
            }
            if(mode=="Crowd")
            {
                // Get-ups with no room around them (2026-10-21): the body is laid face up / face down across the lab
                // (head toward +x) and, during the Down wait, something is put right beside it: the other player walks
                // up to its hip or shoulder (cases 0, 1), a fixed box at the hip (2), a wall along its side (3), a loose
                // crate at the hip (4), a fixed box against its feet, where it would stand up (5), the other player knocked down
                // lying beside it (6, both get up together) or fixed boxes close on both sides (7, no room to scoot). The walker stops for good once
                // it arrives, so a shove leaves it where it lands.
                var cycle=tick/CrowdCycle;var at=tick%CrowdCycle;var rig=_pair.Players[1].Rig;var walker=_pair.Players[0];
                if(at==0&&cycle<CrowdCases)
                {
                    _crowdArrived=false;
                    if(cycle>0)_pair.ResetReview();foreach(var prop in new[]{_crowdProp,_crowdProp2})if(prop!=null)UnityEngine.Object.Destroy(prop);_crowdProp=_crowdProp2=null;for(var i=0;i<20;i++)Tick();
                    var up=cycle!=1&&cycle!=3;rig.BeginLyingForReview(Quaternion.LookRotation(up?Vector3.up:Vector3.down,Vector3.right));

                }
                if(cycle>=CrowdCases){walker.Move=Vector2.zero;return;}
                var pelvis=Bone(1,"Pelvis");var chest=Bone(1,"Chest");var near=Vector3.back;
                if(at==30&&cycle==7)
                {
                    foreach(var sign in new[]{1f,-1f}){var center=pelvis+Vector3.forward*sign*.47f;center.y=.25f;var box=Block("Crowd gap box",center,new Vector3(.5f,.5f,.3f));if(sign>0)_crowdProp=box;else _crowdProp2=box;}
                    UnityEngine.Physics.SyncTransforms();
                }
                if(at==30&&cycle>=2&&cycle<=5)
                {
                    var feet=(Bone(1,"Foot_L")+Bone(1,"Foot_R"))*.5f;
                    var at3=cycle==2||cycle==4?pelvis+near*.42f:cycle==3?(pelvis+chest)*.5f+near*.43f:feet+(feet-pelvis).normalized*.22f;
                    var size=cycle==3?new Vector3(1.6f,.8f,.2f):cycle==4?new Vector3(.3f,.3f,.3f):new Vector3(.4f,.5f,.3f);
                    at3.y=size.y*.5f;
                    _crowdProp=Block(cycle==3?"Crowd wall":cycle==4?"Crowd crate":"Crowd box",at3,size);
                    if(cycle==4)_crowdProp.AddComponent<Rigidbody>().mass=6;
                    UnityEngine.Physics.SyncTransforms();
                }
                // The walker stands right beside the hip (face up) or the shoulder (face down); otherwise it keeps clear.
                var spot=cycle==0?pelvis+near*.4f:cycle==1?chest+near*.4f:cycle==6?pelvis+near*.5f:pelvis+near*1.3f;
                var to=spot-walker.Rig.Actor.Motor.Body.position;to.y=0;
                // Case 6: once beside the hip the walker is laid down there, face up and parallel.
                if(at>20&&to.magnitude<=.04f&&!_crowdArrived){_crowdArrived=true;if(cycle==6&&!walker.Rig.RagdollActive)walker.Rig.BeginLyingForReview(Quaternion.LookRotation(Vector3.up,Vector3.right));}
                walker.Move=at>20&&!_crowdArrived&&rig.RagdollActive?new Vector2(to.x,to.z).normalized*Mathf.Clamp(to.magnitude*3,0,1):Vector2.zero;return;
            }
            if(mode=="PushDown"||mode=="RunDown"||mode=="PushLegs")
            {
                // Knock the target down and keep walking (RunDown: running) into it the whole time it is down, no punches:
                // PushDown/RunDown head for its chest, PushLegs comes in from the side across its legs. The lying body is
                // dragged a little (DownedPushShare), never passed through, and once its Down wait ends it nudges the
                // walker off and gets up.
                var rig=_pair.Players[1].Rig;var walker=_pair.Players[0];if(tick==5)rig.BeginKnockdown(new Vector3(0,0,1.2f));
                // PushLegs crosses once it stands beside the calves: switching at a fixed tick sent it past the feet before
                // it got there (2026-10-22), and only a body pushed from 11 cm away (the old separation margin) moved.
                var calves=(Bone(1,"Calf_L")+Bone(1,"Calf_R"))*.5f;
                var aim=mode=="PushLegs"?calves+(_stage==0?Vector3.right*.35f:Vector3.left*.6f):Bone(1,"Chest");
                var delta=aim-walker.Rig.Actor.Motor.Body.position;delta.y=0;
                if(mode=="PushLegs"&&_stage==0&&rig.RagdollActive&&delta.magnitude<.12f)_stage=1;
                if(tick==60)_pushStart=Bone(1,"Chest");
                if(rig.RagdollActive&&rig.Knockdown.Rising&&_risingTicks++==0)_pushDrift=Vector3.ProjectOnPlane(Bone(1,"Chest")-_pushStart,Vector3.up).magnitude;
                walker.Sprint=mode=="RunDown"&&rig.RagdollActive&&!rig.Knockdown.Rising;
                walker.Move=tick>30&&rig.RagdollActive&&delta.sqrMagnitude>.0001f?new Vector2(delta.x,delta.z).normalized:Vector2.zero;return;
            }
            if(mode=="PressRise")
            {
                // Knock the target down, keep walking into it (to .45 m from its chest) and punch left/right from t150 the
                // whole time it is down: it must still get up, both knees tucking as usual. A punching player's feet used
                // to dig in against the room shove, and the get-up waited 1.2 s, gave up and retried for good; now the shove
                // carries (NudgeFromRoom), and with only characters still in the way after the hold it rises through them.
                var rig=_pair.Players[1].Rig;var presser=_pair.Players[0];if(tick==5)rig.BeginKnockdown(new Vector3(0,0,1.2f));
                var delta=Bone(1,"Chest")-presser.Rig.Actor.Motor.Body.position;delta.y=0;
                presser.Move=tick>30&&rig.RagdollActive?new Vector2(delta.x,delta.z).normalized*Mathf.Clamp((delta.magnitude-.45f)*4,0,.6f):Vector2.zero;
                if(tick>150&&rig.RagdollActive){var age=tick-151;if(age%16==0)Buttons(0,1<<((age/16)%2));if(age%16==2)Buttons(0,0);}else Buttons(0,0);
                return;
            }
            if(mode=="PunchDowned")
            {
                // Knock the target out for a long Down wait, walk up to three spots around it (beside the chest, at the head
                // end, beside the hips on the other side) and punch left/right four times at each: the attacker bends down
                // to the lying body, the hits land and push it a little but count for nothing (2026-10-24).
                var rig=_pair.Players[1].Rig;var puncher=_pair.Players[0];
                if(tick==5){rig.Knockdown.Down.Bind(rig.Actor,new ProjectHotfix.Simulation.DownTimingProfile(10,.5,11),true);rig.BeginKnockdown(new Vector3(0,0,1.2f));}
                if(tick==LowSpotStart){_lowDownsAtStart=rig.Knockdowns;_lowPointsAtStart=_pair.PhysicalPunches.Score(1).Points;}
                var spot=tick<LowSpotStart?-1:(tick-LowSpotStart)/LowSpotTicks;var at=tick-LowSpotStart-spot*LowSpotTicks;
                if(spot<0||spot>2||!rig.RagdollActive){puncher.Move=Vector2.zero;Buttons(0,0);return;}
                var pelvis=Bone(1,"Pelvis");var chest=Bone(1,"Chest");var head=Bone(1,"Head");
                var along=Vector3.ProjectOnPlane(head-pelvis,Vector3.up).normalized;var across=Vector3.Cross(Vector3.up,along);
                var goal=spot==0?chest+across*.5f:spot==1?head+along*.42f:pelvis-across*.5f;
                var delta=goal-puncher.Rig.Actor.Motor.Body.position;delta.y=0;
                puncher.Move=at<110&&delta.magnitude>.04f?new Vector2(delta.x,delta.z).normalized*Mathf.Clamp(delta.magnitude*3,.3f,1):Vector2.zero;
                if(at>=110&&at<174){var age=at-110;if(age%16==0)Buttons(0,1<<((age/16)%2));if(age%16==2)Buttons(0,0);}else Buttons(0,0);
                if(at>=110)
                {
                    for(var s=0;s<2;s++)if(_pair.PhysicalPunches.IsPunching(0,s))_lowFistMin=Mathf.Min(_lowFistMin,puncher.Rig.HandPosition(s).y);
                    if(at<174){var k=spot*4+(at-110)/16;var s1=((at-110)/16)%2;var tip=puncher.Rig.HandPosition(s1);var gap=float.PositiveInfinity;var kb=new Rigidbody[32];var kn=rig.Knockdown.CopyDynamicBodies(kb);foreach(var body in kb.Take(kn))foreach(var col in body.GetComponents<Collider>())if(col.enabled&&!col.isTrigger)gap=Mathf.Min(gap,Vector3.Distance(col.ClosestPoint(tip),tip));if(at%16==0)_lowPunchGap[k]=float.PositiveInfinity;_lowPunchGap[k]=Mathf.Min(_lowPunchGap[k],gap);}
                    var c=puncher.Rig.Find("Chest");var up=c.Body.rotation*(Quaternion.Inverse(c.StartRotation)*Vector3.up);
                    _lowLeanMax=Mathf.Max(_lowLeanMax,Vector3.Angle(up,Vector3.up));
                }
                return;
            }
            if(mode=="PunchRise")
            {
                // Knock the target down and wait RisePunchDistance from its chest; while it gets up (its room shoves push the
                // puncher off, so it keeps stepping back in to RiseStepIn) punch it left/right: a hit lands and pushes it a little but counts for nothing (no points, no
                // knockdown) and it finishes standing. Fists pass over it while it is still low.
                var rig=_pair.Players[1].Rig;var puncher=_pair.Players[0];if(tick==5)rig.BeginKnockdown(new Vector3(0,0,1.2f));
                var rising=rig.RagdollActive&&rig.Knockdown.Rising;
                if(rising&&_risingTicks++==0){_risePoints=_pair.PhysicalPunches.Score(1).Points;_riseKickDowns=rig.Knockdowns;Debug.Log($"PunchRise rising from t{tick} (pair tick {_pair.Tick})");}
                var delta=Bone(1,"Chest")-puncher.Rig.Actor.Motor.Body.position;delta.y=0;
                puncher.Move=tick>30&&rig.RagdollActive?new Vector2(delta.x,delta.z).normalized*Mathf.Clamp((delta.magnitude-(rising?RiseStepIn:RisePunchDistance))*4,0,.8f):Vector2.zero;
                if(rising){var age=_risingTicks-1;var strikeSide=(age/16)%2;if(age%16==0)Buttons(0,1<<strikeSide);if(age%16==2)Buttons(0,0);}else Buttons(0,0);
                return;
            }
            if(mode=="DropkickRise")
            {
                // Knock the target down; once it starts getting up run (Shift) at it and dropkick it mid-rise: the kick
                // lands and pushes it a little but counts for nothing; the target keeps rising.
                var rig=_pair.Players[1].Rig;var runner=_pair.Players[0];if(tick==5)rig.BeginKnockdown(new Vector3(0,0,1.2f));
                if(rig.RagdollActive&&rig.Knockdown.Rising&&_risingTicks++==0)Debug.Log($"DropkickRise rising from t{tick}");
                // Walk up to ~2 m and wait; RiseKickDelay ticks into the rise, sprint in and kick from .7 m so the legs land on
                // the crouching body (from farther they fly over it).
                var delta=Bone(1,"Pelvis")-runner.Rig.Actor.Motor.Body.position;delta.y=0;var go=_risingTicks>RiseKickDelay;
                if(!go&&_kickTick<0){runner.Sprint=false;runner.Move=tick>30&&delta.magnitude>2?new Vector2(delta.x,delta.z).normalized*.6f:Vector2.zero;return;}
                if(_kickTick<0&&go&&delta.magnitude<.7f){_kickTick=tick;Buttons(0,1);_riseKickDowns=rig.Knockdowns;}
                if(_kickTick>=0&&tick==_kickTick+2)Buttons(0,0);
                if(_kickTick>=0&&(tick-_kickTick)%10==0&&tick-_kickTick<=40)Debug.Log($"DropkickRise t{tick} kick+{tick-_kickTick} target rising {rig.GettingUp} downs {rig.Knockdowns} leg contacts {runner.DropkickHits}");
                runner.Sprint=_kickTick<0||tick<=_kickTick+2;runner.Move=runner.Sprint?new Vector2(delta.x,delta.z).normalized:Vector2.zero;return;
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
                _stage=0;_releaseTick=-1;_gripTick=-1;_throwTick=-1;_throwCount=0;_throwDistance=-1;_heldParts.Clear();_riseGrabPressed=_grabbedWhileRising=_risingKickIgnored=false;_risingTicks=0;_riseKickDowns=-1;Array.Clear(_lowSpotHits,0,3);Array.Clear(_lowPunchGap,0,12);_lowFistMin=float.PositiveInfinity;_lowLeanMax=0;_lowDownsAtStart=_lowPointsAtStart=-1;_risePoints=-1;_pushDrift=-1;_pushDepth=0;_pushInside=0;_skinDeepest=0;_skinInside=0;_skinWhere="";_downTick=-1;_kickTick=-1;_koRestHeight=-1;_upTick=-1;_protectHeld=_protectExpired=false;_zoom=.8f;_climbTick=-1;_topTick=-1;
                Create(mode=="GrabStanding"?.5f:mode=="Solo"||mode=="WallClimb"||mode=="WallClimbHigh"?3f:mode=="Gait"||mode=="DropkickMiss"||mode=="Jump"?9f:mode=="Dropkick"||mode=="DropkickRise"||mode=="Protect"?3f:mode=="PunchRise"||mode=="PunchDowned"||mode=="PressRise"||mode=="PushDown"||mode=="PushLegs"?1.6f:mode=="RunDown"?3.2f:mode=="StandOver"?1.6f:mode=="KOVariety"||mode=="KORandom"||mode=="GetUp"?3f:mode=="Crowd"?2f:mode=="Far"?FarSeparation:mode=="GrabPunch"?.5f:(mode=="Head"?.42f:.38f)+Spacing,mode=="Head"?.22f:0,mode=="Wall");
                // Ledge LedgeHeight high, its face .8 m ahead of the climber (roots at z -1.5 and 1.5).
                if(mode=="WallClimbHigh"){Block("High wall",new Vector3(0,HighWall*.5f,.4f),new Vector3(3f,HighWall,2.2f));UnityEngine.Physics.SyncTransforms();}
                if(mode=="WallClimb"){Block("Ledge",new Vector3(0,LedgeHeight*.5f,.4f),new Vector3(3f,LedgeHeight,2.2f));UnityEngine.Physics.SyncTransforms();}
                var folder=Path.Combine(Output,"Frames",mode);Directory.CreateDirectory(folder);var rt=new RenderTexture(800,600,24);rt.Create();var image=new Texture2D(800,600,TextureFormat.RGB24,false);_pair.View.targetTexture=rt;_pair.View.aspect=4f/3;_pair.View.orthographicSize=.80f;
                var renderers=_pair.Players.Select(p=>p.Rig.Actor.Pose.GetComponentsInChildren<SkinnedMeshRenderer>().Single(s=>s.name.EndsWith("SkinnedBody"))).ToArray();_crowdRenderers=renderers;var mesh=new Mesh();var tri=renderers[0].sharedMesh.triangles;var pairs=CameraGaitReviewTests.Pairs(tri);var ticks=mode=="Knockout"||mode=="GrabDown"||mode=="GrabHoldLong"||mode=="GrabRise"||mode=="GrabHead"||mode=="GrabLegs"||mode=="GrabArm"?720:mode=="GrabStanding"?420:mode=="WallClimb"?540:mode=="WallClimbHigh"?1200:mode=="Gait"?300:mode=="Jump"?360:mode=="DropkickRise"?600:mode=="PunchDowned"?LowSpotStart+3*LowSpotTicks:mode=="PunchRise"||mode=="PressRise"||mode=="PushDown"||mode=="RunDown"||mode=="PushLegs"?420:mode.StartsWith("Dropkick")?420:mode=="Protect"?360:mode=="StandOver"?480:mode=="KOVariety"?KOCycle*6:mode=="KORandom"?KOCycle*8:mode=="GetUp"?GetUpCycle*GetUpCases:mode=="Crowd"?CrowdCycle*CrowdCases:180;var clip=new Clip{name=mode,frames=new Frame[ticks]};
                for(var tick=0;tick<ticks;tick++)
                {
                    if(Environment.GetEnvironmentVariable("PARTY_PUNCH_DEBUG")=="1"&&_pair.PhysicalPunches!=null&&_pair.PhysicalPunches.ContactLog==null)_pair.PhysicalPunches.ContactLog=new List<string>{$"start tick {_pair.Tick} = capture tick {tick}"};CaptureInput(mode,tick);Tick();TrackThrow(tick);var angle=0f;for(var actor=0;actor<renderers.Length;actor++){var skin=renderers[actor];skin.BakeMesh(mesh);if(mode=="Crowd"&&actor==1)CrowdSkinInside(skin,mesh,tick);if(mode=="PushDown"||mode=="RunDown"||mode=="PushLegs"||mode=="PressRise"||mode=="StandOver")PushSkinInside(actor,skin,mesh);var measured=CameraGaitReviewTests.Surface(mesh.vertices,tri,pairs,out var point);angle=Mathf.Max(angle,measured);if(tick==0&&actor==0)ExportSurfacePose(skin,mode+"_Tick0");if(actor==0&&Environment.GetEnvironmentVariable("PARTY_EXPORT_ALL_TICKS")=="1")ExportSurfacePose(skin,$"{mode}_T{tick:D3}");if(measured>clip.maxSkin){clip.maxSkin=measured;clip.worstPoint=point;clip.worstActor=actor;clip.worstTick=tick;ExportSurfacePose(skin,mode+"_Worst");}}
                    if((mode=="PushDown"||mode=="RunDown"||mode=="PushLegs"||mode=="PressRise"||mode=="StandOver")&&_pair.Players[1].Rig.RagdollActive&&tick%2==0)
                    {var (_,inside,deepest,where)=SkinGapNow(renderers);_skinInside=Mathf.Max(_skinInside,inside);if(deepest>_skinDeepest){_skinDeepest=deepest;_skinWhere=$"t{tick} {where}";}}
                    var punch=_pair.PhysicalPunches;clip.maxSkin=Mathf.Max(clip.maxSkin,angle);clip.frames[tick]=new Frame{tick=tick,punches=punch.State(0).PunchCount,hits=punch.Hits.Count,points=punch.Score(1).Points,grips=_pair.LogicalGrips,throws=_pair.Throws,cues=punch.ConfirmedCues,sourceAge=(float)punch.State(0).SourcePunchAge(mode=="RightBody"||mode=="GrabPunch"?1:0),skin=angle,tip=_pair.Players[0].Rig.HandPosition(mode=="RightBody"||mode=="GrabPunch"?1:0),root=_pair.Players[0].Rig.Actor.Motor.Body.position,targetRoot=_pair.Players[1].Rig.Actor.Motor.Body.position,targetDown=_pair.Players[1].Rig.KnockedDown,knockdowns=_pair.Players[1].Rig.Knockdowns,climbs=_pair.Players[0].Climbs,boneHandL=Bone(0,"HandLogical_L"),boneHandR=Bone(0,"HandLogical_R"),boneForearmL=Bone(0,"Forearm_L"),boneForearmR=Bone(0,"Forearm_R")};
                    _pair.Present(1);var center=(mode=="KOVariety"||mode=="KORandom"||mode=="GetUp"?_pair.Players[1].Rig.Actor.Motor.Body.position:mode=="Solo"||mode=="WallClimb"||mode=="WallClimbHigh"||mode=="Gait"||mode=="DropkickMiss"||mode=="Jump"?_pair.Players[0].Rig.Actor.Motor.Body.position:(_pair.Players[0].Rig.Actor.Motor.Body.position+_pair.Players[1].Rig.Actor.Motor.Body.position)*.5f)+Vector3.up*.52f;var offset=mode=="WallClimbHigh"?new Vector3(-2.6f,.6f,-1.9f):mode=="Gait"||mode=="GetUp"||mode=="Jump"||mode.StartsWith("Dropkick")||mode=="PunchRise"||mode=="PunchDowned"||mode=="PressRise"||mode=="PushDown"||mode=="RunDown"||mode=="PushLegs"?new Vector3(2.6f,.6f,0):mode=="WallClimb"?new Vector3(-2.2f,.8f,-1.7f):new Vector3(mode=="RightBody"||mode=="GrabPunch"?2.3f:-2.3f,1.05f,1.25f);if(mode=="WallClimb")_pair.View.orthographicSize=1.05f;if(mode.StartsWith("Dropkick")){_pair.View.orthographicSize=1.1f;center-=Vector3.up*.15f;}if(mode=="DropkickRise"||mode=="PunchRise"||mode=="PunchDowned"||mode=="PressRise"||mode=="PushDown"||mode=="RunDown"||mode=="PushLegs"){center=(_pair.Players[0].Rig.Actor.Motor.Body.position+Bone(1,"Pelvis"))*.5f;center.y=.45f;_pair.View.orthographicSize=1.2f;}if(mode=="WallClimbHigh"){_pair.View.orthographicSize=1.35f;center+=Vector3.up*.3f;}if(mode=="PunchDowned"){var a0=_pair.Players[0].Rig.Actor.Motor.Body;var fw=Vector3.ProjectOnPlane(a0.rotation*Vector3.forward,Vector3.up).normalized;center=a0.position+fw*.35f;center.y=.4f;offset=Vector3.Cross(Vector3.up,fw)*-2.6f+Vector3.up*.5f;_pair.View.orthographicSize=.85f;}if(mode=="KOVariety"||mode=="KORandom"){center-=Vector3.up*.35f;_pair.View.orthographicSize=.75f;}if(mode=="Crowd"){center=Bone(1,"Pelvis");center.y=.45f;offset=new Vector3(1.4f,1.7f,2.2f);_pair.View.orthographicSize=.85f;}if(mode=="GetUp"){center=(Bone(1,"Head")+Bone(1,"Foot_L")+Bone(1,"Foot_R"))/3;center.y=.42f;_pair.View.orthographicSize=.62f;if(Environment.GetEnvironmentVariable("PARTY_GETUP_VIEW")=="quarter"){offset=new Vector3(1.9f,1.5f,-1.4f);_pair.View.orthographicSize=.7f;}}if(mode.StartsWith("Grab")&&mode!="GrabPunch"){var span=Vector3.Distance(_pair.Players[0].Rig.Actor.Motor.Body.position,_pair.Players[1].Rig.Actor.Motor.Body.position);_zoom=Mathf.Max(_zoom,Mathf.Clamp(.45f+span*.45f,.8f,2.6f));_pair.View.orthographicSize=_zoom;}_pair.View.transform.SetPositionAndRotation(center+offset,Quaternion.LookRotation(-offset));
                    yield return null;_pair.View.Render();var old=RenderTexture.active;RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,800,600),0,0);image.Apply();RenderTexture.active=old;File.WriteAllBytes(Path.Combine(folder,$"Frame_{tick:D4}.png"),image.EncodeToPNG());
                }
                clip.hits=_pair.PhysicalPunches.Hits.ToArray();results.Add(clip);if(_pair.PhysicalPunches.ContactLog!=null)File.WriteAllLines(Path.Combine(Output,mode+"_Contacts.tsv"),_pair.PhysicalPunches.ContactLog);File.WriteAllText(Path.Combine(Output,"Capture.json"),JsonUtility.ToJson(new Report{clips=results.ToArray()},true));
                if(clip.maxSkin>=60)failures.Add(mode+" skin "+clip.maxSkin);
                if((mode=="LeftBody"||mode=="RightBody"||mode=="Head")&&clip.hits.Length==0)failures.Add(mode+" no hit");
                if((mode=="Far"||mode=="Wall")&&clip.hits.Length!=0)failures.Add(mode+" false hit");
                // A standing opponent is only held: releasing the grab never throws it (2026-10-17).
                if(mode=="GrabPunch"&&(_pair.PhysicalPunches.State(0).PunchCount!=1||_pair.Throws!=0))failures.Add(mode+" action arbitration");
                if(mode=="GrabStanding"&&(_pair.Throws!=0||clip.frames.Any(f=>f.targetDown)))failures.Add(mode+" standing target thrown");
                // Held past the Down time: the body struggles free (no throw), is not grabbed again, and stands up.
                if(mode=="GrabHoldLong")
                {
                    var freed=Array.FindIndex(clip.frames,f=>f.tick>0&&f.grips==0&&clip.frames[f.tick-1].grips>0);
                    if(_pair.Escapes!=1||_pair.Throws!=0)failures.Add(mode+$" expected one escape and no throw (escapes {_pair.Escapes}, throws {_pair.Throws})");
                    else if(freed<0||clip.frames.Skip(freed).Any(f=>f.grips>0))failures.Add(mode+" re-grabbed after the escape");
                    if(clip.frames[ticks-1].targetDown)failures.Add(mode+" escaped body did not get up");
                }
                if(mode=="GrabDown"&&_pair.Throws!=1)failures.Add(mode+" downed target not thrown");
                else if(mode=="GrabDown"&&_throwDistance<.5f)failures.Add(mode+$" throw carried only {_throwDistance:F2} m");
                if(mode.StartsWith("Grab")&&_throwTick>=0)Debug.Log($"{mode}: held {string.Join("+",_heldParts.OrderBy(n=>n))} (first grip t{_gripTick}), thrown t{_throwTick}, chest landed {_throwDistance:F2} m from the release");
                if(mode=="GrabHead"||mode=="GrabLegs"||mode=="GrabArm")
                {
                    if(_heldParts.Count==0)failures.Add(mode+" never held the downed body");
                    else if(_pair.Throws!=1)failures.Add(mode+$" held {string.Join("+",_heldParts)} but not thrown (throws {_pair.Throws})");
                    else if(_throwDistance<.5f)failures.Add(mode+$" held {string.Join("+",_heldParts)}: throw carried only {_throwDistance:F2} m");
                    if(clip.frames[ticks-1].targetDown)failures.Add(mode+" thrown body did not get up");
                }
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
                // Every crowded get-up ends standing.
                if(mode=="Crowd")for(var c=0;c<CrowdCases;c++)if(clip.frames[(c+1)*CrowdCycle-1].targetDown)failures.Add(mode+$" case {c} still down at the end of its cycle");
                if(mode=="Crowd"){Debug.Log("CROWD max skin vertices inside per case: "+string.Join(",",_crowdInside));if(_crowdInside.Any(n=>n>0))failures.Add(mode+" get-up sank into a neighbour or prop: "+string.Join(",",_crowdInside));}
                if(mode=="Crowd"){foreach(var prop in new[]{_crowdProp,_crowdProp2})if(prop!=null)UnityEngine.Object.Destroy(prop);_crowdProp=_crowdProp2=null;}
                // Since the skin-fitted legs (2026-10-22) the walker is held clear of the get-up's room, so a shove is no
                // longer required here (PressRise and Crowd keep characters in the room); it must still stand up.
                if(mode=="StandOver")Debug.Log($"StandOver: room pushes {_pair.Players[1].Rig.Knockdown.RoomPushes}");
                if(mode=="StandOver"&&clip.frames[ticks-1].targetDown)failures.Add(mode+$" downed body under a standing player (pushes {_pair.Players[1].Rig.Knockdown.RoomPushes}, down at end {clip.frames[ticks-1].targetDown})");
                if(mode=="Jump"&&_pair.Players[0].Jumps!=4)failures.Add(mode+" expected 4 jumps, got "+_pair.Players[0].Jumps);
                if(mode=="Protect"&&(_upTick<0||!_protectHeld||!_protectExpired||!_risingKickIgnored))failures.Add(mode+$" get-up protection (stood t{_upTick}, held {_protectHeld}, expired {_protectExpired}, rising kick ignored {_risingKickIgnored})");
                if(mode=="GrabRise"){Debug.Log($"GrabRise: rising ticks {_risingTicks}, palm touches {_pair.Players[0].PalmTouches}, acquisitions {_pair.Players[0].Acquisitions}");if(_risingTicks==0||_grabbedWhileRising||clip.frames[ticks-1].targetDown)failures.Add(mode+$" rising body grabbed or not up (rising ticks {_risingTicks}, grabbed {_grabbedWhileRising}, down at end {clip.frames[ticks-1].targetDown})");}
                if(mode=="PushDown"||mode=="RunDown"||mode=="PushLegs"||mode=="PressRise"||mode=="StandOver")
                {
                    // Judged skin against skin (SkinGapNow). Skin against the other's colliders is only logged: the standing
                    // root capsule is ~5 cm wider than the legs it covers (2026-10-22), so a lying body skin inside it is not
                    // inside the legs.
                    Debug.Log($"{mode}: downed chest dragged {_pushDrift:F2} m before getting up, push ticks {_pair.DownedPushTicks}, skin in skin {_skinInside} vertices over 1 cm (deepest {_skinDeepest:F3} m, {_skinWhere}); skin in colliders {_pushInside} (deepest {_pushDepth:F3} m)");
                    if(_skinDeepest>PushSkinTolerance)failures.Add(mode+$" skins passed through each other ({_skinInside} vertices over 1 cm, deepest {_skinDeepest:F3} m, {_skinWhere})");
                    if(mode!="PressRise"&&mode!="StandOver"&&(clip.frames[ticks-1].targetDown||_pushDrift<.03f||_pushDrift>1f))failures.Add(mode+$" walked-into body: dragged {_pushDrift:F2} m, down at end {clip.frames[ticks-1].targetDown}");
                }
                if(mode=="PressRise"&&clip.frames[ticks-1].targetDown)failures.Add(mode+" a body pressed and punched never got up");
                // A body that still lies there never shoves anyone off; that starts with the get-up (2026-10-22).
                foreach(var player in _pair.Players)if(player.Rig.Knockdown!=null&&player.Rig.Knockdown.LyingNudges>0)failures.Add(mode+$" {player.Rig.name} shoved a standing player {player.Rig.Knockdown.LyingNudges} ticks while still lying");
                if(mode=="PunchRise"){var rising=clip.hits.Where(h=>!h.counted).ToArray();Debug.Log($"PunchRise: uncounted hits {rising.Length}, counted {clip.hits.Count(h=>h.counted)}, points {_risePoints}->{_pair.PhysicalPunches.Score(1).Points}, downs {_riseKickDowns}->{_pair.Players[1].Rig.Knockdowns}, punches {clip.frames[ticks-1].punches}, contacts {_pair.PhysicalPunches.PhysicalContactsInWindow}, outside tip {_pair.PhysicalPunches.RejectedOutsideTip}, slow {_pair.PhysicalPunches.RejectedMotion}");
                    if(rising.Length==0||rising.Any(h=>h.points!=0)||_pair.PhysicalPunches.Score(1).Points!=_risePoints||_pair.Players[1].Rig.Knockdowns!=_riseKickDowns||clip.frames[ticks-1].targetDown)failures.Add(mode+$" rising body hit counted or not standing (uncounted hits {rising.Length}, points {_risePoints}->{_pair.PhysicalPunches.Score(1).Points})");}
                if(mode=="PunchDowned")
                {
                    var hits=clip.hits.Where(h=>h.attacker==0&&h.region!="Dropkick").ToArray();
                    for(var s=0;s<3;s++){var from=LowSpotStart+s*LowSpotTicks;_lowSpotHits[s]=clip.frames[from+LowSpotTicks-1].hits-clip.frames[from-1].hits;}
                    Debug.Log($"PunchDowned: hits per spot {string.Join(",",_lowSpotHits)} (counted {hits.Count(h=>h.counted)}), punches {clip.frames[ticks-1].punches}, points {_lowPointsAtStart}->{_pair.PhysicalPunches.Score(1).Points}, downs {_lowDownsAtStart}->{_pair.Players[1].Rig.Knockdowns}, lowest fist {_lowFistMin:F3} m, deepest trunk bend {_lowLeanMax:F0} deg, contacts {_pair.PhysicalPunches.PhysicalContactsInWindow}, outside tip {_pair.PhysicalPunches.RejectedOutsideTip}, slow {_pair.PhysicalPunches.RejectedMotion}");
                    Debug.Log("PunchDowned closest fist-to-body per punch (cm): "+string.Join(",",_lowPunchGap.Select(g=>(g*100).ToString("F1"))));
                    if(_lowSpotHits.Any(n=>n==0))failures.Add(mode+$" a spot landed no hit on the lying body ({string.Join(",",_lowSpotHits)})");
                    if(hits.Any(h=>h.counted||h.points!=0)||_pair.PhysicalPunches.Score(1).Points!=_lowPointsAtStart||_pair.Players[1].Rig.Knockdowns!=_lowDownsAtStart)failures.Add(mode+" a hit on the lying body counted");
                    if(_pair.Players[0].Rig.RagdollActive)failures.Add(mode+" the attacker fell over");
                }
                if(mode=="DropkickRise"&&!clip.hits.Any(h=>h.region=="Dropkick"&&!h.counted))failures.Add(mode+" rising dropkick did not land as an uncounted hit");
                if(mode=="DropkickRise"&&(_kickTick<0||_pair.Players[1].Rig.Knockdowns!=_riseKickDowns||clip.frames[ticks-1].targetDown))failures.Add(mode+$" rising body was dropkicked down (kick t{_kickTick}, downs {_riseKickDowns}->{_pair.Players[1].Rig.Knockdowns}, down at end {clip.frames[ticks-1].targetDown})");
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
