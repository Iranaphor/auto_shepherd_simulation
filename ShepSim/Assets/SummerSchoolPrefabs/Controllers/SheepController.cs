using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Ursaanimation.CubicFarmAnimals
{
    [RequireComponent(typeof(Animator))]
    public class SheepController : MonoBehaviour
    {
        /* ───────── baseline tunables (private) ───────── */

        // Movement & steering
        [SerializeField] private float baseMaxSpeed        = 3.8f;  // slightly faster
        [SerializeField] private float baseMaxForce        = 6f;
        [SerializeField] private float baseNeighbourRadius = 6f;

        // Separation ellipse half‑axes (smaller => tighter pack)
        [SerializeField] private float baseSepSideRadius    = 2.0f;
        [SerializeField] private float baseSepForwardRadius = 2.0f;

        // Rule weights
        [SerializeField] private float baseSeparationWeight = 0.8f;
        [SerializeField] private float baseAlignmentWeight  = 1.0f;
        [SerializeField] private float baseCohesionWeight   = 1.2f;

        // Density comfort
        [SerializeField] private int baseMaxNeighboursForFullCohesion = 8;

        /* Resting behaviour */
        [Header("Resting")]
        [SerializeField] private float baseSitCheckInterval = 10f;                // seconds between rest checks
        [SerializeField, Range(0f, 1f)] private float baseSitProbability = 0.12f; // chance per check to sit
        [SerializeField] private float baseMinSitTime = 20f;  // seconds
        [SerializeField] private float baseMaxSitTime = 60f;

        [Header("Animation")]
        [SerializeField] private string walkForwardAnimation = "walk_forward";
        [SerializeField] private string standToSitAnimation  = "stand_to_sit";
        [SerializeField] private string sitIdleAnimation     = "sit_idle";
        [SerializeField] private string sitToStandAnimation  = "sit_to_stand";

        /* ───────── per‑sheep runtime values (randomised) ───────── */
        // movement
        private float maxSpeed, maxForce, neighbourRadius;
        // personal space
        private float sepSideRadius, sepForwardRadius;
        // weights
        private float separationWeight, alignmentWeight, cohesionWeight;
        private int   maxNeighboursForFullCohesion;
        // wander noise
        private float jitterStrength;
        // resting
        private float sitCheckInterval, sitProbability, minSitTime, maxSitTime;

        private static readonly List<SheepController> _flock = new();

        private Vector3 _velocity;
        private Animator _anim;
        private bool _isSitting;

        /* ---- personality variance constants ---- */
        private const float PARAM_VARIANCE   = 0.25f;
        private const float WEIGHT_VARIANCE  = 0.30f;
        private const float BASE_JITTER      = 0.25f;

        /* -------------- Unity lifecycle -------------- */
        private void Awake()
        {
            float V(float v) => v * Random.Range(1f - PARAM_VARIANCE, 1f + PARAM_VARIANCE);
            float W(float w) => w * Random.Range(1f - WEIGHT_VARIANCE, 1f + WEIGHT_VARIANCE);

            // Movement & perception
            maxSpeed        = V(baseMaxSpeed);
            maxForce        = V(baseMaxForce);
            neighbourRadius = V(baseNeighbourRadius);

            sepSideRadius    = Mathf.Max(V(baseSepSideRadius), 0.3f * neighbourRadius);
            sepForwardRadius = Mathf.Max(V(baseSepForwardRadius), 0.3f * neighbourRadius);

            // Rule weights
            separationWeight = W(baseSeparationWeight);
            alignmentWeight  = W(baseAlignmentWeight);
            cohesionWeight   = W(baseCohesionWeight);

            // Social comfort
            maxNeighboursForFullCohesion = Mathf.Max(1, Mathf.RoundToInt(W(baseMaxNeighboursForFullCohesion)));

            // Wander noise
            jitterStrength = W(BASE_JITTER);

            // Resting parameters
            sitCheckInterval = V(baseSitCheckInterval);
            sitProbability   = Mathf.Clamp01(W(baseSitProbability));
            minSitTime       = V(baseMinSitTime);
            maxSitTime       = V(baseMaxSitTime);

            // Initial velocity & registration
            _velocity = Quaternion.Euler(0, Random.Range(0, 360f), 0) * Vector3.forward * maxSpeed * 0.5f;
            _anim     = GetComponent<Animator>();

            _flock.Add(this);
        }

        private void Start()
        {
            StartCoroutine(RestRoutine());
        }

        private void OnDestroy()
        {
            _flock.Remove(this);
        }

        private void Update()
        {
            if (_isSitting)
            {
                if (!string.IsNullOrEmpty(sitIdleAnimation))
                    _anim.Play(sitIdleAnimation, 0);
                return;
            }

            Vector3 steer = ComputeBoidSteering();

            // Add wander
            Vector3 jitter = Random.insideUnitSphere; jitter.y = 0f;
            steer += jitter * maxForce * jitterStrength;

            _velocity = Vector3.ClampMagnitude(_velocity + steer * Time.deltaTime, maxSpeed);
            if (_velocity.sqrMagnitude < 0.0001f) return;

            transform.position += _velocity * Time.deltaTime;
            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                Quaternion.LookRotation(_velocity, Vector3.up),
                5f * Time.deltaTime);

            if (!string.IsNullOrEmpty(walkForwardAnimation))
                _anim.Play(walkForwardAnimation, 0);
        }

        /* -------------- Boid logic -------------- */
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

                if (dist > neighbourRadius) continue;

                if (other._isSitting)
                {
                    // obstacle – strong repulsion, no alignment/cohesion
                    separation += (-toOther.normalized) * Mathf.Clamp01((neighbourRadius - dist) / neighbourRadius);
                    continue;
                }

                neighbourCount++;
                alignment += other._velocity;
                cohesion  += other.transform.position;

                // oval separation
                Vector3 local = transform.InverseTransformDirection(toOther);
                float sx = local.x / sepSideRadius;
                float sz = local.z / sepForwardRadius;
                float inside = sx * sx + sz * sz; // <1 inside oval

                if (inside < 1f && dist > 0.0001f)
                {
                    float strength = 1f - inside; // linear fall‑off
                    separation += (-toOther.normalized) * strength;
                }
            }

            if (neighbourCount == 0 && separation == Vector3.zero)
                return Vector3.zero;

            // density adaptive weighting
            float density = neighbourCount / (float)maxNeighboursForFullCohesion;
            float sepW = separationWeight * (1f + density * 0.5f);
            float cohW = cohesionWeight   * Mathf.Clamp01(1f - density * 0.5f);
            float alignW = alignmentWeight;

            if (cohesion.sqrMagnitude > 0.0001f)
                cohesion = ((cohesion / neighbourCount) - pos).normalized * maxSpeed - _velocity;
            if (alignment.sqrMagnitude > 0.0001f)
                alignment = (alignment / neighbourCount).normalized * maxSpeed - _velocity;
            if (separation.sqrMagnitude > 0.0001f)
                separation = separation.normalized * maxSpeed - _velocity;

            Vector3 steer = separation * sepW + alignment * alignW + cohesion * cohW;
            return Vector3.ClampMagnitude(steer, maxForce);
        }

        /* -------------- Sitting / sleeping -------------- */
        private IEnumerator RestRoutine()
        {
            while (true)
            {
                yield return new WaitForSeconds(sitCheckInterval + Random.Range(-2f, 2f));

                if (!_isSitting && Random.value < sitProbability)
                    yield return StartCoroutine(SitAndSleep());
            }
        }

        private IEnumerator SitAndSleep()
        {
            _isSitting = true;
            _velocity = Vector3.zero;

            if (!string.IsNullOrEmpty(standToSitAnimation))
                _anim.Play(standToSitAnimation, 0);

            yield return new WaitForSeconds(1f);

            float sleepTime = Random.Range(minSitTime, maxSitTime);
            if (!string.IsNullOrEmpty(sitIdleAnimation))
                _anim.Play(sitIdleAnimation, 0);

            yield return new WaitForSeconds(sleepTime);

            if (!string.IsNullOrEmpty(sitToStandAnimation))
                _anim.Play(sitToStandAnimation, 0);

            yield return new WaitForSeconds(1f);

            _isSitting = false;
        }
    }
}

