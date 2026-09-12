using HarmonyLib;
using UnityEngine;

namespace OrbitAssist
{
    [HarmonyPatch]
    public static class OrbitPatches
    {
        // Field refs are resolved once at static init, not every physics frame.
        // Traverse.Create() per frame is measurably slow.
        private static readonly AccessTools.FieldRef<ShipThrusterController, ThrusterModel> ThrusterModelRef =
            AccessTools.FieldRefAccess<ShipThrusterController, ThrusterModel>("_thrusterModel");

        private static readonly AccessTools.FieldRef<ShipThrusterController, RulesetDetector> RulesetDetectorRef =
            AccessTools.FieldRefAccess<ShipThrusterController, RulesetDetector>("_rulesetDetector");

        private static readonly AccessTools.FieldRef<ShipThrusterController, ShipResources> ShipResourcesRef =
            AccessTools.FieldRefAccess<ShipThrusterController, ShipResources>("_shipResources");

        private static readonly AccessTools.FieldRef<ShipThrusterController, Autopilot> AutopilotRef =
            AccessTools.FieldRefAccess<ShipThrusterController, Autopilot>("_autopilot");

        /// <summary>
        /// Postfix rather than prefix, deliberately:
        ///   - the original method still runs, so ignition events and
        ///     _lastTranslationalInput bookkeeping stay correct
        ///   - other mods patching the same method aren't skipped
        ///   - __result gives us the player's actual stick input for free, which is
        ///     the cleanest possible "player wants control back" signal
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(ShipThrusterController), "ReadTranslationalInput")]
        public static void ReadTranslationalInput_Postfix(
            ShipThrusterController __instance, ref Vector3 __result)
        {
            var mod = OrbitAssistMod.Instance;
            if (mod == null || !mod.IsOrbitActive) return;

            // Player touched the thrusters: hand control straight back.
            if (__result.sqrMagnitude > 0.01f)
            {
                mod.Disengage(DisengageReason.ManualInput);
                return;
            }

            if (!OWInput.IsInputMode(InputMode.ShipCockpit | InputMode.LandingCam))
            {
                mod.Disengage(DisengageReason.LeftCockpit);
                return;
            }

            var resources = ShipResourcesRef(__instance);
            if (resources == null || !resources.AreThrustersUsable())
            {
                mod.Disengage(DisengageReason.ThrustersUnusable);
                return;
            }

            // Don't strand the player with a dry tank halfway through a loop.
            if (resources.GetFractionalFuel() < mod.MinFuelFraction)
            {
                mod.Disengage(DisengageReason.LowFuel);
                return;
            }

            var autopilot = AutopilotRef(__instance);
            if (autopilot != null && autopilot.IsFlyingToDestination())
            {
                mod.Disengage(DisengageReason.Autopilot);
                return;
            }

            var thrusterModel = ThrusterModelRef(__instance);
            var ruleset = RulesetDetectorRef(__instance);
            if (thrusterModel == null || ruleset == null) return;

            float maxAccel = thrusterModel.GetMaxTranslationalThrust();
            if (maxAccel <= 0f) return;

            // Same thrust limiting the base game applies (sun station, etc).
            float limitRatio = Mathf.Min(ruleset.GetThrustLimit(), maxAccel) / maxAccel;

            __result = mod.Controller.ComputeInput(maxAccel, limitRatio);
        }

        /// <summary>
        /// Same postfix pattern as ReadTranslationalInput_Postfix, for the
        /// Fix-Orientation toggle: __result gives us the player's actual rotational
        /// stick input for free, so grabbing pitch/yaw/roll hands control straight
        /// back, same as grabbing the translation stick disengages orbit hold.
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(ShipThrusterController), "ReadRotationalInput")]
        public static void ReadRotationalInput_Postfix(
            ShipThrusterController __instance, ref Vector3 __result)
        {
            var mod = OrbitAssistMod.Instance;
            if (mod == null || !mod.IsOrientationLockActive) return;

            if (__result.sqrMagnitude > 0.01f)
            {
                mod.DisengageOrientation(DisengageReason.ManualInput);
                return;
            }

            if (!OWInput.IsInputMode(InputMode.ShipCockpit | InputMode.LandingCam))
            {
                mod.DisengageOrientation(DisengageReason.LeftCockpit);
                return;
            }

            var resources = ShipResourcesRef(__instance);
            if (resources == null || !resources.AreThrustersUsable())
            {
                mod.DisengageOrientation(DisengageReason.ThrustersUnusable);
                return;
            }

            if (resources.GetFractionalFuel() < mod.MinFuelFraction)
            {
                mod.DisengageOrientation(DisengageReason.LowFuel);
                return;
            }

            var autopilot = AutopilotRef(__instance);
            if (autopilot != null && autopilot.IsFlyingToDestination())
            {
                mod.DisengageOrientation(DisengageReason.Autopilot);
                return;
            }

            var thrusterModel = ThrusterModelRef(__instance);
            if (thrusterModel == null) return;

            float maxAngularAccel = thrusterModel.GetMaxRotationalThrust();
            if (maxAngularAccel <= 0f) return;

            __result = mod.OrientationController.ComputeInput(maxAngularAccel);
        }

        /// <summary>
        /// Keeps the screen prompt in sync. Note: no logging in here. The previous
        /// version wrote to the OWML console every frame, which floods the log and
        /// costs real framerate.
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(ShipPromptController), nameof(ShipPromptController.Update))]
        public static void ShipPromptController_Update_Postfix()
        {
            OrbitAssistMod.Instance?.UpdatePrompt();
        }

        /// <summary>Landing cancels orbit hold.</summary>
        // [HarmonyPostfix]
        // [HarmonyPatch(typeof(ShipBody), nameof(ShipBody.OnImpact))]
        // public static void ShipBody_OnImpact_Postfix()
        // {
        //     OrbitAssistMod.Instance?.Disengage(DisengageReason.Landed);
        // }
    }
}
