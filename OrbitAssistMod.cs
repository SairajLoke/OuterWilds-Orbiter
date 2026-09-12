using HarmonyLib;
using OWML.Common;
using OWML.Common.Enums;
using OWML.ModHelper;
using System.Reflection;
using UnityEngine;
using UnityEngine.InputSystem;

namespace OrbitAssist
{
    public enum DisengageReason
    {
        PlayerToggle,
        ManualInput,
        LeftCockpit,
        Autopilot,
        ThrustersUnusable,
        LowFuel,
        Landed,
        TargetLost,
        SceneChange
    }

    public class OrbitAssistMod : ModBehaviour
    {
        public static OrbitAssistMod Instance { get; private set; }

        public OrbitController Controller { get; private set; }
        public bool IsOrbitActive { get; private set; }

        public OrientationController OrientationController { get; private set; }
        public bool IsOrientationLockActive { get; private set; }

        // ── Config ───────────────────────────────────────────────────────
        public float MinFuelFraction { get; private set; } = 0.15f;
        private bool _showDebugWindow;
        private float _responseTime = 2.0f;
        private float _deadband = 0.5f;
        private float _engageDistanceThreshold = 3000f;
        private bool _showTrajectoryRing = true;

        // ── Input ────────────────────────────────────────────────────────
        private InputConsts.InputCommandType _toggleCommand;
        private InputConsts.InputCommandType _orientationToggleCommand;

        // ── Scene state ──────────────────────────────────────────────────
        private bool _inSolarSystem;
        private OWRigidbody _shipBody;
        private OrbitPrompt _prompt;
        private OrientationPrompt _orientationPrompt;
        private OrbitTrajectoryRing _trajectoryRing;

        // ─────────────────────────────────────────────────────────────────
        // Lifecycle
        // ─────────────────────────────────────────────────────────────────

        private void Awake()
        {
            Instance = this;
        }

        private void Start()
        {
            Controller = new OrbitController
            {
                ResponseTime = _responseTime,
                Deadband = _deadband
            };
            OrientationController = new OrientationController();

            // Rebindable input. Registration must happen in Start().
            _toggleCommand = ModHelper.RebindingHelper.RegisterRebindable(
                "Orbiter Mode",
                "Circularise your ship's orbit around the targeted or nearest body.",
                Key.O,
                GamepadBinding.DPadDown,
                false);

            _orientationToggleCommand = ModHelper.RebindingHelper.RegisterRebindable(
                "Fix Orientation",
                "Point the ship's nose at the orbited body.",
                Key.L,
                GamepadBinding.DPadUp,
                false);

            Harmony.CreateAndPatchAll(Assembly.GetExecutingAssembly());

            LoadManager.OnStartSceneLoad += OnStartSceneLoad;
            LoadManager.OnCompleteSceneLoad += OnCompleteSceneLoad;

            ModHelper.Console.WriteLine("[OrbiterMode] Loaded.", MessageType.Success);
        }

        private void OnDestroy()
        {
            LoadManager.OnStartSceneLoad -= OnStartSceneLoad;
            LoadManager.OnCompleteSceneLoad -= OnCompleteSceneLoad;
            _prompt?.Teardown();
            _orientationPrompt?.Teardown();
            _trajectoryRing?.Teardown();
            if (Instance == this) Instance = null;
        }

        private void OnStartSceneLoad(OWScene previous, OWScene next)
        {
            // Covers death, loop reset, quit to menu. Drop every latched reference
            // before Unity destroys the objects behind them.
            HardReset();
        }

        private void OnCompleteSceneLoad(OWScene previous, OWScene next)
        {
            HardReset();
            _inSolarSystem = next == OWScene.SolarSystem;
            if (!_inSolarSystem) return;

            // Create these regardless of whether the ship body is found below -
            // Update() re-acquires _shipBody on its own retry loop, but these were
            // only ever constructed here, so bailing out early before this point
            // (as the ship-body check below does) meant they stayed null forever.
            // PromptManager isn't guaranteed to exist on the same frame as the load
            // either, and a single FireOnNextUpdate retry isn't guaranteed to catch
            // it - Update() keeps calling Setup() on these every frame until it sticks.
            _prompt = new OrbitPrompt();
            _orientationPrompt = new OrientationPrompt();

            // Our own GameObject, not a game system lookup - always succeeds
            // immediately, unlike the prompts above.
            _trajectoryRing = new OrbitTrajectoryRing();
            _trajectoryRing.Setup();

            _shipBody = Locator.GetShipBody();
            if (_shipBody == null)
            {
                ModHelper.Console.WriteLine("[OrbiterMode] Ship body not found on scene load.", MessageType.Warning);
                return;
            }
        }

        private void HardReset()
        {
            IsOrbitActive = false;
            Controller?.Clear();
            IsOrientationLockActive = false;
            OrientationController?.Clear();
            _shipBody = null;
            _inSolarSystem = false;
            _prompt?.Teardown();
            _prompt = null;
            _orientationPrompt?.Teardown();
            _orientationPrompt = null;
            _trajectoryRing?.Teardown();
            _trajectoryRing = null;
        }

        // ─────────────────────────────────────────────────────────────────
        // Input
        // ─────────────────────────────────────────────────────────────────

        private void Update()
        {
            if (!_inSolarSystem ) return; //_inputRegistered

            // Re-acquire the ship if we lost it.
            if (_shipBody == null)
            {
                _shipBody = Locator.GetShipBody();
                if (_shipBody == null) return;
            }

            // Cheap no-op once registered; keeps retrying until PromptManager exists.
            _prompt?.Setup(_toggleCommand);
            _orientationPrompt?.Setup(_orientationToggleCommand);

            // Latched references can be destroyed out from under us.
            if (IsOrbitActive && Controller.IsStale())
            {
                Disengage(DisengageReason.TargetLost);
            }

            if (IsOrientationLockActive && OrientationController.IsStale())
            {
                DisengageOrientation(DisengageReason.TargetLost);
            }

            UpdatePrompt();

            if (_showTrajectoryRing && IsOrbitActive && Controller.TargetBody != null)
            {
                _trajectoryRing?.UpdateTransform(
                    Controller.TargetBody.GetWorldCenterOfMass(),
                    Controller.OrbitNormal,
                    Controller.TargetRadius);
                _trajectoryRing?.SetVisible(true);
            }
            else
            {
                _trajectoryRing?.SetVisible(false);
            }

            // Only listen while the player is actually flying the ship.
            if (!OWInput.IsInputMode(InputMode.ShipCockpit | InputMode.LandingCam))
                return;

            var toggleCommand = InputLibrary.GetInputCommand(_toggleCommand);
            if (toggleCommand != null && OWInput.IsNewlyPressed(toggleCommand, InputMode.All))
            {
                if (IsOrbitActive) Disengage(DisengageReason.PlayerToggle);
                else TryEngage();
            }

            var orientationCommand = InputLibrary.GetInputCommand(_orientationToggleCommand);
            if (orientationCommand != null && OWInput.IsNewlyPressed(orientationCommand, InputMode.All))
            {
                if (IsOrientationLockActive) DisengageOrientation(DisengageReason.PlayerToggle);
                else TryEngageOrientation();
            }
        }

        // ─────────────────────────────────────────────────────────────────
        // Engage / disengage
        // ─────────────────────────────────────────────────────────────────

        /// <summary>True if pressing the key right now would do something sensible.</summary>
        public bool CanEngage(out ReferenceFrame frame, out string reason)
        {
            frame = null;
            reason = null;

            if (_shipBody == null) { reason = "no ship"; return false; }

            if (!OWInput.IsInputMode(InputMode.ShipCockpit | InputMode.LandingCam))
            { reason = "not flying"; return false; }

            var autopilot = _shipBody.GetComponent<Autopilot>();
            if (autopilot != null && autopilot.IsFlyingToDestination())
            { reason = "autopilot engaged"; return false; }

            return OrbitController.TryResolveTarget(_shipBody, _engageDistanceThreshold, out frame, out reason);
        }

        private void TryEngage()
        {
            if (!CanEngage(out ReferenceFrame frame, out string reason))
            {
                Notify($"Orbiter Mode unavailable: {reason}");
                return;
            }

            if (!Controller.Engage(_shipBody, frame, out string engageFailReason))
            {
                Notify($"Orbiter Mode unavailable: {engageFailReason}");
                return;
            }

            IsOrbitActive = true;
            Notify($"Orbiter Mode engaged: {Controller.TargetName}");
            ModHelper.Console.WriteLine(
                $"[OrbiterMode] Engaged on {Controller.TargetName} " +
                $"at {Controller.LastDistance:F0}m.", MessageType.Info);
        }

        public void Disengage(DisengageReason reason)
        {
            if (!IsOrbitActive) return;
            IsOrbitActive = false;
            Controller.Clear();

            // Orientation lock has no meaning without an orbit target - it can't
            // outlive the orbit assist itself.
            if (IsOrientationLockActive) DisengageOrientation(reason);

            // Manual input is the normal way to take back control, so don't nag.
            if (reason != DisengageReason.ManualInput && reason != DisengageReason.SceneChange)
                Notify($"Orbiter Mode off ({Describe(reason)})");

            ModHelper.Console.WriteLine($"[OrbiterMode] Disengaged: {reason}", MessageType.Info);
        }

        // ─────────────────────────────────────────────────────────────────
        // Orientation lock engage / disengage
        // ─────────────────────────────────────────────────────────────────

        private void TryEngageOrientation()
        {
            if (!IsOrbitActive)
            {
                Notify("Fix-Orientation unavailable: not orbiting");
                return;
            }

            OrientationController.Engage(_shipBody, Controller.TargetBody);
            IsOrientationLockActive = true;
            Notify($"Fix-Orientation engaged: {Controller.TargetName}");
            ModHelper.Console.WriteLine($"[OrbiterMode] Orientation lock engaged on {Controller.TargetName}.", MessageType.Info);
        }

        public void DisengageOrientation(DisengageReason reason)
        {
            if (!IsOrientationLockActive) return;
            IsOrientationLockActive = false;
            OrientationController.Clear();

            if (reason != DisengageReason.ManualInput && reason != DisengageReason.SceneChange)
                Notify($"Fix-Orientation off ({Describe(reason)})");

            ModHelper.Console.WriteLine($"[OrbiterMode] Orientation lock disengaged: {reason}", MessageType.Info);
        }

        private static string Describe(DisengageReason reason)
        {
            switch (reason)
            {
                case DisengageReason.LowFuel: return "low fuel";
                case DisengageReason.Autopilot: return "autopilot";
                case DisengageReason.LeftCockpit: return "left cockpit";
                case DisengageReason.ThrustersUnusable: return "thrusters offline";
                case DisengageReason.Landed: return "landed";
                case DisengageReason.TargetLost: return "target lost";
                default: return "off";
            }
        }

        // ─────────────────────────────────────────────────────────────────
        // Prompt
        // ─────────────────────────────────────────────────────────────────

        public void UpdatePrompt()
        {
            UpdateOrientationPrompt();

            if (_prompt == null) return;

            if (IsOrbitActive)
            {
                _prompt.SetActiveState(Controller.TargetName);
                _prompt.SetVisible(true);
                return;
            }

            bool available = CanEngage(out _, out _);
            _prompt.SetIdleState();
            _prompt.SetVisible(available);
        }

        private void UpdateOrientationPrompt()
        {
            if (_orientationPrompt == null) return;

            // Only worth showing while there's an orbit target to lock onto.
            _orientationPrompt.SetVisible(IsOrbitActive);
            _orientationPrompt.SetActive(IsOrientationLockActive);
        }

        private void Notify(string message)
        {
            var manager = NotificationManager.SharedInstance;
            if (manager == null) return;
            manager.PostNotification(new NotificationData(NotificationTarget.Ship, message, 4f, true));
        }

        // ─────────────────────────────────────────────────────────────────
        // Config
        // ─────────────────────────────────────────────────────────────────

        public override void Configure(IModConfig config)
        {
            _responseTime = config.GetSettingsValue<float>("responseTime");
            _deadband = config.GetSettingsValue<float>("deadband");
            MinFuelFraction = config.GetSettingsValue<float>("minFuelFraction");
            _engageDistanceThreshold = config.GetSettingsValue<float>("engageDistanceThreshold");
            _showTrajectoryRing = config.GetSettingsValue<bool>("showOrbitTrajectory");
            _showDebugWindow = config.GetSettingsValue<bool>("showDebugWindow");

            // Configure() can fire before Start(), so Controller may not exist yet.
            if (Controller != null)
            {
                Controller.ResponseTime = _responseTime;
                Controller.Deadband = _deadband;
            }
        }

        // ─────────────────────────────────────────────────────────────────
        // Optional debug overlay, off by default
        // ─────────────────────────────────────────────────────────────────

        private void OnGUI()
        {
            if (!_showDebugWindow || !_inSolarSystem || Controller == null) return;

            GUI.Label(new Rect(10, 10, 600, 20), $"[OrbiterMode] active={IsOrbitActive}  target={Controller.TargetName}");
            GUI.Label(new Rect(10, 30, 600, 20), $"dist={Controller.LastDistance:F0}m (hold={Controller.TargetRadius:F0}m)  orbitSpeed={Controller.LastOrbitSpeed:F1}m/s");
            GUI.Label(new Rect(10, 50, 600, 20), $"velError={Controller.LastVelError.magnitude:F2}m/s  {Controller.LastVelError}");
            GUI.Label(new Rect(10, 70, 600, 20), $"surfaceVel: radial={Controller.LastSurfaceRadialSpeed:F2}m/s (+away)  tangential={Controller.LastSurfaceTangentialSpeed:F2}m/s");
            GUI.Label(new Rect(10, 90, 600, 20), $"cmd={Controller.LastCommand} mag={Controller.LastCommand.magnitude:F3}  maxAccel={Controller.LastMaxAccel:F2}m/s^2  limitRatio={Controller.LastThrustLimitRatio:F2}");
            GUI.Label(new Rect(10, 110, 600, 20), $"orient: active={IsOrientationLockActive}  angleErr={OrientationController.LastAngleError:F1}deg  cmd={OrientationController.LastCommand} mag={OrientationController.LastCommand.magnitude:F3}");
        }
    }
}
