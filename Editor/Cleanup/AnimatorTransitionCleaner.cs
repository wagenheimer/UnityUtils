using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Wagenheimer.UnityUtils.Editor
{
    /// <summary>
    /// Scans Animator Controller assets for invalid state transitions (such as AnyState transitions
    /// with no conditions and hasExitTime == false) that Unity ignores while spamming console warnings
    /// on scene load: "Transition 'AnyState -> X' in state 'AnyState' doesn't have an Exit Time or any condition..."
    /// </summary>
    public static class AnimatorTransitionCleaner
    {
        public struct InvalidTransitionInfo
        {
            public string AssetPath;
            public string LayerName;
            public string SourceName;
            public string DestinationName;
            public AnimatorStateTransition Transition;
            public AnimatorStateMachine StateMachine;
            public AnimatorState State;
            public bool IsAnyState;
        }

        public struct ScanResult
        {
            public int InvalidTransitionCount;
            public int AffectedControllersCount;
            public List<InvalidTransitionInfo> InvalidTransitions;
            public List<string> Details;
        }

        #region Menu Items

        [MenuItem("Tools/Wagenheimer/Unity Utils/Cleanup/Animator Controllers/Audit Invalid Animator Transitions", priority = 40)]
        public static void AuditInvalidTransitions()
        {
            var scan = ScanProject();
            if (scan.InvalidTransitionCount == 0)
            {
                EditorUtility.DisplayDialog("Animator Transition Audit",
                    "No invalid animator transitions found! All Animator Controllers are healthy.", "OK");
                return;
            }

            Debug.LogWarning($"[AnimatorTransitionCleaner] Found {scan.InvalidTransitionCount} invalid transition(s) across {scan.AffectedControllersCount} controller(s):");
            foreach (var detail in scan.Details)
            {
                Debug.LogWarning($"  • {detail}");
            }

            EditorUtility.DisplayDialog("Animator Transition Audit",
                $"Found {scan.InvalidTransitionCount} invalid transition(s) across {scan.AffectedControllersCount} controller(s).\n\n" +
                "Check the Console for a full breakdown, or use 'Fix Invalid Animator Transitions' to resolve them.", "OK");
        }

        [MenuItem("Tools/Wagenheimer/Unity Utils/Cleanup/Animator Controllers/Fix Invalid Transitions (Enable Exit Time)", priority = 41)]
        public static void FixInvalidTransitionsEnableExitTime()
        {
            if (!EditorUtility.DisplayDialog("Fix Invalid Transitions",
                    "This will scan all Animator Controllers and enable 'Has Exit Time' on transitions that " +
                    "have no conditions, fixing the 'transition will be ignored' warning.\n\n" +
                    "Make sure your work is saved before proceeding.", "Proceed", "Cancel"))
                return;

            var fixedCount = FixAll(FixMode.EnableExitTime);
            var msg = $"Fixed {fixedCount} invalid animator transition(s) by enabling Exit Time.";
            Debug.Log($"[AnimatorTransitionCleaner] {msg}");
            EditorUtility.DisplayDialog("Fix Invalid Transitions", msg, "OK");
        }

        [MenuItem("Tools/Wagenheimer/Unity Utils/Cleanup/Animator Controllers/Remove Invalid Transitions (Delete Dead Transitions)", priority = 42)]
        public static void FixInvalidTransitionsRemove()
        {
            if (!EditorUtility.DisplayDialog("Remove Invalid Transitions",
                    "This will scan all Animator Controllers and DELETE any transitions that have no conditions " +
                    "and no Exit Time (dead transitions).\n\n" +
                    "Make sure your work is saved before proceeding.", "Proceed", "Cancel"))
                return;

            var removedCount = FixAll(FixMode.Remove);
            var msg = $"Removed {removedCount} invalid animator transition(s).";
            Debug.Log($"[AnimatorTransitionCleaner] {msg}");
            EditorUtility.DisplayDialog("Remove Invalid Transitions", msg, "OK");
        }

        #endregion

        #region Core Scan & Fix Logic

        public enum FixMode
        {
            EnableExitTime,
            Remove
        }

        public static ScanResult ScanProject()
        {
            var result = new ScanResult
            {
                InvalidTransitions = new List<InvalidTransitionInfo>(),
                Details = new List<string>()
            };

            var controllerGuids = AssetDatabase.FindAssets("t:AnimatorController");
            var controllersWithIssues = new HashSet<string>();

            for (int i = 0; i < controllerGuids.Length; i++)
            {
                var path = AssetDatabase.GUIDToAssetPath(controllerGuids[i]);
                var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
                if (controller == null) continue;

                foreach (var layer in controller.layers)
                {
                    if (layer.stateMachine == null) continue;
                    CheckStateMachine(controller, path, layer.name, layer.stateMachine, result, controllersWithIssues);
                }
            }

            result.InvalidTransitionCount = result.InvalidTransitions.Count;
            result.AffectedControllersCount = controllersWithIssues.Count;
            return result;
        }

        private static void CheckStateMachine(
            AnimatorController controller,
            string assetPath,
            string layerName,
            AnimatorStateMachine sm,
            ScanResult result,
            HashSet<string> controllersWithIssues)
        {
            if (sm == null) return;

            // 1. Check AnyState transitions
            foreach (var transition in sm.anyStateTransitions)
            {
                if (IsTransitionInvalid(transition))
                {
                    var destName = transition.destinationState != null
                        ? transition.destinationState.name
                        : (transition.destinationStateMachine != null ? transition.destinationStateMachine.name : "<null>");

                    result.InvalidTransitions.Add(new InvalidTransitionInfo
                    {
                        AssetPath = assetPath,
                        LayerName = layerName,
                        SourceName = "AnyState",
                        DestinationName = destName,
                        Transition = transition,
                        StateMachine = sm,
                        IsAnyState = true
                    });

                    controllersWithIssues.Add(assetPath);
                    result.Details.Add($"{assetPath} [{layerName}]: AnyState -> {destName} has no conditions and no Exit Time");
                }
            }

            // 2. Check individual states in this state machine
            foreach (var childState in sm.states)
            {
                var state = childState.state;
                if (state == null) continue;

                foreach (var transition in state.transitions)
                {
                    if (IsTransitionInvalid(transition))
                    {
                        var destName = transition.destinationState != null
                            ? transition.destinationState.name
                            : (transition.destinationStateMachine != null ? transition.destinationStateMachine.name : "<null>");

                        result.InvalidTransitions.Add(new InvalidTransitionInfo
                        {
                            AssetPath = assetPath,
                            LayerName = layerName,
                            SourceName = state.name,
                            DestinationName = destName,
                            Transition = transition,
                            StateMachine = sm,
                            State = state,
                            IsAnyState = false
                        });

                        controllersWithIssues.Add(assetPath);
                        result.Details.Add($"{assetPath} [{layerName}]: {state.name} -> {destName} has no conditions and no Exit Time");
                    }
                }
            }

            // 3. Recursive sub-state machines
            foreach (var subSm in sm.stateMachines)
            {
                if (subSm.stateMachine != null)
                {
                    CheckStateMachine(controller, assetPath, layerName, subSm.stateMachine, result, controllersWithIssues);
                }
            }
        }

        private static bool IsTransitionInvalid(AnimatorStateTransition transition)
        {
            if (transition == null) return false;
            // A transition is invalid/ignored if it has NO conditions AND hasExitTime is false
            return !transition.hasExitTime && (transition.conditions == null || transition.conditions.Length == 0);
        }

        public static int FixAll(FixMode mode)
        {
            var scan = ScanProject();
            if (scan.InvalidTransitionCount == 0)
                return 0;

            var modifiedPaths = new HashSet<string>();
            var count = 0;

            foreach (var item in scan.InvalidTransitions)
            {
                var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(item.AssetPath);
                if (controller == null) continue;

                if (mode == FixMode.EnableExitTime)
                {
                    item.Transition.hasExitTime = true;
                    if (item.Transition.exitTime <= 0f)
                    {
                        item.Transition.exitTime = 1f;
                    }
                    EditorUtility.SetDirty(item.Transition);
                    EditorUtility.SetDirty(controller);
                    modifiedPaths.Add(item.AssetPath);
                    count++;
                }
                else if (mode == FixMode.Remove)
                {
                    if (item.IsAnyState && item.StateMachine != null)
                    {
                        item.StateMachine.RemoveAnyStateTransition(item.Transition);
                        EditorUtility.SetDirty(item.StateMachine);
                        EditorUtility.SetDirty(controller);
                        modifiedPaths.Add(item.AssetPath);
                        count++;
                    }
                    else if (item.State != null)
                    {
                        item.State.RemoveTransition(item.Transition);
                        EditorUtility.SetDirty(item.State);
                        EditorUtility.SetDirty(controller);
                        modifiedPaths.Add(item.AssetPath);
                        count++;
                    }
                }
            }

            if (modifiedPaths.Count > 0)
            {
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }

            return count;
        }

        #endregion
    }
}
