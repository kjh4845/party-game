using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace ProjectHotfix.Physics.Tests
{
    public sealed class PhysicsPackageSmokeTests
    {
        private const float PhysicsStep = 1f / 60f;
        private Scene _scene;
        private PhysicsScene _physicsScene;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _scene = SceneManager.CreateScene(
                $"FDN005-{System.Guid.NewGuid():N}",
                new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            _physicsScene = _scene.GetPhysicsScene();

            Assert.That(_physicsScene.IsValid(), Is.True);
            yield break;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            var unload = SceneManager.UnloadSceneAsync(_scene);
            while (unload != null && !unload.isDone)
            {
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator RigidbodyAndContact_SettleOnAStaticCollider()
        {
            var floor = new GameObject("ContactFloor");
            floor.transform.position = new Vector3(0f, -0.5f, 0f);
            floor.AddComponent<BoxCollider>().size = new Vector3(10f, 1f, 10f);
            SceneManager.MoveGameObjectToScene(floor, _scene);

            var bodyObject = new GameObject("ContactBody");
            bodyObject.transform.position = new Vector3(0f, 3f, 0f);
            bodyObject.AddComponent<SphereCollider>().radius = 0.5f;
            var body = bodyObject.AddComponent<Rigidbody>();
            SceneManager.MoveGameObjectToScene(bodyObject, _scene);

            Simulate(180);

            Assert.That(body.position.y, Is.InRange(0.45f, 0.65f));
            Assert.That(Mathf.Abs(body.linearVelocity.y), Is.LessThan(0.2f));
            yield break;
        }

        [UnityTest]
        public IEnumerator ConfigurableJoint_ConstrainsWhileControlBodyMoves()
        {
            var anchorObject = new GameObject("JointAnchor");
            anchorObject.transform.position = new Vector3(0f, 2f, 0f);
            SceneManager.MoveGameObjectToScene(anchorObject, _scene);
            var anchor = anchorObject.AddComponent<Rigidbody>();
            anchor.isKinematic = true;

            var jointBodyObject = new GameObject("JointBody");
            jointBodyObject.transform.position = anchorObject.transform.position;
            SceneManager.MoveGameObjectToScene(jointBodyObject, _scene);
            var jointBody = jointBodyObject.AddComponent<Rigidbody>();
            jointBody.useGravity = false;
            var joint = jointBodyObject.AddComponent<ConfigurableJoint>();
            joint.connectedBody = anchor;
            joint.autoConfigureConnectedAnchor = false;
            joint.anchor = Vector3.zero;
            joint.connectedAnchor = Vector3.zero;
            joint.xMotion = ConfigurableJointMotion.Locked;
            joint.yMotion = ConfigurableJointMotion.Locked;
            joint.zMotion = ConfigurableJointMotion.Locked;
            joint.angularXMotion = ConfigurableJointMotion.Locked;
            joint.angularYMotion = ConfigurableJointMotion.Locked;
            joint.angularZMotion = ConfigurableJointMotion.Locked;

            var controlObject = new GameObject("UnjoinedControlBody");
            controlObject.transform.position = new Vector3(0f, 10f, 0f);
            SceneManager.MoveGameObjectToScene(controlObject, _scene);
            var controlBody = controlObject.AddComponent<Rigidbody>();
            controlBody.useGravity = false;
            var controlStart = controlBody.position;

            jointBody.AddForce(Vector3.right * 10f, ForceMode.VelocityChange);
            controlBody.AddForce(Vector3.right * 10f, ForceMode.VelocityChange);

            _physicsScene.Simulate(PhysicsStep);
            Assert.That(controlBody.linearVelocity.x, Is.GreaterThan(9f));
            Assert.That(Mathf.Abs(jointBody.linearVelocity.x), Is.LessThan(0.1f));

            Simulate(119);

            Assert.That(Vector3.Distance(jointBody.position, anchor.position), Is.LessThan(0.02f));
            Assert.That(Mathf.Abs(jointBody.linearVelocity.x), Is.LessThan(0.1f));
            Assert.That(Vector3.Distance(controlBody.position, controlStart), Is.GreaterThan(5f));
            Assert.That(controlBody.linearVelocity.x, Is.GreaterThan(9f));
            yield break;
        }

        private void Simulate(int steps)
        {
            for (var step = 0; step < steps; step++)
            {
                _physicsScene.Simulate(PhysicsStep);
            }
        }
    }
}
