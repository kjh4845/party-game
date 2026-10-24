using System.Collections;
using NUnit.Framework;
using ProjectHotfix.PhysicsJump;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace ProjectHotfix.Physics.Tests
{
    public sealed class PhysicsJumpTests
    {
        private const float Dt = 1f / 60f;
        private Scene _scene;
        private PhysicsScene _physics;
        private PhysicsJumpMotor _motor;
        private GameObject _floor;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _scene = SceneManager.CreateScene("JumpTest-" + System.Guid.NewGuid(), new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            _physics = _scene.GetPhysicsScene();
            _floor = Box("Floor", new Vector3(0, -.1f, 0), new Vector3(10, .2f, 10));
            var obj = new GameObject("Character"); SceneManager.MoveGameObjectToScene(obj, _scene);
            obj.transform.position = new Vector3(0, .03f, 0);
            var capsule = obj.AddComponent<CapsuleCollider>(); capsule.height = 1f; capsule.radius = .17f; capsule.center = Vector3.up * .5f;
            var body = obj.AddComponent<Rigidbody>(); body.constraints = RigidbodyConstraints.FreezeRotation;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            _motor = obj.AddComponent<PhysicsJumpMotor>(); _motor.ManualStep = true;
            Simulate(60);
            Assert.That(_motor.Grounded, Is.True);
            yield break;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            var operation = SceneManager.UnloadSceneAsync(_scene);
            while (operation != null && !operation.isDone) yield return null;
        }

        [UnityTest]
        public IEnumerator Jump_UsesImpulseImmediatelyAndLandsOnce()
        {
            var baseline = _motor.Body.position.y; var landings = _motor.LandingCount;
            _motor.RequestJump(); Simulate(1);
            Assert.That(_motor.JumpCount, Is.EqualTo(1));
            Assert.That(_motor.Body.linearVelocity.y, Is.GreaterThan(1f));
            var maximum = _motor.Body.position.y;
            for (var i = 0; i < 90; i++) { Simulate(1); maximum = Mathf.Max(maximum, _motor.Body.position.y); }
            Assert.That(maximum-baseline, Is.InRange(.20f, .29f));
            Assert.That(_motor.Grounded, Is.True);
            Assert.That(_motor.LandingCount, Is.EqualTo(landings+1));
            Assert.That(_motor.PresentationState.LandingImpactSpeed, Is.GreaterThan(1.5f));
            yield break;
        }

        [UnityTest]
        public IEnumerator MidairRequest_DoesNotDoubleJumpOrRemainBufferedForever()
        {
            _motor.RequestJump(); Simulate(5); _motor.RequestJump(); Simulate(90);
            Assert.That(_motor.JumpCount, Is.EqualTo(1));
            Assert.That(_motor.Grounded, Is.True);
            yield break;
        }

        [UnityTest]
        public IEnumerator PressJustBeforeLanding_IsBufferedUntilActualContact()
        {
            _motor.ResetAt(new Vector3(0, .08f, 0)); _motor.Body.linearVelocity = Vector3.down;
            _motor.RequestJump(); Simulate(1); Assert.That(_motor.JumpCount, Is.Zero);
            Simulate(15);
            Assert.That(_motor.JumpCount, Is.EqualTo(1));
            Assert.That(_motor.Body.position.y, Is.GreaterThan(.1f));
            yield break;
        }

        [UnityTest]
        public IEnumerator Coyote_AllowsOneRecentEdgeJumpButExpires()
        {
            _floor.SetActive(false); Simulate(3); _motor.RequestJump(); Simulate(1);
            Assert.That(_motor.JumpCount, Is.EqualTo(1));
            _motor.RequestJump(); Simulate(1); Assert.That(_motor.JumpCount, Is.EqualTo(1));
            _floor.SetActive(true); _motor.ResetAt(new Vector3(0, .03f, 0)); Simulate(60);
            _floor.SetActive(false); Simulate(15); _motor.RequestJump(); Simulate(1);
            Assert.That(_motor.JumpCount, Is.Zero);
            yield break;
        }

        [UnityTest]
        public IEnumerator CeilingContact_DoesNotRearmJump()
        {
            Box("Ceiling", new Vector3(0, 1.1f, 0), new Vector3(4, .1f, 4));
            _motor.RequestJump(); Simulate(2); _motor.RequestJump(); Simulate(1);
            Assert.That(_motor.JumpCount, Is.EqualTo(1));
            Assert.That(_motor.Body.position.y, Is.LessThan(.09f));
            yield break;
        }

        [UnityTest]
        public IEnumerator WallContact_DoesNotCountAsGround()
        {
            _floor.SetActive(false); _motor.ResetAt(new Vector3(0, 2, 0));
            Box("Wall", new Vector3(.25f, 2, 0), new Vector3(.2f, 10, 4));
            Simulate(12); _motor.RequestJump(); Simulate(1);
            Assert.That(_motor.Grounded, Is.False); Assert.That(_motor.JumpCount, Is.Zero);
            yield break;
        }

        [UnityTest]
        public IEnumerator HigherFall_ProducesLargerMeasuredLandingImpact()
        {
            _motor.ResetAt(new Vector3(0, .25f, 0)); Simulate(120);
            var low = _motor.PresentationState.LandingImpactSpeed;
            _motor.ResetAt(new Vector3(0, 1f, 0)); Simulate(120);
            Assert.That(_motor.PresentationState.LandingImpactSpeed, Is.GreaterThan(low+1f));
            yield break;
        }

        [UnityTest]
        public IEnumerator AuthorityDisableAndReset_ClearPendingInput()
        {
            _motor.RequestJump(); _motor.AuthorityEnabled = false; Simulate(1);
            _motor.AuthorityEnabled = true; Simulate(2); Assert.That(_motor.JumpCount, Is.Zero);
            _motor.RequestJump(); _motor.ResetAt(new Vector3(0, .03f, 0)); Simulate(30);
            Assert.That(_motor.JumpCount, Is.Zero);
            yield break;
        }

        [UnityTest]
        public IEnumerator SlopeContact_RespectsWalkableAngleAndDoesNotDoubleJump()
        {
            _floor.transform.rotation = Quaternion.Euler(0, 0, 20);
            _motor.ResetAt(new Vector3(0, .3f, 0)); Simulate(90);
            Assert.That(_motor.Grounded, Is.True);
            _motor.RequestJump(); Simulate(2); _motor.RequestJump(); Simulate(2);
            Assert.That(_motor.JumpCount, Is.EqualTo(1));
            _floor.transform.rotation = Quaternion.Euler(0, 0, 65);
            _motor.ResetAt(new Vector3(0, .3f, 0)); Simulate(15);
            _motor.RequestJump(); Simulate(1);
            Assert.That(_motor.JumpCount, Is.Zero);
            yield break;
        }

        private GameObject Box(string name, Vector3 position, Vector3 size)
        {
            var obj = new GameObject(name); SceneManager.MoveGameObjectToScene(obj, _scene);
            obj.transform.position = position; obj.AddComponent<BoxCollider>().size = size;
            return obj;
        }

        private void Simulate(int count)
        {
            for (var i = 0; i < count; i++) { _motor.Step(Dt); _physics.Simulate(Dt); }
        }
    }
}
