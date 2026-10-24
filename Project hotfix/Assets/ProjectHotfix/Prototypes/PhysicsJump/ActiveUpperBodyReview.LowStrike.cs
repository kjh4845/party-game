using UnityEngine;

namespace ProjectHotfix.PhysicsJump
{
    // A punch at a body lying in front of the attacker (ActiveGrabPairReview.AimLowStrike): the attacker bends down at
    // the waist through the low-reach hinge, deeper and faster than a grab's, the punching arm is pitched down by what
    // the bend leaves short of the target point and ends turned onto it instead of crossing in (the far arm's hook
    // otherwise fell 4-15 cm beside the body, 5 of 12 punches missed). Nothing else of the stroke changes (2026-10-24
    // review: hit the lying body, uncounted and light, without a separate action).
    public sealed partial class ActiveUpperBodyReview
    {
        // Trunk bend of a low strike (deg; a grab's low reach bends 35), how fast it bends in (per second; the stroke
        // extends ~.15 s after the press, a grab bends in at 3/s), the extended lift of the stroke it corrects
        // (BodyStrikeArmPath.Merged) and the most the arm is pitched down.
        public float LowStrikeLean=55,LowStrikeBendRate=9,LowStrikeReachLift=84,LowStrikeMaxPitch=55;
        private readonly bool[] _lowStrike=new bool[2];
        private readonly Vector3[] _lowStrikeTarget=new Vector3[2];
        private readonly float[] _lowStrikePitch=new float[2],_lowStrikeAim=new float[2];
        private float _lowReachLean=35,_lowReachDrop;
        public bool LowStriking=>_lowStrike[0]&&_punchReference[0]||_lowStrike[1]&&_punchReference[1];
        public float LowStrikePitch(int side)=>_lowStrikePitch[side];
        public void SetLowStrike(int side,bool active,Vector3 point){_lowStrike[side]=active;if(active)_lowStrikeTarget[side]=point;}
        // Arm pitch (deg, down) that puts the extended fist on the target: its depression from the shoulder less the
        // depression the stroke already has (90 - lift, plus the chest's forward lean). Aim (deg, right of the chest's
        // facing): where the target lies from the shoulder.
        private void UpdateLowStrikePitch(int side,Link upper)
        {
            var goal=0f;
            if(_lowStrike[side]&&_punchReference[side])
            {
                var d=_lowStrikeTarget[side]-upper.Body.position;var flat=new Vector2(d.x,d.z).magnitude;
                var depression=Mathf.Atan2(-d.y,Mathf.Max(flat,.05f))*Mathf.Rad2Deg;
                var chest=Find("Chest");var up=chest.Body.rotation*(Quaternion.Inverse(chest.StartRotation)*Vector3.up);
                var forward=Vector3.ProjectOnPlane(Actor.Motor.Body.rotation*Vector3.forward,Vector3.up).normalized;
                var lean=Mathf.Asin(Mathf.Clamp(Vector3.Dot(up,forward),-1,1))*Mathf.Rad2Deg;
                goal=Mathf.Clamp(depression-(90-LowStrikeReachLift+lean),0,LowStrikeMaxPitch);
                var chestForward=Vector3.ProjectOnPlane(chest.Body.rotation*(Quaternion.Inverse(chest.StartRotation)*Vector3.forward),Vector3.up);
                var aim=Mathf.Clamp(Vector3.SignedAngle(chestForward,new Vector3(d.x,0,d.z),Vector3.up),-40,40);
                _lowStrikeAim[side]=Mathf.MoveTowards(_lowStrikeAim[side],aim,_stepSeconds*600);
            }
            _lowStrikePitch[side]=Mathf.MoveTowards(_lowStrikePitch[side],goal,_stepSeconds*400);
        }
        private void ClearLowStrike()
        {
            for(var side=0;side<2;side++){_lowStrike[side]=false;_lowStrikePitch[side]=_lowStrikeAim[side]=0;}
            _lowReachLean=35;_lowReachDrop=0;
        }
    }
}
