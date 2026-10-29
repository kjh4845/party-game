using UnityEngine;

namespace ProjectHotfix.PhysicsJump
{
    // Punch knockdown for the interaction rig (2026-10-17). When the PRD point threshold is reached the
    // active upper body hands the whole character to the shared limp ragdoll (RagdollRecoveryReview:
    // ten bodies incl. legs, Down timing, support probe, shared GetUp). The active links are switched
    // off while down and rebuilt in their bind-relative pose once the GetUp has finished.
    public sealed partial class ActiveUpperBodyReview
    {
        public RagdollRecoveryReview Knockdown { get; private set; }
        // Any limp episode (knockdown, climb or dropkick); KnockedDown is only the knockdown.
        public bool RagdollActive => Knockdown != null && Knockdown.Active;
        public bool KnockedDown => RagdollActive && !ClimbingRagdoll && !Dropkicking;
        public bool ClimbingRagdoll { get; private set; }
        public int Knockdowns { get; private set; }
        // Tip-over at knockdown: root angular speed (rad/s), the extra speed at head height and the
        // opposite sweep at the feet (m/s). The sweep takes the feet out from under the body so it falls
        // flat instead of folding onto its knees (2026-10-17 review: "왜 발은 바닥에 접지가 되어 있는거지").
        public float KnockdownTipSpeed = 6f, KnockdownTopSpeed = 2.2f, KnockdownFootSweep = .9f;
        // Per-knockdown variation (2026-10-17 review: every fall looked the same and static). 1 = full,
        // 0 = the same fall for the same shove. Seeded from the rig, its knockdown count and the shove,
        // so a replay of the same fight falls the same way.
        public float KnockdownVariation = 1f;
        // After a knockdown's get-up no new Down starts for GetUpProtectionSeconds (hits only push); the rig's own
        // punch, grab or dropkick ends it early (2026-10-20: stops chained knockdowns such as a dropkick landing the
        // moment the body stands; 2026-10-21: .6 -> .3 s, the rise itself is now protected).
        public float GetUpProtectionSeconds = .3f;
        // Any get-up in motion (rolling over, waiting for room, rising) cannot be hit, kicked or grabbed (2026-10-21):
        // lying still it can, the get-up once started always finishes.
        public bool GettingUp => RagdollActive && Knockdown.Rising;
        private float _downProtection;
        public bool DownProtected => _downProtection > 0 && !RagdollActive;
        public void EndDownProtection() => _downProtection = 0;
        private bool _linksSuspended;private int _koDebugTick;private Quaternion[] _shownPose;
        private struct RootState
        {
            public float Mass, AngularDamping, MaxAngularVelocity, MaxDepenetration;
            public int Iterations, VelocityIterations;
            public RigidbodyConstraints Constraints;
            public CollisionDetectionMode Collision;
            public RigidbodyInterpolation Interpolation;
        }
        private RootState _rootState;

        private void BindKnockdown()
        {
            var ragdoll = Actor.gameObject.AddComponent<RagdollRecoveryReview>();
            ragdoll.ArticulatedTorso = true; ragdoll.RelaxedRagdoll = true; ragdoll.UseGetUpAnimation = true; ragdoll.UprightReturnWithoutGrip = true; ragdoll.ProbeFromPelvis = true; ragdoll.SnapCompleteAfterAnimation = true; ragdoll.ProceduralGetUp = true;
            ragdoll.LooseLegs = true; ragdoll.LooseUpperBody = true; ragdoll.SelfCollision = true; ragdoll.LooseElbowBend = 90f; ragdoll.FeetPlantedGetUp = true;
            // Slimmer than the standing torso (.15): at full thickness the limp body read as a block and
            // propped itself up (2026-10-17 review). Arm proxies use the transport radii; legs the r20 sizes.
            // Also the longer review Down wait (2026-10-17: 2.5 s, +.5 s per
            // Down, 4 s max). The GetUp plays twice as fast as authored (~1.2 s).
            var profile = Actor.GetComponentInChildren<SkinTransportProfile>();
            if (profile != null && profile.HasArmColliderRadii)
            {
                ragdoll.SpineRadius = profile.HasTorsoColliderRadii ? profile.RagdollSpineRadius : .11f;
                ragdoll.ChestRadius = profile.HasTorsoColliderRadii ? profile.RagdollChestRadius : .12f; ragdoll.PelvisRadius = .10f;
                ragdoll.UpperArmRadius = profile.UpperArmRadius; ragdoll.ForearmRadius = profile.ForearmRadius;
            }
            // Fitted to the r46 skin (2026-10-22: a landing fist and a stepping foot sank up to 4-6 cm into the downed
            // body's sides, and the 2.5 cm calves sat inside 4-5.5 cm skin): side capsules fill the torso's width without
            // adding depth (the .15 round torso propped itself up, above), the pelvis lies across the hips.
            ragdoll.ChestSideRadius = .085f; ragdoll.ChestSideOffset = .058f; ragdoll.SpineRadius = .105f; ragdoll.SpineSideRadius = .095f; ragdoll.SpineSideOffset = .065f;
            ragdoll.PelvisAcrossRadius = .075f; ragdoll.PelvisAcrossHalfWidth = .17f; ragdoll.ThighRadius = .055f; ragdoll.CalfRadius = .045f; ragdoll.HeadLength = .29f;
            ragdoll.DownBaseSeconds = 2.5; ragdoll.DownIncrementSeconds = .5; ragdoll.DownMaximumSeconds = 4;
            ragdoll.GetUpSpeed = 2;
            // Fully limp (every joint drive off) and grippy enough to roll and catch on the floor rather
            // than slide (2026-10-17: the .2 ragdoll friction read as a sliding block).
            ragdoll.RagdollFriction = .8f; ragdoll.RagdollFrictionCombine = PhysicsMaterialCombine.Maximum;
            ragdoll.Bind(Actor);
            Knockdown = ragdoll;
        }

        // A climb hands the whole body to the limp ragdoll (no Down: no count, score or wait); the climb
        // controller holds the ledge with its forearms and gets it over (ActiveGrabPairReview.Climb).
        public bool BeginClimbRagdoll()
        {
            if (!EnterRagdoll(false)) return false;
            ClimbingRagdoll = true; Actor.Motor.Body.constraints = RigidbodyConstraints.None;
            return true;
        }

        private bool EnterRagdoll(bool down)
        {
            if (Knockdown == null || RagdollActive) return false;
            var root = Actor.Motor.Body;
            _rootState = new RootState
            {
                Mass = root.mass, AngularDamping = root.angularDamping, MaxAngularVelocity = root.maxAngularVelocity,
                MaxDepenetration = root.maxDepenetrationVelocity, Iterations = root.solverIterations,
                VelocityIterations = root.solverVelocityIterations, Constraints = root.constraints,
                Collision = root.collisionDetectionMode, Interpolation = root.interpolation
            };
            for (var side = 0; side < 2; side++) CancelPunchReference(side);
            ClearImpactReaction(); _plantTimer = 0; _settleLean = 0;
            if (!(down ? Knockdown.BeginDown(Vector3.zero, Vector3.zero) : Knockdown.BeginTumble(Vector3.zero, Vector3.zero))) return false;
            SuspendLinks();
            return true;
        }

        // A lucky punch (PhysicalPunchReview.LuckyChance): not a knockdown, a tumble (no Down, no points spent) thrown
        // along velocityChange and spun at spin (rad/s); it gets up by the tumble get-up.
        public int Launches { get; private set; }
        public bool BeginLaunch(Vector3 velocityChange, Vector3 spin)
        {
            if (!EnterRagdoll(false)) return false;
            Actor.Motor.Body.constraints = RigidbodyConstraints.None;
            // The limp ragdoll caps spin at LimpMaximumSpin.
            spin = Vector3.ClampMagnitude(spin, LimpMaximumSpin);
            var bodies = new Rigidbody[16]; var count = Knockdown.CopyDynamicBodies(bodies);
            var mass = 0f; var center = Vector3.zero;
            for (var i = 0; i < count; i++) { mass += bodies[i].mass; center += bodies[i].mass * bodies[i].worldCenterOfMass; }
            center /= mass;
            for (var i = 0; i < count; i++)
            {
                bodies[i].AddForce(velocityChange + Vector3.Cross(spin, bodies[i].worldCenterOfMass - center), ForceMode.VelocityChange);
                bodies[i].AddTorque(spin, ForceMode.VelocityChange);
            }
            Launches++;
            return true;
        }

        // velocityChange: the confirmed strike's shove, applied to every ragdoll body.
        public bool BeginKnockdown(Vector3 velocityChange)
        {
            if (!EnterRagdoll(true)) return false;
            var root = Actor.Motor.Body;
            var bodies = new Rigidbody[16]; var count = Knockdown.CopyDynamicBodies(bodies);
            var random = new System.Random(KnockdownSeed(velocityChange));
            var amount = Mathf.Clamp01(KnockdownVariation);
            float Range(float low, float high) => Mathf.Lerp(low, high, (float)random.NextDouble());
            float Scale(float low, float high) => Mathf.Lerp(1, Range(low, high), amount);
            velocityChange *= Scale(.8f, 1.3f);
            for (var i = 0; i < count; i++) bodies[i].AddForce(velocityChange, ForceMode.VelocityChange);
            // A limp body must go over, not stand on its locked-straight legs (PRD 4.5: lies like a doll
            // with its strength gone). Tip it in the strike direction: the upper body is pushed along
            // the strike, the feet are swept the other way; the root (hips) starts rotating about the matching axis.
            var direction = Vector3.ProjectOnPlane(velocityChange, Vector3.up);
            if (direction.sqrMagnitude < .0001f) direction = -(root.rotation * Vector3.forward);
            // Varied fall: the tip veers off the strike line, tip/top/sweep strengths differ, the body
            // twists about the vertical while going down, and each limb is flung its own way.
            direction = Quaternion.AngleAxis(Range(-40, 40) * amount, Vector3.up) * direction.normalized;
            var axis = Vector3.Cross(Vector3.up, direction);
            root.constraints = RigidbodyConstraints.None;
            root.AddTorque(axis * KnockdownTipSpeed * Scale(.5f, 1.4f) + Vector3.up * Range(-5, 5) * amount, ForceMode.VelocityChange);
            var top = KnockdownTopSpeed * Scale(.6f, 1.4f); var sweep = KnockdownFootSweep * Scale(.3f, 1.4f);
            for (var i = 0; i < count; i++)
            {
                var height = Mathf.Clamp01((bodies[i].worldCenterOfMass.y - root.position.y) / .7f);
                bodies[i].AddForce(direction * (top * height - sweep * (1 - height)), ForceMode.VelocityChange);
                var name = bodies[i].name;
                if (!name.StartsWith("UpperArm") && !name.StartsWith("Forearm") && !name.StartsWith("Thigh") && !name.StartsWith("Calf") && !name.StartsWith("Head")) continue;
                var fling = new Vector3(Range(-1, 1), Range(-.2f, 1), Range(-1, 1));
                bodies[i].AddForce(fling.normalized * Range(.5f, 3.5f) * amount, ForceMode.VelocityChange);
            }
            Knockdowns++;
            return true;
        }

        // Review fixture: knock down and lay the limp body still on the floor with its torso (pelvis) in the given
        // rotation, to check the get-up from each lying orientation (2026-10-20).
        public bool BeginLyingForReview(Quaternion torso)
        {
            if (!EnterRagdoll(true)) return false;
            var root = Actor.Motor.Body; root.constraints = RigidbodyConstraints.None;
            var bodies = new Rigidbody[16]; var count = Knockdown.CopyDynamicBodies(bodies);
            var turn = torso * Quaternion.Inverse(root.rotation); var pivot = root.position; var lowest = float.PositiveInfinity;
            for (var i = 0; i < count; i++)
            {
                var body = bodies[i]; var position = pivot + turn * (body.position - pivot); var rotation = turn * body.rotation;
                body.transform.SetPositionAndRotation(position, rotation); body.position = position; body.rotation = rotation;
                body.linearVelocity = body.angularVelocity = Vector3.zero;
            }
            Physics.SyncTransforms();
            for (var i = 0; i < count; i++) foreach (var shape in bodies[i].GetComponentsInChildren<Collider>()) if (shape.enabled) lowest = Mathf.Min(lowest, shape.bounds.min.y);
            for (var i = 0; i < count; i++) { var position = bodies[i].position + Vector3.up * (.01f - lowest); bodies[i].transform.position = position; bodies[i].position = position; }
            Physics.SyncTransforms(); Knockdowns++;
            return true;
        }

        private int KnockdownSeed(Vector3 shove)
        {
            unchecked
            {
                var hash = (int)2166136261;
                foreach (var c in name) hash = (hash ^ c) * 16777619;
                hash = (hash ^ Knockdowns) * 16777619;
                hash = (hash ^ Mathf.RoundToInt(shove.x * 1000)) * 16777619;
                hash = (hash ^ Mathf.RoundToInt(shove.y * 1000)) * 16777619;
                return (hash ^ Mathf.RoundToInt(shove.z * 1000)) * 16777619;
            }
        }

        // Before physics. Down timing first, then the ragdoll's own recovery step (manual harness order).
        public void StepKnockdown(float seconds)
        {
            _stepSeconds = seconds;
            // The pose on screen before this step: the get-up's last frame when the ragdoll hands back.
            if (_shownPose == null) _shownPose = new Quaternion[_links.Count];
            for (var i = 0; i < _links.Count; i++) _shownPose[i] = _links[i].Bone.rotation;
            Knockdown.Down.Step(seconds);
            Knockdown.Step(seconds);
            if (System.Environment.GetEnvironmentVariable("PARTY_KO_DEBUG") == "1" && (_koDebugTick++ % 6) == 0)
            {
                var root = Actor.Motor.Body;
                Debug.Log($"KO {name} active={Knockdown.Active} phase={Knockdown.Down.State.Phase} remain={Knockdown.Down.State.RemainingSeconds:F2} align={Knockdown.ReadyAngle:F0}/{Knockdown.ReadyAngular:F2} root={root.position:F3}{(Knockdown.Active?Knockdown.ElbowDebug():"")} up={(root.rotation * Vector3.up):F2}");
            }
            if (!Knockdown.Active) ResumeLinks();
        }

        // After physics, in place of the active pose capture.
        private void CaptureKnockdown()
        {
            Actor.FixedPose.RestoreForEvaluation();
            Knockdown.ApplyPose(_stepSeconds);
            if (_shoulderHelpers != null) _shoulderHelpers.Apply();
            Actor.FixedPose.CaptureEvaluatedPose(); Captures++;
        }

        private void SuspendLinks()
        {
            foreach (var link in _links) link.Body.gameObject.SetActive(false);
            _linksSuspended = true;
        }

        // The GetUp has ended upright: rebuild each joint in its bind-relative pose at the current
        // heading (re-enabling a joint captures a new reference frame), restore the rig's own root
        // settings (the ragdoll restores a 20 kg capsule) and the collision-ignore pairs, which
        // Unity clears when a collider is re-enabled.
        private void ResumeLinks()
        {
            if (!_linksSuspended) return;
            var wasDown = !ClimbingRagdoll && !Dropkicking;
            var root = Actor.Motor.Body;
            root.mass = _rootState.Mass; root.angularDamping = _rootState.AngularDamping; root.maxAngularVelocity = _rootState.MaxAngularVelocity;
            root.maxDepenetrationVelocity = _rootState.MaxDepenetration; root.solverIterations = _rootState.Iterations;
            root.solverVelocityIterations = _rootState.VelocityIterations; root.constraints = _rootState.Constraints;
            root.collisionDetectionMode = _rootState.Collision; root.interpolation = _rootState.Interpolation;
            var heading = Quaternion.LookRotation(Vector3.ProjectOnPlane(root.rotation * Vector3.forward, Vector3.up).normalized, Vector3.up);
            root.rotation = heading; root.transform.rotation = heading; root.angularVelocity = Vector3.zero;
            foreach (var link in _links)
            {
                var parent = link.ParentBody;
                var rotation = parent.rotation * link.StartRelative;
                var position = parent.transform.TransformPoint(link.StartConnectedAnchor);
                link.Body.transform.SetPositionAndRotation(position, rotation);
                link.Body.position = position; link.Body.rotation = rotation;
                link.Joint.connectedAnchor = link.StartConnectedAnchor;
                link.Joint.targetRotation = Quaternion.identity; link.Joint.slerpDrive = link.Drive;
                link.Body.gameObject.SetActive(true);
                link.Body.linearVelocity = root.linearVelocity; link.Body.angularVelocity = Vector3.zero;
            }
            ApplyCollisionIgnores();
            // Each joint has now taken its reference in the bind-relative pose; start the links from the pose
            // last shown instead. Started from the bind A-pose, the arms dropped 40 cm in one step and swung
            // for half a second (2026-10-17: "팔을 부들부들"). Parents come before children in _links.
            if (_shownPose != null)
                for (var i = 0; i < _links.Count; i++)
                {
                    var link = _links[i]; var parent = link.ParentBody;
                    var position = parent.position + parent.rotation * Vector3.Scale(parent.transform.lossyScale, link.StartConnectedAnchor);
                    link.Body.transform.SetPositionAndRotation(position, _shownPose[i]);
                    link.Body.position = position; link.Body.rotation = _shownPose[i];
                }
            ResetShoulderMotion();
            _linksSuspended = false; ClimbingRagdoll = false; Knockdown.HoldTumble = false; EndDropkick(); ClearStruggle(); _downProtection = wasDown ? GetUpProtectionSeconds : 0;
            Physics.SyncTransforms();
        }

        private bool _ignoreUpperArmTorso;
        private void ApplyCollisionIgnores()
        {
            var baseShape = Actor.GetComponent<CapsuleCollider>();
            foreach (var link in _links)
                Physics.IgnoreCollision(link.Shape, link.ParentName == null ? baseShape : Find(link.ParentName).Shape, true);
            if (_ignoreUpperArmTorso)
                foreach (var name in new[] { "UpperArm_L", "UpperArm_R" })
                    Physics.IgnoreCollision(Find(name).Shape, Find("Spine").Shape, true);
        }
    }
}
