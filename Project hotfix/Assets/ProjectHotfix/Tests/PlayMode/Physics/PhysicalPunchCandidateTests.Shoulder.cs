using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace ProjectHotfix.Physics.Tests
{
    public sealed partial class PhysicalPunchCandidateTests
    {
        private IEnumerator ShoulderPhoto(string file, Vector3 offset)
        {
            _pair.Present(1);
            var root = _pair.Players[0].Rig.Actor.Motor.Body;
            var center = root.position + Vector3.up * .49f;
            _pair.View.transform.SetPositionAndRotation(center + offset, Quaternion.LookRotation(-offset));
            var others = _pair.Players[1].Rig.Actor.Pose.GetComponentsInChildren<Renderer>();
            var wasEnabled = others.Select(renderer => renderer.enabled).ToArray();
            foreach (var renderer in others) renderer.enabled = false;
            yield return null;
            var rt = new RenderTexture(800, 1000, 24);
            rt.Create();
            var photo = new Texture2D(800, 1000, TextureFormat.RGB24, false);
            _pair.View.targetTexture = rt;
            _pair.View.aspect = .8f;
            _pair.View.orthographicSize = .62f;
            _pair.View.Render();
            var previous = RenderTexture.active;
            RenderTexture.active = rt;
            photo.ReadPixels(new Rect(0, 0, 800, 1000), 0, 0);
            photo.Apply();
            RenderTexture.active = previous;
            File.WriteAllBytes(Path.Combine(Output, file + ".png"), photo.EncodeToPNG());
            _pair.View.targetTexture = null;
            rt.Release();
            UnityEngine.Object.Destroy(rt);
            UnityEngine.Object.Destroy(photo);
            for(var i=0;i<others.Length;i++)others[i].enabled=wasEnabled[i];
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator CaptureShoulderFrontAndPunchPoses()
        {
            Create(.7f);
            var rig = _pair.Players[0].Rig;
            var skin = rig.Actor.Pose.GetComponentsInChildren<SkinnedMeshRenderer>()
                .Single(renderer => renderer.name.EndsWith("SkinnedBody"));
            _pair.Present(1);
            using (var writer = new BinaryWriter(File.Create(Path.Combine(Output, "IdleSkinMatrices.bin"))))
            {
                writer.Write(skin.bones.Length);
                for (var bone = 0; bone < skin.bones.Length; bone++)
                {
                    var matrix = skin.transform.worldToLocalMatrix * skin.bones[bone].localToWorldMatrix * skin.sharedMesh.bindposes[bone];
                    for (var row = 0; row < 4; row++)
                        for (var column = 0; column < 4; column++) writer.Write(matrix[row, column]);
                }
            }
            yield return ShoulderPhoto("Idle_Front", new Vector3(0, 0, 3));
            yield return ShoulderPhoto("Idle_ThreeQuarter", new Vector3(-2, .25f, 3));
            yield return ShoulderPhoto("Idle_Side", new Vector3(-3, 0, 0));
            var baked = new Mesh();
            var triangles = skin.sharedMesh.triangles;
            var pairs = CameraGaitReviewTests.Pairs(triangles);
            var maximumSurfaceAngle = 0f;
            Tap(0, 0);
            for (var tick = 0; tick < 30; tick++)
            {
                Tick();
                skin.BakeMesh(baked);
                var surfaceAngle=CameraGaitReviewTests.Surface(baked.vertices, triangles, pairs, out _);
                if(surfaceAngle>maximumSurfaceAngle){maximumSurfaceAngle=surfaceAngle;ExportSurfacePose(skin,"Shoulder_Worst");}
                if (tick == 2 || tick == 5 || tick == 8 || tick == 12 || tick == 18)
                {
                    ExportSurfacePose(skin,"Punch_"+tick.ToString("D2"));
                    yield return ShoulderPhoto("Punch_Front_" + tick.ToString("D2"), new Vector3(0, 0, 3));
                    yield return ShoulderPhoto("Punch_Side_" + tick.ToString("D2"), new Vector3(-3, 0, 0));
                }
            }
            UnityEngine.Object.Destroy(baked);
            File.WriteAllText(Path.Combine(Output, "ShoulderSurface.txt"), "maximumAdjacentFaceAngle=" + maximumSurfaceAngle);
            Assert.That(maximumSurfaceAngle, Is.LessThan(60), "Shoulder edits must remain smooth through the physical strike");
            Assert.That(_pair.PhysicalPunches.Hits.Count, Is.Zero, "Isolated model capture is outside punching range");
        }

        [UnityTest]
        public IEnumerator CombatGuardEndsWithMenuAndIdleTimeout()
        {
            foreach (var menu in new[] { false, true })
            {
                Create(.7f);
                Tap(0, 0);
                for (var tick = 0; tick < 30; tick++) Tick();
                var rig = _pair.Players[0].Rig;
                Assert.That(_pair.PhysicalPunches.IsPunching(0, 0), Is.False);
                Assert.That(Quaternion.Angle(rig.Find("Forearm_R").Joint.targetRotation, Quaternion.identity), Is.GreaterThan(50));
                Assert.That(_pair.PhysicalPunches.Hits.Count, Is.Zero);
                if (menu) _pair.SetSuppressed(true);
                else for (var tick = 0; tick < 75; tick++) Tick();
                foreach (var name in new[] { "UpperArm_L", "Forearm_L", "UpperArm_R", "Forearm_R" })
                {
                    var link = rig.Find(name);
                    Assert.That(Quaternion.Angle(link.Joint.targetRotation, Quaternion.identity), Is.LessThan(.01f));
                    Assert.That(link.Joint.slerpDrive.positionSpring, Is.EqualTo(link.Drive.positionSpring));
                }
                var operation = UnityEngine.SceneManagement.SceneManager.UnloadSceneAsync(_scene);
                while (!operation.isDone) yield return null;
            }
        }

        [UnityTest]
        public IEnumerator PreviousWallBlockDoesNotDisableOppositePunchGuard()
        {
            Create(.38f, 0, true);
            Tap(0, 0);
            for (var tick = 0; tick < 30; tick++) Tick();
            Assert.That(_pair.PhysicalPunches.BlockedContacts, Is.GreaterThan(0));
            Assert.That(_pair.PhysicalPunches.IsPunching(0, 0), Is.False);
            Tap(0, 1);
            for (var tick = 0; tick < 8; tick++) Tick();
            var idleHand = _pair.Players[0].Rig.Find("Forearm_L");
            Assert.That(Quaternion.Angle(idleHand.Joint.targetRotation, Quaternion.identity), Is.GreaterThan(50));
            Assert.That(_pair.PhysicalPunches.Hits.Count, Is.Zero, "A wall block never scores");
            yield return null;
        }
    }
}
