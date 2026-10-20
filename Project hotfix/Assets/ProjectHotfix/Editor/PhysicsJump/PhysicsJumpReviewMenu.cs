using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ProjectHotfix.PhysicsJump.Editor
{
    public static class PhysicsJumpReviewMenu
    {
        [MenuItem("Project Hotfix/Prototypes/Open Active Upper Body Stage 1")]
        public static void OpenActiveUpperBodyStageOne()
        {
            if(!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())return;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var review=ActiveUpperBodyReview.Create(SceneManager.GetActiveScene());Selection.activeGameObject=review.gameObject;
        }
        [MenuItem("Project Hotfix/Prototypes/Open Active Grab Stage 2")]
        public static void OpenActiveGrabStageTwo()=>OpenGrabMotion(ActiveGrabPairReview.GrabMotion.AdaptiveReach);
        [MenuItem("Project Hotfix/Prototypes/Open Previous Active Grab Comparison")]
        public static void OpenPreviousActiveGrab()=>OpenGrabMotion(ActiveGrabPairReview.GrabMotion.Original);
        [MenuItem("Project Hotfix/Prototypes/Open Forward Grab Candidate")]
        public static void OpenForwardGrab()=>OpenGrabMotion(ActiveGrabPairReview.GrabMotion.ForwardReach);
        [MenuItem("Project Hotfix/Prototypes/Open Adaptive Grab Candidate")]
        public static void OpenAdaptiveGrab()=>OpenGrabMotion(ActiveGrabPairReview.GrabMotion.AdaptiveReach);
        [MenuItem("Project Hotfix/Prototypes/Open Shoulder Repair Candidate")]
        public static void OpenShoulderRepair()=>OpenRepairedGrab(false,LimpGrabTargetReview.Orientation.FaceUp);
        [MenuItem("Project Hotfix/Prototypes/Low Ragdoll Grab/Face Up")]
        public static void OpenLowFaceUp()=>OpenRepairedGrab(true,LimpGrabTargetReview.Orientation.FaceUp);
        [MenuItem("Project Hotfix/Prototypes/Low Ragdoll Grab/Face Down")]
        public static void OpenLowFaceDown()=>OpenRepairedGrab(true,LimpGrabTargetReview.Orientation.FaceDown);
        [MenuItem("Project Hotfix/Prototypes/Low Ragdoll Grab/Left Side")]
        public static void OpenLowLeft()=>OpenRepairedGrab(true,LimpGrabTargetReview.Orientation.LeftSide);
        [MenuItem("Project Hotfix/Prototypes/Low Ragdoll Grab/Right Side")]
        public static void OpenLowRight()=>OpenRepairedGrab(true,LimpGrabTargetReview.Orientation.RightSide);
        [MenuItem("Project Hotfix/Prototypes/Limp Lift Candidate/Face Up")]
        public static void OpenLimpLiftUp()=>OpenRepairedGrab(true,LimpGrabTargetReview.Orientation.FaceUp,true);
        [MenuItem("Project Hotfix/Prototypes/Limp Lift Candidate/Face Down")]
        public static void OpenLimpLiftDown()=>OpenRepairedGrab(true,LimpGrabTargetReview.Orientation.FaceDown,true);
        [MenuItem("Project Hotfix/Prototypes/Limp Lift Candidate/Left Side")]
        public static void OpenLimpLiftLeft()=>OpenRepairedGrab(true,LimpGrabTargetReview.Orientation.LeftSide,true);
        [MenuItem("Project Hotfix/Prototypes/Limp Lift Candidate/Right Side")]
        public static void OpenLimpLiftRight()=>OpenRepairedGrab(true,LimpGrabTargetReview.Orientation.RightSide,true);
        [MenuItem("Project Hotfix/Prototypes/Skin Contact Candidate/Face Up")]
        public static void OpenSkinContactUp()=>OpenRepairedGrab(true,LimpGrabTargetReview.Orientation.FaceUp,true,true);
        [MenuItem("Project Hotfix/Prototypes/Skin Contact Candidate/Face Down")]
        public static void OpenSkinContactDown()=>OpenRepairedGrab(true,LimpGrabTargetReview.Orientation.FaceDown,true,true);
        [MenuItem("Project Hotfix/Prototypes/Skin Contact Candidate/Left Side")]
        public static void OpenSkinContactLeft()=>OpenRepairedGrab(true,LimpGrabTargetReview.Orientation.LeftSide,true,true);
        [MenuItem("Project Hotfix/Prototypes/Skin Contact Candidate/Right Side")]
        public static void OpenSkinContactRight()=>OpenRepairedGrab(true,LimpGrabTargetReview.Orientation.RightSide,true,true);
        [MenuItem("Project Hotfix/Prototypes/Skin Contact Candidate/Drag")]
        public static void OpenSkinContactDrag()=>OpenRepairedGrab(true,LimpGrabTargetReview.Orientation.FaceUp,false,true);
        [MenuItem("Project Hotfix/Prototypes/Standing Skin Contact/Lift and Release")]
        public static void OpenStandingSkinContact()=>OpenStandingContact(true);
        [MenuItem("Project Hotfix/Prototypes/Standing Skin Contact/Contact Comparison Only")]
        public static void OpenStandingSkinComparison()=>OpenStandingContact(false);
        [MenuItem("Project Hotfix/Prototypes/Open Physical Punch Candidate")]
        public static void OpenPhysicalPunchCandidate()=>OpenArcStrike(ActiveUpperBodyReview.ReviewStrikeStyle.BodyStrike);
        [MenuItem("Project Hotfix/Prototypes/Open Body Impact Candidate")]
        public static void OpenBodyImpactCandidate()=>OpenArcStrike(ActiveUpperBodyReview.ReviewStrikeStyle.BodyStrike);
        // The slim C+ body (C1B-RW-046, 2026-10-17) is the default; the thicker r43 stays for comparison.
        [MenuItem("Project Hotfix/Prototypes/Open Previous R43 Thick Body Comparison")]
        public static void OpenPreviousR43Body()=>OpenArcStrike(ActiveUpperBodyReview.ReviewStrikeStyle.BodyStrike,"C1B-RW-043-gb-body/R43Character.bytes");
        [MenuItem("Project Hotfix/Prototypes/Open Previous R40 Body Comparison")]
        public static void OpenPreviousR40Body()=>OpenArcStrike(ActiveUpperBodyReview.ReviewStrikeStyle.BodyStrike,"C1B-RW-040-cage-body/CageBodyCharacter.bytes");
        [MenuItem("Project Hotfix/Prototypes/Arc Strike/A - Side Swing")]
        public static void OpenSideSwingCandidate()=>OpenArcStrike(ActiveUpperBodyReview.ReviewStrikeStyle.SideSwing);
        [MenuItem("Project Hotfix/Prototypes/Arc Strike/B - Diagonal Slam")]
        public static void OpenDiagonalSlamCandidate()=>OpenArcStrike(ActiveUpperBodyReview.ReviewStrikeStyle.DiagonalSlam);
        private static void OpenArcStrike(ActiveUpperBodyReview.ReviewStrikeStyle style,string bodyStrikeModel="C1B-RW-046-slim-body/R43Character.bytes")
        {
            if(!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())return;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var skin=System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath,"../../BlenderSource/Characters/C1B-RW-023-shallow-axilla/ShallowCharacter.bytes"));
            if(style==ActiveUpperBodyReview.ReviewStrikeStyle.BodyStrike)
                skin=System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath,"../../BlenderSource/Characters/"+bodyStrikeModel));
            var review=ActiveGrabPairReview.Create(SceneManager.GetActiveScene(),.5f,ActiveGrabPairReview.GrabMotion.AdaptiveReach,skin,matchHandEnvelope:true);
            review.EnableStandingSkinContacts=review.EnableLiftThrow=review.EnablePhysicalPunch=true;review.UseStrikeStyle(style);review.UseFightThrows();
            Selection.activeGameObject=review.gameObject;
        }
        [MenuItem("Project Hotfix/Prototypes/Open Punch With Previous Surface")]
        public static void OpenPunchPreviousSurface()=>OpenStandingContact(true,true);
        private static void OpenStandingContact(bool lift,bool punch=false,bool updatedSurface=false)
        {
            if(!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())return;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var skin=System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath,"../../.codex_tmp/shoulder-reach-20260916/candidate-r02/ShoulderCharacter.bytes"));
            if(updatedSurface)skin=System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath,"../../BlenderSource/Characters/C1B-RW-022-shoulder-contour/ShoulderCharacter.bytes"));
            var review=ActiveGrabPairReview.Create(SceneManager.GetActiveScene(),.65f,ActiveGrabPairReview.GrabMotion.AdaptiveReach,skin,matchHandEnvelope:true);
            review.EnableStandingSkinContacts=true;review.EnableLiftThrow=lift;review.EnablePhysicalPunch=punch;Selection.activeGameObject=review.gameObject;
        }
        private static void OpenRepairedGrab(bool low,LimpGrabTargetReview.Orientation orientation,bool lift=false,bool contacts=false)
        {
            if(!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())return;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var skin=System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath,"../../.codex_tmp/shoulder-reach-20260916/candidate-r02/ShoulderCharacter.bytes"));
            var review=ActiveGrabPairReview.Create(SceneManager.GetActiveScene(),low?.5f:.65f,ActiveGrabPairReview.GrabMotion.AdaptiveReach,skin,low,orientation,true);review.EnableLiftThrow=lift;review.EnableSkinContacts=contacts;Selection.activeGameObject=review.gameObject;
        }
        private static void OpenGrabMotion(ActiveGrabPairReview.GrabMotion motion)
        {
            if(!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())return;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var review=ActiveGrabPairReview.Create(SceneManager.GetActiveScene(),.65f,motion);Selection.activeGameObject=review.gameObject;
        }
        [MenuItem("Project Hotfix/Prototypes/Open Physics Jump Review")]
        public static void Open() => OpenReview(true);

        [MenuItem("Project Hotfix/Prototypes/Open Foot Plant Candidate")]
        public static void OpenFootPlant() => OpenReview(true);

        [MenuItem("Project Hotfix/Prototypes/Open Previous Gait Comparison")]
        public static void OpenPreviousGait() => OpenReview(false);

        [MenuItem("Project Hotfix/Prototypes/Open Hand Strike Review")]
        public static void OpenHandStrike()
        {
            if (!OpenReview(true)) return;
            var actor = UnityEngine.Object.FindFirstObjectByType<PhysicsJumpReview>(); actor.EnableHandStrikeReview();
            var target = PhysicsJumpReview.Create(new Vector3(0f, .02f, .52f));
            target.ReadKeyboard = false; target.enabled = false; target.Motor.AuthorityEnabled = false;
            foreach (var skin in target.GetComponentsInChildren<SkinnedMeshRenderer>()) skin.sharedMaterial.SetColor("_BaseColor", new Color(.92f, .48f, .16f));
            actor.Hands.ReviewTargets = new[] { target.gameObject.AddComponent<StrikeReviewTarget>() };
        }

        [MenuItem("Project Hotfix/Prototypes/Open Grab Physics Review")]
        public static void OpenGrab()
        {
            if (!OpenReview(true)) return;
            var actor = UnityEngine.Object.FindFirstObjectByType<PhysicsJumpReview>(); actor.EnableGrabReview();
            var target = PhysicsJumpReview.Create(new Vector3(0, .02f, .46f));
            target.ReadKeyboard = false; target.enabled = false; target.Motor.AuthorityEnabled = false;
            foreach (var skin in target.GetComponentsInChildren<SkinnedMeshRenderer>()) skin.sharedMaterial.SetColor("_BaseColor", new Color(.92f, .48f, .16f));
            var body = target.gameObject.AddComponent<StrikeReviewTarget>(); body.GrabEligible = true; actor.Hands.ReviewTargets = new[] { body };
        }

        [MenuItem("Project Hotfix/Prototypes/Open Ragdoll Recovery Candidate")]
        public static void OpenRagdoll()
        {
            if (!OpenReview(true)) return;
            var actor = UnityEngine.Object.FindFirstObjectByType<PhysicsJumpReview>();
            actor.gameObject.AddComponent<RagdollRecoveryReview>(); // Bind at runtime Start, after scene/domain reload.
        }

        [MenuItem("Project Hotfix/Prototypes/Open Punch Down Review")]
        public static void OpenPunchDown() => CreatePunchDownReview(false);

        [MenuItem("Project Hotfix/Prototypes/Open Articulated Punch Candidate")]
        public static void OpenArticulatedPunch() => CreatePunchDownReview(true, 1.4, PhysicsJumpReview.LatWeightTransportPath);

        [MenuItem("Project Hotfix/Prototypes/Open Previous Articulated Punch Comparison")]
        public static void OpenPreviousArticulatedPunch() => CreatePunchDownReview(true);

        [MenuItem("Project Hotfix/Prototypes/Open Fast Punch Candidate")]
        public static void OpenFastPunch() => CreatePunchDownReview(true, 1.4, PhysicsJumpReview.LatWeightTransportPath);

        [MenuItem("Project Hotfix/Prototypes/Open Shared Arena 2 Players")]
        public static void OpenSharedTwo() => OpenLedge(2);
        [MenuItem("Project Hotfix/Prototypes/Open Shared Arena 3 Players")]
        public static void OpenSharedThree() => OpenLedge(3);
        [MenuItem("Project Hotfix/Prototypes/Open Shared Arena 4 Players")]
        public static void OpenSharedFour() => OpenLedge(4);
        [MenuItem("Project Hotfix/Prototypes/Open Previous Shared Gait 2 Players")]
        public static void OpenPreviousSharedGaitTwo() => OpenShared(2,false);
        [MenuItem("Project Hotfix/Prototypes/Open Previous Shared Gait 3 Players")]
        public static void OpenPreviousSharedGaitThree() => OpenShared(3,false);
        [MenuItem("Project Hotfix/Prototypes/Open Previous Shared Gait 4 Players")]
        public static void OpenPreviousSharedGaitFour() => OpenShared(4,false);
        [MenuItem("Project Hotfix/Prototypes/Open Previous Movement Speed Review")]
        public static void OpenPreviousMovementSpeed() => OpenShared(4,true,1f);
        [MenuItem("Project Hotfix/Prototypes/Open Movement Speed 1.5x Review")]
        public static void OpenMovementSpeedOneAndHalf() => OpenShared(4,true,1.5f);
        [MenuItem("Project Hotfix/Prototypes/Open Movement Speed 2x Review")]
        public static void OpenMovementSpeedTwo() => OpenShared(4,true,2f);
        private static void OpenShared(int count,bool cameraGaitCandidate=true,float movementScale=SharedArenaReview.DefaultMovementScale)
        {
            if(!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())return;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var review=SharedArenaReview.Create(SceneManager.GetActiveScene(),count,cameraGaitCandidate,movementScale);Selection.activeGameObject=review.gameObject;
        }

        [MenuItem("Project Hotfix/Prototypes/Open Arena Boundary Review 2 Players")]
        public static void OpenBoundaryTwo()=>OpenBoundary(2);
        [MenuItem("Project Hotfix/Prototypes/Open Arena Boundary Review 3 Players")]
        public static void OpenBoundaryThree()=>OpenBoundary(3);
        [MenuItem("Project Hotfix/Prototypes/Open Arena Boundary Review 4 Players")]
        public static void OpenBoundaryFour()=>OpenBoundary(4);
        private static void OpenBoundary(int count)
        {
            if(!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())return;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var review=ArenaBoundaryReview.Create(SceneManager.GetActiveScene(),count);Selection.activeGameObject=review.gameObject;
        }

        [MenuItem("Project Hotfix/Prototypes/Open Ledge Recovery Review")]
        public static void OpenLedgeRecovery()=>OpenLedge(2);
        [MenuItem("Project Hotfix/Prototypes/Open Grab Contact Candidate 2 Players")]
        public static void OpenGrabContactCandidate()
        {
            if(!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())return;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var review=LedgeRecoveryReview.Create(SceneManager.GetActiveScene(),2);
            foreach(var actor in review.Arena.Actors)actor.GetComponent<LedgeGrabReview>().EnableContactReview();
            Selection.activeGameObject=review.gameObject;
        }
        private static void OpenLedge(int count)
        {
            if(!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())return;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var review=LedgeRecoveryReview.Create(SceneManager.GetActiveScene(),count);Selection.activeGameObject=review.gameObject;
        }

        [MenuItem("Project Hotfix/Prototypes/Open Lat Weight Candidate")]
        public static void OpenLatWeight() => CreatePunchDownReview(true, 1.4, PhysicsJumpReview.LatWeightTransportPath);

        private static void CreatePunchDownReview(bool articulated, double punchRate = 1, string skinTransportPath = null)
        {
            if (!OpenReview(true, skinTransportPath)) return;
            var actor = UnityEngine.Object.FindFirstObjectByType<PhysicsJumpReview>(); actor.EnableGrabReview();
            actor.Hands.ConfigurePunchTiming(punchRate);
            actor.Hands.PreciseHits=punchRate>1;
            var target = PhysicsJumpReview.Create(new Vector3(0, .02f, .46f), true, skinTransportPath); target.ReadKeyboard = false;
            foreach (var skin in target.GetComponentsInChildren<SkinnedMeshRenderer>()) skin.sharedMaterial.SetColor("_BaseColor", new Color(.92f, .48f, .16f));
            var targetBody = target.gameObject.AddComponent<StrikeReviewTarget>(); targetBody.GrabEligible = true;
            if (articulated) target.gameObject.AddComponent<RagdollRecoveryReview>().ArticulatedTorso = true;
            var receiver=target.gameObject.AddComponent<PunchDownReview>();
            if(punchRate>1){receiver.RegionPointCandidate=true;receiver.PointDecreasePerDown=1;receiver.MinimumPointThreshold=6;}
            actor.Hands.ReviewTargets = new[] { targetBody };
        }

        [MenuItem("Project Hotfix/Prototypes/Open Articulated Torso Candidate")]
        public static void OpenArticulatedTorso()
        {
            if (!OpenReview(true)) return;
            var actor = UnityEngine.Object.FindFirstObjectByType<PhysicsJumpReview>();
            actor.gameObject.AddComponent<RagdollRecoveryReview>().ArticulatedTorso = true;
        }

        private static bool OpenReview(bool footPlant, string skinTransportPath = null)
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return false;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            PhysicsJumpReview.Create(new Vector3(0f, .02f, 0f), footPlant, skinTransportPath);
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); floor.name = "ReviewFloor";
            floor.transform.position = new Vector3(0f, -.1f, 0f); floor.transform.localScale = new Vector3(12f, .2f, 12f);
            var camera = new GameObject("ReviewCamera").AddComponent<Camera>(); camera.tag = "MainCamera";
            camera.transform.position = new Vector3(2.5f, 1.8f, 3.8f); camera.transform.LookAt(new Vector3(0f, .65f, 0f));
            camera.orthographic = true; camera.orthographicSize = 1.3f;
            var light = new GameObject("ReviewLight").AddComponent<Light>(); light.type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(45f, -35f, 0f); light.intensity = 2f;
            Selection.activeGameObject = camera.gameObject;
            return true;
        }
    }
}
