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
    public sealed partial class LedgeRecoveryReviewTests
    {
        [UnityTest] public IEnumerator BoundedWallContactPreservesRecoveryAndMeasuresSkinIntersection()
        {
            var rows = new List<string>();
            foreach (var hands in new[] { 1, 2, 3 })
            {
                var roots = new List<Vector3>(); var signatures = new List<string>();
                foreach (var candidate in new[] { false, true })
                {
                    Create(true, true); foreach (var actor in _boundary.Arena.Actors) actor.Hands.Grabs.BoundedContactPose = candidate;
                    ReadyHold(hands); SeedFall(2, -.65f); var input = new RecoveryInput(); var held = 0;
                    var skins = Actor.Pose.GetComponentsInChildren<SkinnedMeshRenderer>(); var mesh = new Mesh();
                    var masks = new Dictionary<SkinnedMeshRenderer, bool[]>();
                    foreach (var skin in skins)
                    {
                        var arms = skin.bones.Select(b => b.name.StartsWith("Clavicle") || b.name.StartsWith("UpperArm") || b.name.StartsWith("Forearm") || b.name.StartsWith("HandLogical")).ToArray();
                        masks[skin] = skin.sharedMesh.boneWeights.Select(w => (arms[w.boneIndex0] ? w.weight0 : 0) + (arms[w.boneIndex1] ? w.weight1 : 0) + (arms[w.boneIndex2] ? w.weight2 : 0) + (arms[w.boneIndex3] ? w.weight3 : 0) > .1f).ToArray();
                    }
                    var walls = _boundary.GetComponentsInChildren<ReviewGrabSurface>().Select(s => s.Shape.bounds).ToArray();
                    var penetration = 0f; var holdingPenetration = 0f; var physicsError = 0f; var correction = 0f;
                    for (var tick = 0; tick < 150; tick++)
                    {
                        if (Ledge.IsHolding(0) || Ledge.IsHolding(1)) held++;
                        if (held > 0 && held < 35) { HoldStill(); Tick(); }
                        else if (held >= 35) { Actor.Motor.MoveInput = Vector2.up; Actor.Motor.SprintRequested = true; RecoveryStep(input, hands); }
                        else Tick();
                        var signature = $"{Ledge.IsHolding(0)}/{Ledge.IsHolding(1)}/{Ledge.TotalAssists}/{Actor.Motor.Grounded}";
                        if (!candidate) { roots.Add(Actor.Motor.Body.position); signatures.Add(signature); }
                        else { physicsError = Mathf.Max(physicsError, Vector3.Distance(roots[tick], Actor.Motor.Body.position)); Assert.That(signature, Is.EqualTo(signatures[tick])); }
                        correction = Mathf.Max(correction, Actor.GrabPose.MaximumBoneCorrection);
                        foreach (var alpha in new[] { 0f, .5f, 1f })
                        {
                            if (tick == 0 && alpha < 1) continue;
                            Actor.FixedPose.Present(alpha);
                            foreach (var skin in skins)
                            {
                                skin.BakeMesh(mesh); var vertices = mesh.vertices;
                                for (var v = 0; v < vertices.Length; v++) if (masks[skin][v])
                                {
                                    var point = skin.transform.TransformPoint(vertices[v]);
                                    foreach (var box in walls) if (box.Contains(point))
                                    {
                                        var lo = point - box.min; var hi = box.max - point; var depth = Mathf.Min(lo.x, lo.y, lo.z, hi.x, hi.y, hi.z);
                                        penetration = Mathf.Max(penetration, depth);
                                        if (held > 5 && held < 35) holdingPenetration = Mathf.Max(holdingPenetration, depth);
                                    }
                                }
                            }
                        }
                    }
                    rows.Add($"candidate={candidate}, hands={hands}, physicsError={physicsError:R}, allArmSkinPenetration={penetration:R}, stableHangPenetration={holdingPenetration:R}, maxBoneCorrection={correction:R}, recovered={input.finished}, assists={Ledge.TotalAssists}");
                    var output = Environment.GetEnvironmentVariable("CODEX_CONTACT_OUTPUT") ?? Path.GetFullPath(Path.Combine(Application.dataPath, "../../.codex_tmp/grab-contact-bounded-20260913/Candidate")); Directory.CreateDirectory(output); File.WriteAllLines(Path.Combine(output, "BoundedWall.txt"), rows);
                    Assert.That(physicsError, Is.LessThan(.00001f)); Assert.That(input.finished, Is.True); Assert.That(Ledge.TotalAssists, Is.LessThanOrEqualTo(2));
                    UnityEngine.Object.Destroy(mesh); var op = SceneManager.UnloadSceneAsync(_scene); while (op != null && !op.isDone) yield return null;
                }
            }
        }
        [UnityTest] public IEnumerator CaptureWallSpacingAndExitCandidate()
        {
            foreach(var hands in new[]{1,2,3})foreach(var candidate in new[]{false,true})
            {
            Create(true,true);Actor.Hands.Grabs.BoundedContactPose=candidate;if(candidate)Ledge.EnableContactReview();ReadyHold(hands);SeedFall(2,-.65f);var camera=_boundary.Arena.SharedCamera.View;camera.fieldOfView=32;camera.aspect=16f/9;
            _boundary.Arena.GetComponentInChildren<Light>().intensity=1.2f;
            var folder=Path.Combine(Environment.GetEnvironmentVariable("CODEX_CONTACT_OUTPUT")??Path.GetFullPath(Path.Combine(Application.dataPath,"../../.codex_tmp/auto-lift-20260911")),(candidate?"WallAfter":"WallBefore")+hands);Directory.CreateDirectory(folder);
            var rt=new RenderTexture(960,540,24);rt.Create();camera.targetTexture=rt;var image=new Texture2D(960,540,TextureFormat.RGB24,false);
            var input=new RecoveryInput();var frames=new List<HangFrame>();var held=0;var skin=Actor.Pose.GetComponentsInChildren<SkinnedMeshRenderer>().Single(x=>x.name.EndsWith("SkinnedBody"));var mesh=new Mesh();var triangles=skin.sharedMesh.triangles;var pairs=CameraGaitReviewTests.Pairs(triangles);var surface=0f;var diagnostics=new List<string>();
            for(var tick=0;tick<150;tick++)
            {
                if(Ledge.IsHolding(0)||Ledge.IsHolding(1))held++;
                if(held>0&&held<35){HoldStill();Tick();}
                else if(held>=35){Actor.Motor.MoveInput=Vector2.up;Actor.Motor.SprintRequested=true;RecoveryStep(input,hands);}else Tick();
                frames.Add(HangSnapshot(tick));Assert.That(_boundary.Out[0],Is.False);skin.BakeMesh(mesh);var angle=CameraGaitReviewTests.Surface(mesh.vertices,triangles,pairs,out var location);surface=Mathf.Max(surface,angle);
                var grabState=Actor.Hands.Grabs.Capture();Hang.RestoreBaseline();skin.BakeMesh(mesh);var baseline=CameraGaitReviewTests.Surface(mesh.vertices,triangles,pairs,out _);Actor.FixedPose.RestoreForEvaluation();
                diagnostics.Add($"tick {tick}: skin {angle:R} baseline {baseline:R} location {location:R} holding {Ledge.IsHolding(0)}/{Ledge.IsHolding(1)} lift {grabState.Left.LiftAmount:R}/{grabState.Right.LiftAmount:R} roll {Hang.AppliedRoll:R} load {Hang.LoadWeight:R} root {Actor.Motor.Body.position:R}");File.WriteAllLines(Path.Combine(folder,"SkinTrace.txt"),diagnostics);
                Actor.FixedPose.Present(1);yield return null;FrameHangingReview(camera);camera.Render();var old=RenderTexture.active;RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,960,540),0,0);image.Apply();RenderTexture.active=old;
                File.WriteAllBytes(Path.Combine(folder,$"Frame_{tick:D4}.png"),image.EncodeToPNG());
            }
            File.WriteAllText(Path.Combine(folder,"Frames.json"),JsonUtility.ToJson(new HangReport{status="PASS",frames=frames.ToArray()},true));File.WriteAllText(Path.Combine(folder,"Skin.txt"),surface.ToString("R"));
            Assert.That(surface,Is.LessThan(60));Assert.That(input.finished,Is.True);camera.targetTexture=null;rt.Release();UnityEngine.Object.Destroy(rt);UnityEngine.Object.Destroy(image);UnityEngine.Object.Destroy(mesh);
            var op=SceneManager.UnloadSceneAsync(_scene);while(op!=null&&!op.isDone)yield return null;
            }
        }
        [UnityTest] public IEnumerator WallSpacingAndExitCandidateMeasuresEachInteractionPhase()
        {
            var rows = new List<string>();
            foreach (var hands in new[] { 1, 2, 3 })
            {
                var roots = new List<Vector3>(); var signatures = new List<string>();
                foreach (var candidate in new[] { false, true })
                {
                    Create(true, true); foreach (var actor in _boundary.Arena.Actors) actor.Hands.Grabs.BoundedContactPose = candidate;
                    if(candidate)Ledge.EnableContactReview(); ReadyHold(hands); SeedFall(2, -.65f); var input = new RecoveryInput(); var held = 0;
                    var skins = Actor.Pose.GetComponentsInChildren<SkinnedMeshRenderer>(); var mesh = new Mesh();
                    var masks = new Dictionary<SkinnedMeshRenderer, bool[]>();
                    foreach (var skin in skins)
                    {
                        var arms = skin.bones.Select(b => b.name.StartsWith("Clavicle") || b.name.StartsWith("UpperArm") || b.name.StartsWith("Forearm") || b.name.StartsWith("HandLogical")).ToArray();
                        masks[skin] = skin.sharedMesh.boneWeights.Select(w => (arms[w.boneIndex0] ? w.weight0 : 0) + (arms[w.boneIndex1] ? w.weight1 : 0) + (arms[w.boneIndex2] ? w.weight2 : 0) + (arms[w.boneIndex3] ? w.weight3 : 0) > .1f).ToArray();
                    }
                    var walls = _boundary.GetComponentsInChildren<ReviewGrabSurface>().Select(s => s.Shape.bounds).ToArray();
                    var preContact = 0f; var settled = 0f; var exitDepth = 0f; var worstTick = -1; var penetration = 0f; var holdingPenetration = 0f; var physicsError = 0f; var correction = 0f;
                    for (var tick = 0; tick < 150; tick++)
                    {
                        if (Ledge.IsHolding(0) || Ledge.IsHolding(1)) held++;
                        if (held > 0 && held < 35) { HoldStill(); Tick(); }
                        else if (held >= 35) { Actor.Motor.MoveInput = Vector2.up; Actor.Motor.SprintRequested = true; RecoveryStep(input, hands); }
                        else Tick();
                        var signature = $"{Ledge.IsHolding(0)}/{Ledge.IsHolding(1)}/{Ledge.TotalAssists}/{Actor.Motor.Grounded}";
                        if (!candidate) { roots.Add(Actor.Motor.Body.position); signatures.Add(signature); }
                        else { physicsError = Mathf.Max(physicsError, Vector3.Distance(roots[tick], Actor.Motor.Body.position)); }
                        correction = Mathf.Max(correction, Actor.GrabPose.MaximumBoneCorrection);
                        foreach (var alpha in new[] { 0f, .5f, 1f })
                        {
                            if (tick == 0 && alpha < 1) continue;
                            Actor.FixedPose.Present(alpha);
                            foreach (var skin in skins)
                            {
                                skin.BakeMesh(mesh); var vertices = mesh.vertices;
                                for (var v = 0; v < vertices.Length; v++) if (masks[skin][v])
                                {
                                    var point = skin.transform.TransformPoint(vertices[v]);
                                    foreach (var box in walls) if (box.Contains(point))
                                    {
                                        var lo = point - box.min; var hi = box.max - point; var depth = Mathf.Min(lo.x, lo.y, lo.z, hi.x, hi.y, hi.z);
                                        if(depth>penetration){penetration=depth;worstTick=tick;}
                                        if(held==0)preContact=Mathf.Max(preContact,depth);
                                        if(held>20&&held<35)settled=Mathf.Max(settled,depth);
                                        if(held>=35)exitDepth=Mathf.Max(exitDepth,depth);
                                        if (held > 5 && held < 35) holdingPenetration = Mathf.Max(holdingPenetration, depth);
                                    }
                                }
                            }
                        }
                    }
                    rows.Add($"candidate={candidate}, hands={hands}, physicsError={physicsError:R}, allArmSkinPenetration={penetration:R}, worstTick={worstTick}, preContact={preContact:R}, settledHang={settled:R}, climbAndExit={exitDepth:R}, stableHangPenetration={holdingPenetration:R}, maxBoneCorrection={correction:R}, recovered={input.finished}, assists={Ledge.TotalAssists}");
                    var output = Environment.GetEnvironmentVariable("CODEX_CONTACT_OUTPUT") ?? Path.GetFullPath(Path.Combine(Application.dataPath, "../../.codex_tmp/grab-contact-bounded-20260913/Candidate")); Directory.CreateDirectory(output); File.WriteAllLines(Path.Combine(output, "WallPhases.txt"), rows);
                    Assert.That(input.finished, Is.True); Assert.That(Ledge.TotalAssists, Is.LessThanOrEqualTo(2));
                    UnityEngine.Object.Destroy(mesh); var op = SceneManager.UnloadSceneAsync(_scene); while (op != null && !op.isDone) yield return null;
                }
            }
        }
        [UnityTest] public IEnumerator WallContactCandidateIsIndependentOfRenderRate()
        {
            var poses = new List<Vector3[]>(); var states = new List<string>(); var rows = new List<string>();
            foreach (var fps in new[] { 60, 15, 144 })
            {
                Create(true, true); Ledge.EnableContactReview(); ReadyHold(3); SeedFall(2, -.65f);
                var input = new RecoveryInput(); var time = 0d; var tick = 0; var error = 0f;
                var names = Actor.Pose.GetComponentInChildren<SkinnedMeshRenderer>().bones.Select(b => b.name).ToArray();
                while (tick < 180)
                {
                    time = Math.Min(3, time + 1d / fps); var due = Math.Min(180, (int)Math.Floor(time * 60 + 1e-8));
                    while (tick < due)
                    {
                        RecoveryStep(input, 3);
                        var signature = $"{Ledge.IsHolding(0)}/{Ledge.IsHolding(1)}/{Ledge.TotalAssists}/{Actor.Motor.Grounded}";
                        var points = names.Select(n => Actor.FixedPose.WorldPosition(n)).ToArray();
                        if (fps == 60) { poses.Add(points); states.Add(signature); }
                        else { Assert.That(signature, Is.EqualTo(states[tick])); for (var i = 0; i < points.Length; i++) error = Mathf.Max(error, Vector3.Distance(points[i], poses[tick][i])); }
                        tick++;
                    }
                    Actor.FixedPose.Present((float)Math.Clamp((time - tick / 60d) * 60, 0, 1));
                }
                rows.Add($"fps={fps}, maximumBonePositionDifference={error:R}, recovered={input.finished}, assists={Ledge.TotalAssists}");
                var output = Environment.GetEnvironmentVariable("CODEX_CONTACT_OUTPUT") ?? Path.GetFullPath(Path.Combine(Application.dataPath, "../../.codex_tmp/grab-contact-bounded-20260913/Candidate"));
                Directory.CreateDirectory(output); File.WriteAllLines(Path.Combine(output, "WallFrameRates.txt"), rows);
                Assert.That(error, Is.LessThan(.00001f)); Assert.That(input.finished, Is.True);
                var op = SceneManager.UnloadSceneAsync(_scene); while (op != null && !op.isDone) yield return null;
            }
        }
        [UnityTest] public IEnumerator ContactSpacingCandidateSupportsAllFourEdgesAndRecoveryDepths()
        {
            var evidence=new List<string>();
            foreach(var y in new[]{-.60f,-1.75f,-2.18f})for(var side=0;side<4;side++)
            {
                Create(true,true);Ledge.EnableContactReview();ReadyHold(y< -1?3:1);SeedFall(side,y);var input=new RecoveryInput();var sawHigh=false;var error=0f;
                for(var tick=0;tick<360&&!_boundary.Out[0]&&!input.finished;tick++)
                {
                    RecoveryStep(input,y< -1?3:1);
                    for(var hand=0;hand<2;hand++)if(Ledge.IsHolding(hand))
                    {
                        var height=(Quaternion.Inverse(Actor.Motor.Body.rotation)*(Ledge.ContactPoint(hand)-Actor.Motor.Body.position)).y;
                        sawHigh|=height>.74f;error=Mathf.Max(error,Actor.GrabPose.MaximumHeldTargetError);
                    }
                }
                evidence.Add($"side={side}, y={y:R}, returned={input.finished}, high={sawHigh}, assists={Ledge.TotalAssists}, hand error={error:R}, release={Ledge.LastReleaseReason}");
                var output=Environment.GetEnvironmentVariable("CODEX_CONTACT_OUTPUT")??Path.GetFullPath(Path.Combine(Application.dataPath,"../../.codex_tmp/auto-lift-20260911"));Directory.CreateDirectory(output);File.WriteAllLines(Path.Combine(output,"ContactSpacingWalls.txt"),evidence);
                Assert.That(sawHigh,Is.True);Assert.That(input.finished,Is.True);Assert.That(Ledge.TotalAssists,Is.LessThanOrEqualTo(2));Assert.That(error,Is.LessThan(.005f));
                var op=SceneManager.UnloadSceneAsync(_scene);while(op!=null&&!op.isDone)yield return null;
            }
        }
        [UnityTest] public IEnumerator CosmeticClimbExitDoesNotKeepGripOrSurviveMenuCleanup()
        {
            Create(true,true);Ledge.EnableContactReview();ReadyHold(3);SeedFall(2,-.65f);Assert.That(AwaitHold(),Is.True);
            HoldStill();for(var i=0;i<20;i++)Tick();Actor.Motor.RequestJump();
            for(var i=0;i<20&&Ledge.LastReleaseReason!="ClimbHandoff";i++)Tick();
            Assert.That(Ledge.LastReleaseReason,Is.EqualTo("ClimbHandoff"));
            Assert.That(Ledge.IsHolding(0)||Ledge.IsHolding(1),Is.False);Assert.That(Actor.Hands.Grabs.ThrowCount,Is.Zero);
            var state=Actor.Hands.Grabs.Capture();Assert.That(state.Left.Active||state.Right.Active,Is.False);
            Assert.That(float.IsNaN(state.Left.SurfaceTop)&&float.IsNaN(state.Right.SurfaceTop),Is.False);
            Actor.Hands.SetInputSuppressed(true);Tick();state=Actor.Hands.Grabs.Capture();
            Assert.That(float.IsNaN(state.Left.SurfaceTop)&&float.IsNaN(state.Right.SurfaceTop),Is.True);yield return null;
        }
    }
}
