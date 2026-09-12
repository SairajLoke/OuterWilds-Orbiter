using HarmonyLib;

namespace Orbiter
{
    /// <summary>
    /// Thrust injection for Orbiter Mode / Fix-Orientation lives in
    /// OrbiterThrusterController now (a standalone ThrusterController, same pattern
    /// as the base game's own Autopilot) rather than here, since a Harmony postfix
    /// on ShipThrusterController stops firing entirely once the player unbuckles
    /// (ShipCockpitController disables that whole component then). What's left here
    /// is just prompt bookkeeping, unrelated to that concern.
    /// </summary>
    [HarmonyPatch]
    public static class OrbitPatches
    {
        /// <summary>
        /// Keeps the screen prompt in sync. Note: no logging in here. The previous
        /// version wrote to the OWML console every frame, which floods the log and
        /// costs real framerate.
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(ShipPromptController), nameof(ShipPromptController.Update))]
        public static void ShipPromptController_Update_Postfix()
        {
            OrbiterMod.Instance?.UpdatePrompt();
        }

        /// <summary>Landing cancels orbit hold.</summary>
        // [HarmonyPostfix]
        // [HarmonyPatch(typeof(ShipBody), nameof(ShipBody.OnImpact))]
        // public static void ShipBody_OnImpact_Postfix()
        // {
        //     OrbiterMod.Instance?.Disengage(DisengageReason.Landed);
        // }
    }
}
