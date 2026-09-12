using UnityEngine;

namespace Orbiter
{
    /// <summary>
    /// Standalone ThrusterController, same pattern as the base game's own Autopilot
    /// (Autopilot : ThrusterController) - an independent component with its own
    /// enabled/FixedUpdate lifecycle, not the player's manual controller. This is
    /// deliberate: ShipCockpitController disables ShipThrusterController entirely on
    /// unbuckle ("_thrustController.enabled = false"), so a Harmony postfix on that
    /// controller's ReadTranslationalInput never fires once unbuckled. Autopilot
    /// keeps flying after unbuckling because it's never touched by that - this class
    /// gets us the same property by being architecturally identical to Autopilot
    /// rather than piggybacking on the player's controller.
    ///
    /// Unity finds this via AddComponent on the ship's GameObject (see OrbiterMod),
    /// which already has ThrusterModel/RulesetDetector/ShipResources/Autopilot/
    /// ShipThrusterController - base ThrusterController.Awake() wires up
    /// _thrusterModel automatically via GetRequiredComponent.
    /// </summary>
    public class OrbiterThrusterController : ThrusterController
    {
        private RulesetDetector _rulesetDetector;
        private ShipResources _shipResources;
        private Autopilot _autopilot;

        // The player's own manual controller. Reading its already-computed input is
        // the direct replacement for the old postfix's __result trick - and it
        // naturally reads zero while unbuckled, since that controller isn't running
        // then, so this never misfires as a false "manual override" while unbuckled.
        private ShipThrusterController _manualController;

        public override void Awake()
        {
            base.Awake();
            _rulesetDetector = GetComponentInChildren<RulesetDetector>();
            _shipResources = GetComponent<ShipResources>();
            _autopilot = GetComponent<Autopilot>();
            _manualController = GetComponent<ShipThrusterController>();
        }

        public override Vector3 ReadTranslationalInput()
        {
            var mod = OrbiterMod.Instance;
            if (mod == null || !mod.IsOrbitActive) return Vector3.zero;

            if (_manualController != null && _manualController.GetTranslationalInput().sqrMagnitude > 0.01f)
            {
                mod.Disengage(DisengageReason.ManualInput);
                return Vector3.zero;
            }

            if (_shipResources == null || !_shipResources.AreThrustersUsable())
            {
                mod.Disengage(DisengageReason.ThrustersUnusable);
                return Vector3.zero;
            }

            // Don't strand the player with a dry tank halfway through a loop.
            if (_shipResources.GetFractionalFuel() < mod.MinFuelFraction)
            {
                mod.Disengage(DisengageReason.LowFuel);
                return Vector3.zero;
            }

            if (_autopilot != null && _autopilot.IsFlyingToDestination())
            {
                mod.Disengage(DisengageReason.Autopilot);
                return Vector3.zero;
            }

            if (_thrusterModel == null || _rulesetDetector == null) return Vector3.zero;

            float maxAccel = _thrusterModel.GetMaxTranslationalThrust();
            if (maxAccel <= 0f) return Vector3.zero;

            // Same thrust limiting the base game applies (sun station, etc).
            float limitRatio = Mathf.Min(_rulesetDetector.GetThrustLimit(), maxAccel) / maxAccel;

            return mod.Controller.ComputeInput(maxAccel, limitRatio);
        }

        public override Vector3 ReadRotationalInput()
        {
            var mod = OrbiterMod.Instance;
            if (mod == null || !mod.IsOrientationLockActive) return Vector3.zero;

            if (_manualController != null && _manualController.GetRotationalInput().sqrMagnitude > 0.01f)
            {
                mod.DisengageOrientation(DisengageReason.ManualInput);
                return Vector3.zero;
            }

            if (_shipResources == null || !_shipResources.AreThrustersUsable())
            {
                mod.DisengageOrientation(DisengageReason.ThrustersUnusable);
                return Vector3.zero;
            }

            if (_shipResources.GetFractionalFuel() < mod.MinFuelFraction)
            {
                mod.DisengageOrientation(DisengageReason.LowFuel);
                return Vector3.zero;
            }

            if (_autopilot != null && _autopilot.IsFlyingToDestination())
            {
                mod.DisengageOrientation(DisengageReason.Autopilot);
                return Vector3.zero;
            }

            if (_thrusterModel == null) return Vector3.zero;

            float maxAngularAccel = _thrusterModel.GetMaxRotationalThrust();
            if (maxAngularAccel <= 0f) return Vector3.zero;

            return mod.OrientationController.ComputeInput(maxAngularAccel);
        }
    }
}
