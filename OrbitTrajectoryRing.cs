using UnityEngine;

namespace Orbiter
{
    /// <summary>
    /// Faintly draws the circular orbit path being held, as a LineRenderer ring
    /// around the target body. Same technique the base game's own OrbitLine (used
    /// for the map screen) uses: build a unit circle in local space once, then only
    /// ever move/rotate/scale the transform to place it in the world - the geometry
    /// itself never changes.
    /// </summary>
    public class OrbitTrajectoryRing
    {
        private GameObject _obj;
        private LineRenderer _line;

        private const int NumVerts = 96;

        public void Setup()
        {
            _obj = new GameObject("OrbiterMode_TrajectoryRing");
            _line = _obj.AddComponent<LineRenderer>();
            _line.loop = true;
            _line.useWorldSpace = false; // positions are the unit circle; transform places it
            _line.positionCount = NumVerts;

            var pts = new Vector3[NumVerts];
            for (int i = 0; i < NumVerts; i++)
            {
                float a = (float)i / NumVerts * Mathf.PI * 2f;
                pts[i] = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
            }
            _line.SetPositions(pts);

            _line.material = new Material(Shader.Find("Sprites/Default"));
            _line.startColor = _line.endColor = new Color(1f, 1f, 1f, 0.25f);
            _line.enabled = false;
        }

        /// <summary>Repositions the ring to match the current held orbit. Call every frame while visible.</summary>
        public void UpdateTransform(Vector3 centerWorld, Vector3 orbitNormal, float radius)
        {
            // Any reference direction not parallel to orbitNormal works - only the
            // plane (orbitNormal) matters for a full circle, not the seed direction.
            Vector3 refDir = Vector3.Cross(orbitNormal, Vector3.up);
            if (refDir.sqrMagnitude < 1e-3f)
                refDir = Vector3.Cross(orbitNormal, Vector3.right);

            _obj.transform.position = centerWorld;
            _obj.transform.rotation = Quaternion.LookRotation(refDir, orbitNormal);
            _obj.transform.localScale = Vector3.one * radius;
            _line.widthMultiplier = radius * 0.003f;
        }

        public void SetVisible(bool visible)
        {
            if (_line != null) _line.enabled = visible;
        }

        public void Teardown()
        {
            if (_obj != null) Object.Destroy(_obj);
            _obj = null;
            _line = null;
        }
    }
}
