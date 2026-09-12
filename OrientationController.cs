using UnityEngine;

namespace Orbiter
{
    /// <summary>
    /// Pure "point the ship's nose at a target" logic. No Unity lifecycle, no
    /// Harmony, no logging - same separation as OrbitController.
    ///
    /// Rotational analogue of OrbitController.ComputeInput: a proportional
    /// controller on angular-velocity error, using the same "response time"
    /// framing and the same local-space-command-clamped-to-unit-ball shape.
    /// </summary>
    public class OrientationController
    {
        /// <summary>Seconds we aim to take to close the angle to the target. Higher = gentler.</summary>
        public float ResponseTime = 1.5f;

        /// <summary>Below this angle (degrees) we command zero thrust, to stop chattering.</summary>
        public float Deadband = 1.0f;

        private OWRigidbody _shipBody;
        private OWRigidbody _targetBody;

        public bool HasTarget => _shipBody != null && _targetBody != null;

        // ── Telemetry ─────────────────────────────────────────────────────
        public float LastAngleError { get; private set; }
        public Vector3 LastCommand { get; private set; }

        public void Engage(OWRigidbody shipBody, OWRigidbody targetBody)
        {
            _shipBody = shipBody;
            _targetBody = targetBody;
            LastAngleError = 0f;
            LastCommand = Vector3.zero;
        }

        public void Clear()
        {
            _shipBody = null;
            _targetBody = null;
            LastAngleError = 0f;
            LastCommand = Vector3.zero;
        }

        /// <summary>True if the latched references have gone stale (scene unload, destroyed objects).</summary>
        public bool IsStale()
        {
            return _shipBody == null || _targetBody == null;
        }

        /// <summary>
        /// Returns the local-space rotational thrust command that turns the ship's
        /// forward axis toward the target body, in the same units/convention
        /// ShipThrusterController.ReadRotationalInput() returns.
        /// </summary>
        /// <param name="maxAngularAccel">ThrusterModel.GetMaxRotationalThrust() - an angular acceleration, same "acceleration not force" convention as the translational thrust.</param>
        public Vector3 ComputeInput(float maxAngularAccel)
        {
            if (!HasTarget || maxAngularAccel <= 0f)
                return LastCommand = Vector3.zero;

            Vector3 toTarget = _targetBody.GetWorldCenterOfMass() - _shipBody.GetWorldCenterOfMass();
            if (toTarget.sqrMagnitude < 1f)
                return LastCommand = Vector3.zero;
            toTarget.Normalize();

            Vector3 forward = _shipBody.transform.forward;

            // Vector3.Angle (not OWPhysics.FromToAngularVelocity's Asin-based
            // approach) stays correct across the full 0-180 degree range.
            float angleDeg = Vector3.Angle(forward, toTarget);
            LastAngleError = angleDeg;

            if (angleDeg < Deadband)
                return LastCommand = Vector3.zero;

            Vector3 axis = Vector3.Cross(forward, toTarget);
            if (axis.sqrMagnitude < 1e-6f)
            {
                // Exactly (or almost exactly) opposite: no unique rotation axis falls
                // out of the cross product, so pick an arbitrary one perpendicular to
                // forward to break the tie. Same spirit as OrbitController's dead-stop
                // tangent fallback.
                axis = Vector3.Cross(forward, _shipBody.transform.up);
                if (axis.sqrMagnitude < 1e-6f)
                    axis = _shipBody.transform.right;
            }

            float response = Mathf.Max(ResponseTime, 0.1f);
            Vector3 desiredAngularVel = axis.normalized * (angleDeg * Mathf.Deg2Rad) / response;
            Vector3 angularVelError = desiredAngularVel - _shipBody.GetAngularVelocity();

            Vector3 worldCmd = angularVelError / (maxAngularAccel * response);

            Vector3 local = _shipBody.transform.InverseTransformDirection(worldCmd);
            if (local.sqrMagnitude > 1f)
                local.Normalize();

            return LastCommand = local;
        }
    }
}
