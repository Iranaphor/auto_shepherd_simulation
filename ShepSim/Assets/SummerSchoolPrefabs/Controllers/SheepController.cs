using System.Collections.Generic;
using UnityEngine;

namespace Ursaanimation.CubicFarmAnimals
{
    [RequireComponent(typeof(Animator))]
    public class SheepController : MonoBehaviour
    {
        /* ───────── private base settings ───────── */
        // Movement & steering
        [SerializeField] private float baseMaxSpeed         = 3.5f;
        [SerializeField] private float baseMaxForce         = 6f;
        [SerializeField] private float baseNeighbourRadius  = 6f;

        // Separation ellipse (half‑axes)
        [SerializeField] private float baseSepSideRadius    = 3.0f; // side‑to‑side
        [SerializeField] private float baseSepForwardRadius = 3.0f; // forward/back

        // Rule weights (baseline)
        [SerializeField] private float separationWeight = 1.4f; // baseline, dynamically up‑scaled
        [SerializeField] private float alignmentWeight  = 1f;
        [SerializeField] private float cohesionWeight   = 0.7f; // baseline, dynamically down‑scaled

        // Density handling
        [SerializeField] private int   maxNeighboursForFullCohesion = 16; // > this => we start suppressing cohesion

        [Header("Animation")]
        [SerializeField] private string walkForwardAnimation = "walk_forward";

        /* ───────── per‑instance runtime values (set in Awake) ───────── */
        private float maxSpeed, maxForce, neighbourRadius;
        private float sepSideRadius, sepForwardRadius;

        private Vector3  _velocity;
        private Animator _anim;

        private static readonly List<SheepController> _flock = new();

        /* ───────── personality constants ───────── */
        private const float VARIANCE        = 0.25f; // ±25 % variation
        private const float JITTER_STRENGTH = 0.25f; // wander noise strength

        /* ───────── Unity lifecycle ───────── */
        private void Awake()
        {
            float V(float v) => v * Random.Range(1f - VARIANCE, 1f + VARIANCE);

            maxSpeed        = V(baseMaxSpeed);
            maxForce        = V(baseMaxForce);
            neighbourRadius = V(baseNeighbourRadius);

            // Keep separation radii at least one third of neighbourRadius
            sepSideRadius    = Mathf.Max(V(baseSepSideRadius), 0.33f * neighbourRadius);
            sepForwardRadius = Mathf.Max(V(baseSepForwardRadius), 0.33f * neighbourRadius);

            _velocity = Quaternion.Euler(0, Random.Range(0, 360f), 0) * Vector3.forward * maxSpeed * 0.5f;
            _anim     = GetComponent<Animator>();

            _flock.Add(this);
        }

        private void OnDestroy() => _flock.Remove(this);

        private void Update()
        {
            Vector3 steer = ComputeBoidSteering();

            // Add small random wander (jitter)
            Vector3 jitter = Random.insideUnitSphere; jitter.y = 0f;
            steer += jitter * maxForce * JITTER_STRENGTH;

            _velocity = Vector3.ClampMagnitude(_velocity + steer * Time.deltaTime, maxSpeed);

            if (_velocity.sqrMagnitude < 0.0001f) return;

            transform.position += _velocity * Time.deltaTime;

            // Smoothly rotate to face travel direction
            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                Quaternion.LookRotation(_velocity, Vector3.up),
                5f * Time.deltaTime);

            _anim.Play(walkForwardAnimation, 0);
        }

        /* ────────── Boid rules with density compliance ────────── */
        private Vector3 ComputeBoidSteering()
        {
            Vector3 pos = transform.position;

            Vector3 separation = Vector3.zero;
            Vector3 alignment  = Vector3.zero;
            Vector3 cohesion   = Vector3.zero;
            int neighbourCount = 0;

            foreach (SheepController other in _flock)
            {
                if (other == this) continue;

                Vector3 toOther = other.transform.position - pos;
                float   dist    = toOther.magnitude;

                if (dist < neighbourRadius)
                {
                    neighbourCount++;
                    alignment += other._velocity;
                    cohesion  += other.transform.position;

                    /* ---- Oval separation with linear fall‑off ---- */
                    Vector3 local = transform.InverseTransformDirection(toOther);
                    float   sx = local.x / sepSideRadius;       // side axis scaled
                    float   sz = local.z / sepForwardRadius;    // forward/back axis scaled
                    float   inside = sx * sx + sz * sz;         // < 1 = inside oval

                    if (inside < 1f && dist > 0.0001f)
                    {
                        float strength = 1f - inside; // linear fall‑off
                        separation += (-toOther.normalized) * strength;
                    }
                }
            }

            if (neighbourCount == 0)
                return Vector3.zero;

            /* --- Density‑adaptive weighting ---------------------------------- */
            float density   = Mathf.Clamp01(neighbourCount / (float)maxNeighboursForFullCohesion);
            float sepW      = separationWeight * (1f + density);     // stronger when crowded
            float cohW      = cohesionWeight   * (1f - density);     // weaker when crowded
            float alignW    = alignmentWeight;                       // unchanged for now

            /* --- Convert accumulators to steering vectors -------------------- */
            if (cohesion.sqrMagnitude > 0.0001f)
                cohesion = ( (cohesion / neighbourCount) - pos ).normalized * maxSpeed - _velocity;
            if (alignment.sqrMagnitude > 0.0001f)
                alignment = ( alignment / neighbourCount ).normalized * maxSpeed - _velocity;
            if (separation.sqrMagnitude > 0.0001f)
                separation = separation.normalized * maxSpeed - _velocity;

            Vector3 steer =
                  separation * sepW
                + alignment  * alignW
                + cohesion   * cohW;

            return Vector3.ClampMagnitude(steer, maxForce);
        }
    }
}

