using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ProjectHotfix.PhysicsJump
{
    // A standing player against the other one lying down (2026-10-21 review: "a little pushed, never passed through").
    public sealed partial class ActiveGrabPairReview
    {
        // Walking or running into a downed body that is still lying out its Down wait drags it along a little: every part
        // whose skin the walker's skin touches (SeparateFromDowned, this tick) ahead of it is brought up to DownedPushShare
        // of the walk it wants (never added up), and the walker is slowed to DownedPushDrag while it pushes. Once the body
        // gets up, its own room nudge takes over (2026-10-21 review: the 20 kg ragdoll barely moved and the walker just
        // stopped against its legs).
        public float DownedPushShare=.25f,DownedPushDrag=.6f,DownedPushFollow=.7f,DownedPushMax=.35f;
        public int DownedPushTicks{get;private set;}
        private readonly List<Rigidbody> _pushedParts=new List<Rigidbody>();
        private bool PushDowned(int index,Vector3 desired)
        {
            var player=Players[index];var rig=player.Rig;var other=Players[1-index];
            // Not while reaching for or holding it (that is a grab, not a shove) or while it flies in a dropkick.
            if(desired.sqrMagnitude<.0001f||rig.RagdollActive||!Active(other)||!other.Rig.RagdollActive||other.Rig.Knockdown.Rising||other.Rig.Dropkicking||player.Hands.Any(h=>h.Phase!=HandPhase.Idle))return false;
            var dir=desired.normalized;var goal=Mathf.Min(DownedPushMax,desired.magnitude*DownedPushShare);var from=rig.Actor.Motor.Body.position;
            _pushedParts.Clear();
            foreach(var part in _skinTouched[index])
            {
                if(part==null||part.isKinematic||Vector3.Dot(Vector3.ProjectOnPlane(part.worldCenterOfMass-from,Vector3.up),dir)<=0)continue;
                var along=Vector3.Dot(part.linearVelocity,dir);if(along<goal)part.AddForce(dir*(goal-along),ForceMode.VelocityChange);
                _pushedParts.Add(part);
            }
            // The rest of the body follows at DownedPushFollow of that, so it slides along rather than only the touched limb
            // folding (the touched parts are left out: their velocity reads back unchanged until the step, so they got both).
            var pushed=_pushedParts.Count>0;
            if(pushed)
            {
                var count=other.Rig.Knockdown.CopyDynamicBodies(_knockdownBodies);
                for(var i=0;i<count;i++){var part=_knockdownBodies[i];if(_pushedParts.Contains(part))continue;var along=Vector3.Dot(part.linearVelocity,dir);if(along<goal*DownedPushFollow)part.AddForce(dir*(goal*DownedPushFollow-along),ForceMode.VelocityChange);}
                DownedPushTicks++;
            }
            return pushed;
        }

        // A punch at a body lying knocked out in front (within LowStrikeRange of the root, inside LowStrikeCone of the
        // facing) aims at the nearest point of it, the trunk first (TryKnockdownSurface): the attacker bends down to it
        // and the hit lands uncounted (PhysicalPunchReview.UncountedTarget). Not at a body getting up: it rises out of the
        // bend's way, and its hits land at standing height. 2026-10-24 review: hitting the lying body is play, no extra input.
        public float LowStrikeRange=.8f,LowStrikeCone=.5f;
        private bool AimLowStrike(int index)
        {
            var player=Players[index];var rig=player.Rig;var other=Players[1-index];var any=false;
            var lying=PhysicalPunches!=null&&!rig.RagdollActive&&Active(other)&&other.Rig.KnockedDown&&!other.Rig.Knockdown.Rising;
            var root=rig.Actor.Motor.Body;var forward=Vector3.ProjectOnPlane(root.rotation*Vector3.forward,Vector3.up).normalized;
            for(var side=0;side<2;side++)
            {
                var aim=false;var point=default(Vector3);
                if(lying&&PhysicalPunches.IsPunching(index,side)&&TryKnockdownSurface(other.Rig,rig.Find("UpperArm_"+(side==0?"L":"R")).Body.position,out point))
                {
                    var to=Vector3.ProjectOnPlane(point-root.position,Vector3.up);
                    aim=to.magnitude<=LowStrikeRange&&Vector3.Dot(to.normalized,forward)>=LowStrikeCone;
                }
                rig.SetLowStrike(side,aim,point);any|=aim;
            }
            return any;
        }

        // Keeps a standing player's skin off a downed body's skin (2026-10-22 review: walking into a knocked-out body
        // felt like pushing a wall between the two). Both skins are sampled (SkinContactCloud) and every walker skin
        // point closer than DownedSkinGap over the downed skin moves its body off along the skin normal at
        // DownedSeparationGain per metre: the hips and legs through the root (sideways only, HoldOff: no shove, its
        // walk drive untouched), arms, chest and head through their link. Points within DownedPushContact beyond that are the touch PushDowned
        // shoves the body by, so the walker pushes only what it really touches, the same from any side and in any pose.
        // Before, the walker's capsules were kept 5 cm off the downed proxies: skin sticks out past capsules by a
        // different amount per part, so a 6-8 cm gap showed and it changed with the side pushed from.
        // While the body gets up the gap blends up to DownedRisingMargin over DownedRisingBlend: the hips swing toward
        // the feet fast as the weight comes over them.
        // A player reaching for, holding or carrying the body is left alone (it has to get in close), so is a punching
        // arm, and a body flying in a dropkick is not a downed body. Not for DownedReleaseGrace after the hands let go
        // either: a body just thrown or dropped is still on or above the carrier (2026-10-22: the release pushed the
        // thrower ~6 m and the body dropped where it was).
        public float DownedSkinGap=.008f,DownedPushContact=.015f,DownedRisingMargin=.15f,DownedRisingBlend=.25f,DownedSeparationGain=45f,DownedSeparationMax=1.5f,DownedLinkGain=45f,DownedLinkMax=.6f,DownedSlideShare=.3f,DownedYield=.5f,DownedStepYield=1f,DownedReleaseGrace=.4f;
        public int DownedSeparations{get;private set;}
        // While its skin touches the downed body, the walker's drive toward it is capped at DownedPressAcceleration
        // (m/s²; the walk itself has Acceleration, 20): at full drive the feet kept slamming the stopped root into the body every tick and
        // the trunk lagged behind each jolt, leaning ~15 deg back (2026-10-23 review: the pushing player looked folded).
        // Walking off or along the body is not capped.
        public float DownedPressAcceleration=8f;
        private readonly Vector3[] _pressInto=new Vector3[2];
        private Vector3 EasePressIntoDowned(int index,Vector3 force)
        {
            var into=_pressInto[index];if(into==Vector3.zero)return force;
            var limit=Players[index].Rig.TotalMass*DownedPressAcceleration;var ahead=Vector3.Dot(force,into);
            return ahead>limit?force-into*(ahead-limit):force;
        }
        private readonly SkinContactCloud[] _skinClouds=new SkinContactCloud[2];
        private readonly float[] _risingFor=new float[2];
        private readonly List<Rigidbody>[] _skinTouched={new List<Rigidbody>(),new List<Rigidbody>()};
        private readonly Dictionary<Rigidbody,(Vector3 push,float deepest)> _skinPush=new Dictionary<Rigidbody,(Vector3,float)>();
        private readonly Dictionary<Rigidbody,(Vector3 push,float deepest)> _skinYield=new Dictionary<Rigidbody,(Vector3,float)>();
        private SkinContactCloud SkinCloud(int index)=>_skinClouds[index]??=new SkinContactCloud(Players[index].Rig.Actor);
        private void SeparateFromDowned(int index)
        {
            var player=Players[index];var rig=player.Rig;var other=Players[1-index];var downedRig=other.Rig;_skinTouched[index].Clear();_pressInto[index]=Vector3.zero;
            _risingFor[index]=Active(other)&&downedRig.RagdollActive&&downedRig.Knockdown.Rising?_risingFor[index]+_stepSeconds:0;
            var active=Active(other)&&downedRig.RagdollActive&&!downedRig.Dropkicking&&!player.Hands.Any(h=>h.Phase!=HandPhase.Idle)&&player.HandsIdleSeconds>=DownedReleaseGrace;
            IgnoreDownedWithRoot(index,active&&!downedRig.Knockdown.Rising);
            if(!active)return;
            if(Vector3.ProjectOnPlane(downedRig.Actor.Motor.Body.position-rig.Actor.Motor.Body.position,Vector3.up).sqrMagnitude>2.5f*2.5f)return;
            var gap=downedRig.Knockdown.Rising?Mathf.Lerp(DownedSkinGap,DownedRisingMargin,_risingFor[index]/DownedRisingBlend):DownedSkinGap;
            var walker=SkinCloud(index);var downed=SkinCloud(1-index);walker.Update(Tick);downed.Update(Tick);
            var reach=downed.Bounds;reach.Expand(2*(gap+DownedPushContact));
            if(!reach.Intersects(walker.Bounds))return;
            downed.BuildGrid();_skinPush.Clear();_skinYield.Clear();
            var touched=_skinTouched[index];var root=rig.Actor.Motor.Body;
            for(var i=0;i<walker.Count;i++)
            {
                var point=walker.Points[i];
                if(!reach.Contains(point)||!downed.Nearest(point,out var near,out var height,out var normal))continue;
                var own=rig.BodyForBone(walker.Bones[walker.Bone[i]]);
                // Where it will be after this step if both skins keep moving as they did over the last one (a walk
                // covers ~2 cm a tick, and a swinging foot more): reacting to the last pose alone let the skins sink
                // in by a step at every first touch.
                height+=Mathf.Min(0,Vector3.Dot(walker.Motion[i]-downed.Motion[near],normal));
                if(height>=gap+DownedPushContact)continue;
                var part=downedRig.Knockdown.BodyForBone(downed.Bones[downed.Bone[near]]);if(!touched.Contains(part))touched.Add(part);
                _pressInto[index]+=Vector3.ProjectOnPlane(downed.Points[near]-root.position,Vector3.up);
                if(height>=gap)continue;
                if(own!=root&&PhysicalPunches!=null)
                {
                    var link=rig.Links.First(l=>l.Body==own);var side=link.Name.EndsWith("_L")?0:link.Name.EndsWith("_R")?1:-1;
                    if(side>=0&&(link.Name.StartsWith("Forearm")||link.Name.StartsWith("UpperArm"))&&PhysicalPunches.IsPunching(index,side))continue;
                }
                var depth=gap-height;_skinPush.TryGetValue(own,out var sum);_skinPush[own]=(sum.push+normal*depth,Mathf.Max(sum.deepest,depth));
                // The touched part gives way too, out from under the walker (DownedYield), fully when stepped on (a leg
                // or hip point, DownedStepYield): a foot set down by the walk on a hand or arm lying flat meets a skin
                // normal pointing up, so holding the walker off does nothing, and a stopped walker's stride still swings
                // the foot on by most of a step.
                if(part!=null&&!part.isKinematic)
                {
                    var outward=Vector3.ProjectOnPlane(downed.Points[near]-root.position,Vector3.up);var share=own==root?DownedStepYield:DownedYield;
                    if(outward.sqrMagnitude>1e-6f){_skinYield.TryGetValue(part,out var y);_skinYield[part]=(y.push+outward.normalized*depth*share,Mathf.Max(y.deepest,depth*share));}
                }
            }
            _pressInto[index]=_pressInto[index].sqrMagnitude>1e-8f?_pressInto[index].normalized:Vector3.zero;
            // Once per part, like the walker's own bodies: a velocity change per contact point added up (velocities
            // read back are last step's), and a limb with dozens of touching points was flung and the ragdoll blew apart.
            foreach(var entry in _skinYield)
            {
                var part=entry.Key;var (push,deepest)=entry.Value;var direction=push.normalized;
                var yieldSpeed=Mathf.Min(DownedSeparationMax,deepest*DownedSeparationGain);var along=Vector3.Dot(part.linearVelocity,direction);
                if(along<yieldSpeed)part.AddForce(direction*(yieldSpeed-along),ForceMode.VelocityChange);
            }
            // One push per own body, toward its contacts' combined away direction at the deepest one's speed: a velocity
            // change per contact added up would fling it (the velocity read is last step's).
            // A hand or arm against the body stops the whole player too (its pose drive pulled a pushed-off arm straight
            // back in: hanging hands reach ~.2 m, a lying back's height). The root keeps only DownedSlideShare of the
            // push across the way it walks: along the skin normal a walker coming at rounded legs or a shoulder was
            // steered round the body and slid past it instead of pushing it.
            var travel=new Vector3(player.Move.x,0,player.Move.y);travel=travel.sqrMagnitude>.0001f?travel.normalized:Vector3.zero;
            var rootPush=Vector3.zero;var rootSpeed=0f;
            foreach(var entry in _skinPush)
            {
                var own=entry.Key;var (push,deepest)=entry.Value;var speed=Mathf.Min(DownedSeparationMax,deepest*DownedSeparationGain);DownedSeparations++;
                var away=Vector3.ProjectOnPlane(push,Vector3.up);if(away.sqrMagnitude>1e-8f){rootPush+=away.normalized*speed;rootSpeed=Mathf.Max(rootSpeed,speed*away.magnitude/push.magnitude);}
                if(own==root)continue;
                // An arm is only eased off (DownedLinkMax): pushed off within a step, hands that brushed a lying body
                // flung both arms out wide.
                var direction=push.normalized;var along=Vector3.Dot(own.linearVelocity,direction);var linkSpeed=Mathf.Min(DownedLinkMax,deepest*DownedLinkGain);
                if(along<linkSpeed)own.AddForce(direction*(linkSpeed-along),ForceMode.VelocityChange);
            }
            if(rootSpeed>0)
            {
                if(travel!=Vector3.zero){var back=travel*Vector3.Dot(rootPush,travel);rootPush=back+(rootPush-back)*DownedSlideShare;}
                if(rootPush.sqrMagnitude>1e-8f)rig.HoldOff(rootPush,rootSpeed*Mathf.Clamp01(rootPush.magnitude/Mathf.Max(rootSpeed,1e-4f)));
            }
        }

        // The standing root capsule (radius .13 over the hips and legs, up to .34 m) pokes ~5 cm out past the leg skin in
        // front and behind (measured 2026-10-22), and all of a lying body is below .34 m, so it was a wall 5 cm before
        // the skins touched. While SeparateFromDowned keeps the skins apart, that capsule ignores the downed proxies and
        // a leg core (a box inside the standing legs' skin, clear of the floor) collides with them instead: still a
        // physical stop, so the walker cannot sink deep into the body or climb over it (with nothing there, the arm
        // and chest links ended up deep in the proxies and the solver threw the body ~60 m). The core ignores
        // everything else (own body, the other player's standing links), so walking, punches and standing contacts
        // are unchanged. All of it is undone as soon as the separation stops (hands busy, a dropkick, getting up done),
        // and only pairs set here are restored.
        public Vector3 LegCoreSize=new Vector3(.18f,.27f,.10f);public float LegCoreBottom=.06f;
        private readonly List<(Collider a,Collider b)>[] _rootIgnores={new List<(Collider,Collider)>(),new List<(Collider,Collider)>()};
        private readonly BoxCollider[] _legCores=new BoxCollider[2];
        private readonly (Vector3 center,Vector3 inertia,Quaternion rotation,bool autoCenter,bool autoInertia)[] _legCoreMass=new (Vector3,Vector3,Quaternion,bool,bool)[2];
        // Only while the body lies: a body getting up turns its standing capsule on, which met the walker's sunk-in legs
        // and dropped the rise (2026-10-22, a body that struggled free beside its grabber). Undone pair by pair once the
        // two no longer overlap (undoing it deep inside threw them apart); all at once when this player itself goes down.
        private void IgnoreDownedWithRoot(int index,bool ignore,bool now=false)
        {
            var list=_rootIgnores[index];
            if(!ignore)
            {
                for(var i=list.Count-1;i>=0;i--)
                {
                    var (a,b)=list[i];
                    if(a!=null&&b!=null&&!now&&a.enabled&&b.enabled&&Physics.ComputePenetration(a,a.transform.position,a.transform.rotation,b,b.transform.position,b.transform.rotation,out _,out _))continue;
                    if(a!=null&&b!=null)Physics.IgnoreCollision(a,b,false);list.RemoveAt(i);
                }
                if(list.Count==0||now)SetLegCore(index,false);return;
            }
            SetLegCore(index,true);
            // Re-checked every tick: re-enabling a collider clears its ignore pairs.
            var count=Players[1-index].Rig.Knockdown.CopyDynamicBodies(_knockdownBodies);
            foreach(var own in Players[index].Rig.Actor.Motor.Body.GetComponents<Collider>())
            {
                if(!own.enabled||own.isTrigger||own==_legCores[index])continue;
                for(var i=0;i<count;i++)foreach(var shape in _knockdownBodies[i].GetComponents<Collider>())
                {
                    if(!shape.enabled||shape.isTrigger||Physics.GetIgnoreCollision(own,shape))continue;
                    Physics.IgnoreCollision(own,shape,true);list.Add((own,shape));
                }
            }
        }
        private void SetLegCore(int index,bool on)
        {
            var rig=Players[index].Rig;var body=rig.Actor.Motor.Body;var core=_legCores[index];
            if(core==null)
            {
                if(!on)return;
                core=_legCores[index]=body.gameObject.AddComponent<BoxCollider>();core.enabled=false;
                core.size=LegCoreSize;core.center=new Vector3(0,LegCoreBottom+LegCoreSize.y*.5f,0);core.contactOffset=.002f;
            }
            if(core.enabled==on)return;
            // The root's mass properties stay as they were (an extra collider would move its centre of mass).
            if(on)_legCoreMass[index]=(body.centerOfMass,body.inertiaTensor,body.inertiaTensorRotation,body.automaticCenterOfMass,body.automaticInertiaTensor);
            core.enabled=on;var mass=_legCoreMass[index];
            body.centerOfMass=mass.center;body.inertiaTensor=mass.inertia;body.inertiaTensorRotation=mass.rotation;
            if(!on){body.automaticCenterOfMass=mass.autoCenter;body.automaticInertiaTensor=mass.autoInertia;return;}
            foreach(var link in rig.Links)if(link.Shape!=null)Physics.IgnoreCollision(core,link.Shape,true);
            foreach(var own in rig.Knockdown.Colliders)if(own!=null)Physics.IgnoreCollision(core,own,true);
            foreach(var own in body.GetComponents<Collider>())if(own!=core)Physics.IgnoreCollision(core,own,true);
            var other=Players[1-index].Rig;foreach(var link in other.Links)if(link.Shape!=null)Physics.IgnoreCollision(core,link.Shape,true);
        }
    }
}
