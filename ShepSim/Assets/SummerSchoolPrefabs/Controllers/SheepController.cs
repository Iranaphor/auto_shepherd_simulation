using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Ursaanimation.CubicFarmAnimals
{
    [RequireComponent(typeof(Animator))]
    public class SheepController : MonoBehaviour
    {
        /* ───────── baseline tunables (private) ───────── */

        // Movement & steering (baseline – each sheep will receive a randomised copy)
        // 3 m/s when ambling, 4.5 m/s when trotting in the flock
        [SerializeField] private float baseMaxSpeed        = 4.5f;  // top trot speed
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

        /* Transition & comfort factors */
        [Header("Behaviour Modifiers")]
        [SerializeField] private float slowSpeedFactor  = 0.667f; // 3.0 / 4.5 when alone or just stood up
        [SerializeField] private float standUpSlowTime  = 3f;     // time (s) sheep stays slow after standing
        [SerializeField] private float obstacleAvoidFactor = 0.1f;// sitting‑sheep avoidance radius factor (much smaller)

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
        // resting parameters
        private float sitCheckInterval, sitProbability, minSitTime, maxSitTime;

        private static readonly List<SheepController> _flock = new();

        private Vector3 _velocity;
        private Animator _anim;
        private bool _isSitting;

        // dynamic state
        private float _standSlowTimer = 0f;
        private int   _lastNeighbourCount = 0;

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

            // Initial velocity — start at slowSpeedFactor * maxSpeed ≈ 3 m/s
            _velocity = Quaternion.Euler(0, Random.Range(0, 360f), 0) * Vector3.forward * maxSpeed * slowSpeedFactor;
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

            // Speed regulation
            _standSlowTimer = Mathf.Max(0f, _standSlowTimer - Time.deltaTime);
            float targetMax = (_lastNeighbourCount >= 2 && _standSlowTimer <= 0f) ? maxSpeed : maxSpeed * slowSpeedFactor;

            _velocity = Vector3.ClampMagnitude(_velocity + steer * Time.deltaTime, targetMax);
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

            float obstacleAvoidRadius = neighbourRadius * obstacleAvoidFactor;

            foreach (SheepController other in _flock)
            {
                if (other == this) continue;

                Vector3 toOther = other.transform.position - pos;
                float   dist    = toOther.magnitude;

                if (dist > neighbourRadius) continue;

                if (other._isSitting)
                {
                    // keep minimal distance from resting sheep
                    if (dist < obstacleAvoidRadius)
                    {
                        separation += (-toOther.normalized) * ((obstacleAvoidRadius - dist) / obstacleAvoidRadius);
                    }
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

            _lastNeighbourCount = neighbourCount; // for speed control

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
            _standSlowTimer = standUpSlowTime; // start slow for a short duration
        }
    }
}

