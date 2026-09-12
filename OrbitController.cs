using UnityEngine;

namespace Orbiter
{
    /// <summary>
    /// Pure orbit-hold logic. No Unity lifecycle, no Harmony, no logging.
    ///
    /// Given a latched target body and the ship's current state, produces a
    /// local-space translational thrust command in the same units the game's
    /// ShipThrusterController.ReadTranslationalInput() returns.
    ///
    /// This is deliberately the *only* place the physics lives, so an RL policy
    /// can later replace ComputeInput() without touching anything else.
    /// </summary>
    public class OrbitController
    {
        // ── Tunables (driven from mod config) ────────────────────────────
        /// <summary>Seconds we aim to take to null the velocity error. Higher = gentler.</summary>
        public float ResponseTime = 2.0f;

        /// <summary>Below this velocity error (m/s) we command zero thrust, to stop chattering.</summary>
        public float Deadband = 0.5f;

        // ── Latched target (set once at engage, never re-resolved) ───────
        private OWRigidbody _shipBody;
        private OWRigidbody _targetBody;
        private ReferenceFrame _targetFrame;

        // ── Latched orbit to hold (set once at engage, from whatever state the
        // ship was in at that moment: current distance, current direction of
        // travel). ComputeInput continuously servos back to *this*, rather than
        // re-deriving "correct" from wherever the ship currently is - that
        // self-referencing approach has no restoring force and just lets the
        // ship drift wherever perturbations take it.
        private float _targetRadius;
        private float _targetOrbitSpeed;
        private Vector3 _orbitNormal;

        // ── Telemetry for the debug window / prompt ──────────────────────
        public Vector3 LastVelError { get; private set; }
        public float LastDistance { get; private set; }
        public float LastOrbitSpeed { get; private set; }
        public string TargetName { get; private set; } = "none";

        /// <summary>The distance latched at Engage() time that ComputeInput tries to hold.</summary>
        public float TargetRadius => _targetRadius;

        /// <summary>The body currently being orbited, or null. Lets other systems (e.g. orientation lock) target the same body without re-resolving it.</summary>
        public OWRigidbody TargetBody => _targetBody;

        /// <summary>The fixed orbital-plane normal latched at Engage() time.</summary>
        public Vector3 OrbitNormal => _orbitNormal;

        /// <summary>Velocity relative to the target's surface (accounts for the body's spin), radial component. m/s, + = moving away from the body.</summary>
        public float LastSurfaceRadialSpeed { get; private set; }
        /// <summary>Velocity relative to the target's surface (accounts for the body's spin), tangential magnitude. m/s.</summary>
        public float LastSurfaceTangentialSpeed { get; private set; }

        /// <summary>Local-space thrust command actually returned by ComputeInput, post thrustLimitRatio. For diagnosing whether real thrust is reaching the ship.</summary>
        public Vector3 LastCommand { get; private set; }
        public float LastMaxAccel { get; private set; }
        public float LastThrustLimitRatio { get; private set; }

        public bool HasTarget => _shipBody != null && _targetBody != null && _targetFrame != null;

        // ─────────────────────────────────────────────────────────────────
        // Target resolution
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Resolves what to orbit: the actively targeted reference frame if there is
        /// one, otherwise the passive (nearest significant body) frame.
        ///
        /// NOTE: verify the exact semantics of ignorePassiveFrame in your build with
        /// Unity Explorer. If GetReferenceFrame(true) turns out to return the passive
        /// frame too, this still behaves correctly, you just lose the "prefer my
        /// lock-on target" preference.
        /// </summary>
        public static bool TryResolveTarget(OWRigidbody shipBody, float maxEngageDistance, out ReferenceFrame frame, out string failReason)
        {
            frame = null;
            failReason = null;

            if (shipBody == null)
            {
                failReason = "no ship";
                return false;
            }

            ReferenceFrame candidate =
                Locator.GetReferenceFrame(true) ?? Locator.GetReferenceFrame(false);

            if (candidate == null)
            {
                failReason = "nothing to orbit";
                return false;
            }

            OWRigidbody body = candidate.GetOWRigidBody();
            if (body == null)
            {
                failReason = "target has no rigidbody";
                return false;
            }

            if (body == shipBody)
            {
                failReason = "cannot orbit yourself";
                return false;
            }

            float dist = (shipBody.GetWorldCenterOfMass() - body.GetWorldCenterOfMass()).magnitude;
            if (dist < 1f)
            {
                failReason = "too close";
                return false;
            }

            // Only gates new engages - an already-orbiting ship holds its orbit
            // regardless of how far it later drifts.
            if (dist > maxEngageDistance)
            {
                failReason = "too far to engage";
                return false;
            }

            // GetOrbitSpeed encodes the body's gravity. If it returns something
            // non-finite or ~zero, the target has no usable gravity well
            // (probes, shuttles, some Bramble / quantum cases).
            float orbitSpeed = candidate.GetOrbitSpeed(dist);
            if (float.IsNaN(orbitSpeed) || float.IsInfinity(orbitSpeed) || orbitSpeed < 0.5f)
            {
                failReason = "no stable orbit here";
                return false;
            }

            frame = candidate;
            return true;
        }

        // ─────────────────────────────────────────────────────────────────
        // Engage / disengage
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Latches the orbit to hold: whatever distance and direction of travel the
        /// ship has right now become the target radius and rotation sense that
        /// ComputeInput will continuously servo back to. Fails rather than latching
        /// a degenerate orbit (e.g. no usable heading to derive a direction from).
        /// </summary>
        public bool Engage(OWRigidbody shipBody, ReferenceFrame frame, out string failReason)
        {
            failReason = null;

            OWRigidbody targetBody = frame.GetOWRigidBody();
            if (targetBody == null)
            {
                failReason = "target has no rigidbody";
                return false;
            }

            Vector3 r0 = shipBody.GetWorldCenterOfMass() - targetBody.GetWorldCenterOfMass();
            float dist0 = r0.magnitude;
            if (dist0 < 1f)
            {
                failReason = "too close";
                return false;
            }
            Vector3 up0 = r0 / dist0;

            Vector3 vRel0 = shipBody.GetVelocity() - targetBody.GetVelocity();
            Vector3 vTan0 = Vector3.ProjectOnPlane(vRel0, up0);

            Vector3 tangent0;
            if (vTan0.sqrMagnitude > 1e-3f)
            {
                tangent0 = vTan0.normalized;
            }
            else
            {
                // Dead stop relative to the target: fall back to where the ship is pointing.
                tangent0 = Vector3.ProjectOnPlane(shipBody.transform.forward, up0);
                if (tangent0.sqrMagnitude < 1e-3f)
                    tangent0 = Vector3.ProjectOnPlane(shipBody.transform.right, up0);
                if (tangent0.sqrMagnitude < 1e-3f)
                {
                    failReason = "no stable heading to orbit from";
                    return false;
                }
                tangent0.Normalize();
            }

            // tangent0 is unit and (by construction, via ProjectOnPlane) perpendicular
            // to up0, so this cross product is always unit length - the check below is
            // just a defensive floating-point guard, not an expected failure path.
            Vector3 orbitNormal = Vector3.Cross(up0, tangent0).normalized;
            if (orbitNormal.sqrMagnitude < 1e-3f)
            {
                failReason = "degenerate orbit direction";
                return false;
            }

            float targetOrbitSpeed = frame.GetOrbitSpeed(dist0);
            if (float.IsNaN(targetOrbitSpeed) || float.IsInfinity(targetOrbitSpeed))
            {
                failReason = "no stable orbit here";
                return false;
            }

            _shipBody = shipBody;
            _targetFrame = frame;
            _targetBody = targetBody;
            _targetRadius = dist0;
            _targetOrbitSpeed = targetOrbitSpeed;
            _orbitNormal = orbitNormal;

            // GetHUDDisplayName() gives the same pretty "The Attlerock" style name
            // the game's own HUD reticle uses; it's only populated for major bodies,
            // so fall back to the raw GameObject name (e.g. "Moon_Body") for anything
            // else (probes, minor asteroids, etc.) rather than showing a blank name.
            string displayName = frame.GetHUDDisplayName();
            TargetName = !string.IsNullOrEmpty(displayName) ? displayName : targetBody.name;
            LastVelError = Vector3.zero;
            LastDistance = dist0;
            LastOrbitSpeed = targetOrbitSpeed;

            return true;
        }

        /// <summary>
        /// Rotates the held orbital plane, letting the player actively change its
        /// orientation while orbiting. Two independent axes, so the full 2 degrees
        /// of freedom a plane's orientation has (inclination + node) are directly
        /// reachable without needing to wait for the ship to travel to a different
        /// point in the orbit:
        ///
        ///  - radialDegrees: rotates around the ship's *current* radial direction
        ///    (ship-to-target line). Keeps the ship's current position exactly on
        ///    the new plane with zero discontinuity, since "up" stays perpendicular
        ///    to the rotated normal by construction.
        ///  - tangentDegrees: rotates around the current tangential (velocity)
        ///    direction - independent of radialDegrees, always available regardless
        ///    of orbital phase. This one does NOT keep the ship exactly on the new
        ///    plane instantly; the existing radial-restoring term in ComputeInput
        ///    absorbs the resulting small offset the same way it corrects any other
        ///    perturbation, so it just reads as the orbit "catching up" briefly.
        /// </summary>
        public void RotateOrbitAxis(float radialDegrees, float tangentDegrees)
        {
            if (!HasTarget) return;

            Vector3 r = _shipBody.GetWorldCenterOfMass() - _targetBody.GetWorldCenterOfMass();
            float dist = r.magnitude;
            if (dist < 1f) return;
            Vector3 up = r / dist;

            if (!Mathf.Approximately(radialDegrees, 0f))
                _orbitNormal = Quaternion.AngleAxis(radialDegrees, up) * _orbitNormal;

            if (!Mathf.Approximately(tangentDegrees, 0f))
            {
                Vector3 tangent = Vector3.Cross(_orbitNormal, up);
                if (tangent.sqrMagnitude > 1e-6f)
                    _orbitNormal = Quaternion.AngleAxis(tangentDegrees, tangent.normalized) * _orbitNormal;
            }

            _orbitNormal.Normalize();
        }

        public void Clear()
        {
            _shipBody = null;
            _targetFrame = null;
            _targetBody = null;
            _targetRadius = 0f;
            _targetOrbitSpeed = 0f;
            _orbitNormal = Vector3.zero;

            TargetName = "none";
            LastVelError = Vector3.zero;
            LastDistance = 0f;
            LastOrbitSpeed = 0f;
            LastSurfaceRadialSpeed = 0f;
            LastSurfaceTangentialSpeed = 0f;
            LastCommand = Vector3.zero;
            LastMaxAccel = 0f;
            LastThrustLimitRatio = 0f;
        }

        /// <summary>True if the latched references have gone stale (scene unload, destroyed objects).</summary>
        public bool IsStale()
        {
            return _shipBody == null || _targetBody == null || _targetFrame == null;
        }

        // ─────────────────────────────────────────────────────────────────
        // Control
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Returns the local-space thrust command, already scaled by thrustLimitRatio.
        /// </summary>
        /// <param name="maxAccel">
        /// ThrusterModel.GetMaxTranslationalThrust(). Despite the name this is an
        /// ACCELERATION (m/s^2), not a force: the base game divides a delta-v/delta-t
        /// term by it. Getting this wrong is why a naive Kp=1 controller saturates.
        /// </param>
        /// <param name="thrustLimitRatio">
        /// min(RulesetDetector.GetThrustLimit(), maxAccel) / maxAccel, as the base game does.
        /// </param>
        public Vector3 ComputeInput(float maxAccel, float thrustLimitRatio)
        {
            LastMaxAccel = maxAccel;
            LastThrustLimitRatio = thrustLimitRatio;

            if (!HasTarget || maxAccel <= 0f)
                return LastCommand = Vector3.zero;

            // Radial vector, target -> ship. This rotates as the ship moves around
            // the body - that's normal orbital motion, not drift.
            Vector3 r = _shipBody.GetWorldCenterOfMass() - _targetBody.GetWorldCenterOfMass();
            float dist = r.magnitude;
            if (dist < 1f)
                return LastCommand = Vector3.zero;

            Vector3 up = r / dist;
            LastDistance = dist;

            // Velocity relative to the target's surface at the ship's location, i.e.
            // GetVelocity() plus the tangential velocity from the body's own spin
            // (GetPointVelocity = GetVelocity() + angularVelocity x (point - centerOfMass)).
            // Telemetry only: unlike vRel below, this is NOT what the control law uses,
            // since orbit-hold should match the body's orbital motion, not its rotation.
            Vector3 surfacePointVel = _targetBody.GetPointVelocity(_shipBody.GetWorldCenterOfMass());
            Vector3 vRelSurface = _shipBody.GetVelocity() - surfacePointVel;
            LastSurfaceRadialSpeed = Vector3.Dot(vRelSurface, up);
            LastSurfaceTangentialSpeed = Vector3.ProjectOnPlane(vRelSurface, up).magnitude;

            // Tangential direction consistent with the fixed orbital plane/rotation
            // sense latched at Engage(), adapted to the ship's current angular
            // position (up) around the body. This replaces re-deriving direction
            // from the ship's live (assist-perturbed) velocity every frame.
            Vector3 tangent = Vector3.Cross(_orbitNormal, up).normalized;

            float response = Mathf.Max(ResponseTime, 0.1f);

            // Restore the altitude latched at Engage(), not just null radial velocity
            // at whatever altitude we currently happen to be at - this is the actual
            // orbit-hold: without it, any drift away from _targetRadius has no
            // restoring force and just becomes the new normal.
            float distError = _targetRadius - dist;
            float desiredRadialSpeed = distError / response;

            Vector3 desiredWorldVel = tangent * _targetOrbitSpeed + up * desiredRadialSpeed + _targetBody.GetVelocity();
            Vector3 velError = desiredWorldVel - _shipBody.GetVelocity();
            LastVelError = velError;

            if (velError.magnitude < Deadband)
                return LastCommand = Vector3.zero;

            // Proportional command in physically meaningful units: "what fraction of
            // max acceleration would null this error in ResponseTime seconds".
            Vector3 worldCmd = velError / (maxAccel * response);

            Vector3 local = _shipBody.transform.InverseTransformDirection(worldCmd);
            if (local.sqrMagnitude > 1f)
                local.Normalize();

            return LastCommand = local * thrustLimitRatio;
        }
    }
}
