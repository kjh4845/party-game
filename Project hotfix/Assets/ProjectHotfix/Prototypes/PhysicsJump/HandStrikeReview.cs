using System;
using System.Collections.Generic;
using ProjectHotfix.Simulation;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;

namespace ProjectHotfix.PhysicsJump
{
    // Local authority harness; all hit geometry is independent of rendered bones.
    [DefaultExecutionOrder(100)]
    public sealed class HandStrikeReview : MonoBehaviour
    {
        private struct Edge { public ulong Sequence; public int Side; public bool Down, Grounded;public double? HeldSeconds; }
        private readonly Queue<Edge> _edges = new Queue<Edge>();
        private readonly bool[] _buttons = new bool[2];
        private readonly Vector3[] _previous = new Vector3[2];
        private readonly ulong[] _previousAction = new ulong[2];
        private readonly double[] _previousAge = new double[2];
        private readonly RaycastHit[] _hits = new RaycastHit[64];
        private readonly RaycastHit[] _occlusionHits = new RaycastHit[64];
        private readonly Collider[] _overlaps = new Collider[64];
        private double _time;
        public double SimulationTime => _time;
        private ulong _sequence;
        private uint _motorReset;
        public PhysicsJumpReview Actor;
        public GrabPhysicsReview Grabs;
        public StrikeReviewTarget[] ReviewTargets = Array.Empty<StrikeReviewTarget>();
        public void ResetTargets() { if (Grabs != null) Grabs.ReleaseAll("Reset"); foreach (var target in ReviewTargets) if (target != null) target.ResetReview(); }
        public GroundHandActionStateMachine State { get; private set; } = new GroundHandActionStateMachine();
        public bool InputSuppressed { get; private set; }
        // START values for this review, not final gameplay tuning.
        public float SweepRadius = .045f;
        public float ImpulseNewtonSeconds = 20f;
        public float MaximumDeltaSpeed = 1f;
        public int QueryOverflowCount { get; private set; }
        public bool CanHit(int side) => State.Active(side) && (!PreciseHits || State.SourcePunchAge(side) <= 7d/30 + 1e-6);
        [SerializeField] private double _configuredPunchRate = 1;
        public bool PreciseHits;
        private void Awake() { State = new GroundHandActionStateMachine(.15, _configuredPunchRate); }

        public void ConfigurePunchTiming(double playbackRate)
        {
            if (_time > 0) throw new InvalidOperationException("Configure punch timing before starting the review.");
            State = new GroundHandActionStateMachine(State.HoldThresholdSeconds, playbackRate); ClearForReset();
            _configuredPunchRate = playbackRate;
        }

        public void ConfigureThreshold(double seconds)
        {
            if (_time > 0) throw new InvalidOperationException("Configure the threshold before starting the review.");
            State = new GroundHandActionStateMachine(seconds, State.PunchPlaybackRate); ClearForReset();
        }
        private void FixedUpdate() { if (Actor != null && !Actor.Motor.ManualStep) Step(Time.fixedDeltaTime); }

        public void SubmitButtons(bool leftDown, bool rightDown, bool leftTapWithinFrame = false, bool rightTapWithinFrame = false, double frameSeconds = 0)
        {
            var wasArmed = State.Armed;
            State.ObserveButtonsForRearm(leftDown, rightDown, InputSuppressed || Actor.Motor.ActionsBlocked);
            var values = new[] { leftDown, rightDown };
            for (var side = 0; side < 2; side++)
            {
                if (!wasArmed) { _buttons[side] = values[side]; continue; }
                if (values[side] == _buttons[side])
                {
                    // Preserve a down/up pair observed within one render frame.
                    if (!values[side] && (side == 0 ? leftTapWithinFrame : rightTapWithinFrame)
                        && frameSeconds >= 0 && frameSeconds < State.HoldThresholdSeconds && State.Armed && !InputSuppressed)
                    {
                        _edges.Enqueue(new Edge { Sequence = ++_sequence, Side = side, Down = true, Grounded = Actor.Motor.Grounded });
                        _edges.Enqueue(new Edge { Sequence = ++_sequence, Side = side, Down = false, Grounded = Actor.Motor.Grounded });
                    }
                    continue;
                }
                _buttons[side] = values[side];
                if (State.Armed && !InputSuppressed)
                    _edges.Enqueue(new Edge { Sequence = ++_sequence, Side = side, Down = values[side], Grounded = Actor.Motor.Grounded });
            }
        }
        public void SetInputSuppressed(bool suppressed)
        {
            InputSuppressed = suppressed;
            if (!suppressed) return;
            if (Grabs != null) Grabs.ReleaseAll("Menu");
            State.Suppress(); _edges.Clear();
            Actor.Motor.MoveInput = Vector2.zero; Actor.Motor.SprintRequested = false;
            Actor.Motor.ClearPendingInput();
        }
        private void ClearForReset() { if (Grabs != null) Grabs.ReleaseAll("Reset"); State.Reset(); _edges.Clear(); Array.Clear(_previousAction, 0, 2); }

        public void SubmitFixedMouse()
        {
            var mouse=Mouse.current;if(mouse==null)return;var left=mouse.leftButton.isPressed;var right=mouse.rightButton.isPressed;
            var wasArmed=State.Armed;State.ObserveButtonsForRearm(left,right,InputSuppressed||Actor.Motor.ActionsBlocked);
            if(!wasArmed){_buttons[0]=left;_buttons[1]=right;return;}
            foreach(var edge in ReviewInputClock.MouseEdges)if(edge.Device==mouse.deviceId)SubmitTimedButtonEdge(edge.Side,edge.Down,edge.HeldSeconds);
        }
        public void SubmitTimedButtonEdge(int side,bool down,double heldSeconds=double.NaN)
        {
            if(side<0||side>1)throw new ArgumentOutOfRangeException(nameof(side));
            if(_buttons[side]==down)return;_buttons[side]=down;
            if(State.Armed&&!InputSuppressed)_edges.Enqueue(new Edge{Sequence=++_sequence,Side=side,Down=down,Grounded=Actor.Motor.Grounded,
                HeldSeconds=!down&&!double.IsNaN(heldSeconds)&&!double.IsInfinity(heldSeconds)&&heldSeconds>=0 ? (double?)heldSeconds : null});
        }

        public void Step(float deltaTime)
        {
            if (deltaTime <= 0 || float.IsNaN(deltaTime) || float.IsInfinity(deltaTime)) throw new ArgumentOutOfRangeException(nameof(deltaTime));
            _time += deltaTime;
            var reset = Actor.Motor.PresentationState.ResetVersion;
            if (reset != _motorReset) { _motorReset = reset; ClearForReset(); }
            if (!Actor.Motor.AuthorityEnabled || Actor.Motor.ActionsBlocked)
            { if (Grabs != null) Grabs.ReleaseAll("AuthorityDisabled"); State.Suppress(); _edges.Clear(); return; }
            foreach (var edge in _edges)
                if (!edge.Down && edge.HeldSeconds.HasValue) State.PrepareTimedRelease(edge.Side, _time, edge.HeldSeconds.Value);
            while (_edges.Count > 0)
            {
                var edge = _edges.Dequeue();
                if (!edge.Down && Grabs != null) Grabs.TryReleaseThrow(edge.Side, edge.Sequence);
                State.Receive(edge.Sequence, edge.Side, edge.Down, _time, edge.Grounded,
                    edge.Side == 0 ? Actor.Arms.LeftAvailable : Actor.Arms.RightAvailable,edge.HeldSeconds);
            }
            State.Advance(_time);
            for (var side = 0; side < 2; side++)
                if (!(side == 0 ? Actor.Arms.LeftAvailable : Actor.Arms.RightAvailable)) State.CancelHand(side);
            if (Grabs != null) Grabs.Step(deltaTime);
            if (PreciseHits) PunchHitRig.SyncScene(gameObject.scene,deltaTime);
            var scene = gameObject.scene.GetPhysicsScene();
            for (var side = 0; side < 2; side++)
            {
                if (!(side == 0 ? Actor.Arms.LeftAvailable : Actor.Arms.RightAvailable)) State.CancelHand(side);
                if (!CanHit(side)) { _previousAction[side] = 0; continue; }
                var current = StrikePoint(side, State.PunchAge(side));
                var start = _previousAction[side] == State.ActionId(side) ? _previous[side] : StrikePoint(side, GroundHandActionStateMachine.ActiveStart / State.PunchPlaybackRate);
                var fromAge=_previousAction[side]==State.ActionId(side) ? _previousAge[side] : GroundHandActionStateMachine.ActiveStart/State.PunchPlaybackRate;
                _previous[side] = current; _previousAction[side] = State.ActionId(side);
                if(PreciseHits)
                {
                    var pieces=Math.Max(1,(int)Math.Ceiling((State.PunchAge(side)-fromAge)*State.PunchPlaybackRate*Actor.PunchLibrary.sampleRate));
                    for(var part=1;part<=pieces;part++)
                    { var end=StrikePoint(side,fromAge+(State.PunchAge(side)-fromAge)*part/pieces);Sweep(scene,side,start,end);start=end; }
                }
                else Sweep(scene, side, start, current);
                _previousAge[side]=State.PunchAge(side);
            }
        }

        private void OnDisable() { if (Grabs != null) Grabs.ReleaseAll("HandsDisabled"); State.Suppress(); _edges.Clear(); }

        public Vector3 StrikePoint(int side, double age)
        {
            if(PreciseHits)
            {
                var clip=Actor.PunchLibrary.clips[side];var source=(float)(age*State.PunchPlaybackRate);
                var frame=Mathf.Clamp(source/clip.duration*(clip.frames.Length-1),0,clip.frames.Length-1);
                var first=Mathf.Min(Mathf.FloorToInt(frame),clip.frames.Length-2);var t=frame-first;
                var a=Vector3.Lerp(clip.frames[first].heads[3],clip.frames[first].tails[3],.7f);
                var b=Vector3.Lerp(clip.frames[first+1].heads[3],clip.frames[first+1].tails[3],.7f);
                return transform.TransformPoint(Vector3.Lerp(a,b,t));
            }
            var u = Mathf.Clamp01((float)((age * State.PunchPlaybackRate - GroundHandActionStateMachine.ActiveStart) / (.233333333333 - GroundHandActionStateMachine.ActiveStart)));
            return transform.TransformPoint(new Vector3(side == 0 ? -.12f : .12f, .66f, Mathf.Lerp(.12f, .38f, u)));
        }
        private bool Own(Collider collider) => collider.transform.IsChildOf(transform) || StrikeReviewHurtbox.Resolve(collider)?.Body == Actor.Motor.Body;

        private bool QuerySurface(Collider collider)
        {
            if(!PreciseHits)return !collider.isTrigger;
            var volume=collider.GetComponent<PunchHurtbox>();
            if(volume!=null)return volume.AcceptsHit;
            if(collider.isTrigger)return false;
            var target=StrikeReviewHurtbox.Resolve(collider);
            if(target==null)return true;
            if(target.GetComponent<PunchHitRig>()==null)return true;
            var ragdoll=target.GetComponent<RagdollRecoveryReview>();
            return ragdoll!=null && ragdoll.Active && collider!=target.GetComponent<CapsuleCollider>();
        }

        private void Sweep(PhysicsScene scene, int side, Vector3 start, Vector3 end)
        {
            var radius=PreciseHits ? .03f : SweepRadius;
            var trigger=PreciseHits ? QueryTriggerInteraction.Collide : QueryTriggerInteraction.Ignore;
            var count = scene.OverlapSphere(start, radius, _overlaps, ~0, trigger);
            if (count == _overlaps.Length) { QueryOverflowCount++; return; }
            // A wall containing the start point blocks the sweep before targets.
            for (var i = 0; i < count; i++) if (!Own(_overlaps[i]) && QuerySurface(_overlaps[i]) && StrikeReviewHurtbox.Resolve(_overlaps[i]) == null) return;
            if(PreciseHits)
            {
                Collider nearest=null;var distance=float.PositiveInfinity;
                for(var i=0;i<count;i++)if(!Own(_overlaps[i])&&QuerySurface(_overlaps[i]))
                { var d=Vector3.SqrMagnitude(_overlaps[i].ClosestPoint(start)-start);if(d<distance){distance=d;nearest=_overlaps[i];} }
                if(nearest!=null){Confirm(side,nearest);return;}
            }
            else for (var i = 0; i < count; i++) if (!Own(_overlaps[i])) Confirm(side, _overlaps[i]);
            var delta = end - start;
            if (delta.sqrMagnitude < 1e-12f) return;
            count = scene.SphereCast(start, radius, delta.normalized, _hits, delta.magnitude, ~0, trigger);
            if (count == _hits.Length) { QueryOverflowCount++; return; }
            Array.Sort(_hits, 0, count, Comparer<RaycastHit>.Create((a, b) => a.distance.CompareTo(b.distance)));
            for (var i = 0; i < count; i++)
            {
                if (Own(_hits[i].collider) || !QuerySurface(_hits[i].collider)) continue;
                Confirm(side, _hits[i].collider);
                break; // The nearest non-self collider blocks anything behind it.
            }
        }
        private void Confirm(int side, Collider collider)
        {
            var target = StrikeReviewHurtbox.Resolve(collider);
            if (target == null || !target.Eligible || !target.enabled || !target.gameObject.activeInHierarchy
                || target.Body == null || target.Body.isKinematic || target.Body == Actor.Motor.Body) return;
            // Recheck occlusion from the body: a moving sweep must not resume
            // behind a wall after an earlier segment was blocked by that wall.
            var origin = transform.TransformPoint(new Vector3(side == 0 ? -.12f : .12f, .66f, 0f));
            var destination = collider.ClosestPoint(StrikePoint(side, State.PunchAge(side)));
            var delta = destination - origin;
            if (delta.sqrMagnitude > 1e-12f)
            {
                var count = gameObject.scene.GetPhysicsScene().Raycast(origin, delta.normalized, _occlusionHits, delta.magnitude + .0001f, ~0, PreciseHits ? QueryTriggerInteraction.Collide : QueryTriggerInteraction.Ignore);
                if (count == _occlusionHits.Length) { QueryOverflowCount++; return; }
                for (var i = 0; i < count; i++)
                    if (!Own(_occlusionHits[i].collider) && QuerySurface(_occlusionHits[i].collider) && StrikeReviewHurtbox.Resolve(_occlusionHits[i].collider) != target) return;
            }
            if (!State.TryConfirmHit(side, target.GetInstanceID())) return;
            var magnitude = Mathf.Min(Mathf.Max(0, ImpulseNewtonSeconds), target.Body.mass * Mathf.Max(0, MaximumDeltaSpeed));
            target.ConfirmHit(State.ActionId(side), transform.forward * magnitude, this, collider);
        }
    }
}
