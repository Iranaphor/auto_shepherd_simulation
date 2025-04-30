using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Ursaanimation.CubicFarmAnimals
{
    [RequireComponent(typeof(Animator))]
    [RequireComponent(typeof(Rigidbody))]
    public class SheepController : MonoBehaviour
    {
        /* ───────── baseline tunables (private) ───────── */

        // Movement (ambler vs trot)
        private float trotSpeed       = 4.5f; // flock speed
        private float amblerSpeed     = 3.0f; // alone / just‑stood speed
        private float baseMaxForce    = 6f;
        private float baseNeighbourRadius = 6f;

        // Separation ellipse
        private float baseSepSideRadius    = 2.0f;
        private float baseSepForwardRadius = 2.0f;

        // Rule weights
        private float baseSeparationWeight = 0.8f;
        private float baseAlignmentWeight  = 1.0f;
        private float baseCohesionWeight   = 1.2f;

        // Density comfort
        private int   baseMaxNeighboursForFullCohesion = 8;


        /* Resting behaviour */
        private float baseSitCheckInterval = 10f;
        private float baseSitProbability = 0.12f;
        private float baseMinSitTime = 20f;
        private float baseMaxSitTime = 60f;


        /* Death behaviour */
        private float baseDieCheckInterval = 15f;        // seconds between death checks
        private float baseDieProbability   = 0.02f;  // chance per check
        private float dyingSlowFactor      = 0.6f;       // speed multiplier while dying
        private float dyingDuration        = 3f;         // seconds from start of dying to death anim
        private float infectionRadius      = 7f;         // radius to expose neighbours when dying
        //private float deathSpasmForce      = 0f;         // upward impulse after death animation


        /* Behaviour modifiers */
        private float standUpSlowTime  = 3f;
        private float obstacleAvoidFactor = 0.1f;


        /* Animation state changes */
        private string idleAnim           = "idle";
        private string walkAnim           = "walk_forward";
        private string trotAnim           = "trot_forward";
        private string standToSitAnim     = "stand_to_sit";
        //private string sitIdleAnim        = "idle"; // alias for idle while sitting
        private string sitToStandAnim     = "sit_to_stand";
        private string deathAnim          = "death";


        /* ───────── internal state ───────── */
        private float maxForce, neighbourRadius;
        private float sepSideRadius, sepForwardRadius;
        private float separationWeight, alignmentWeight, cohesionWeight;
        private int   maxNeighboursForFullCohesion;

        private float sitCheckInterval, sitProbability, minSitTime, maxSitTime;
        private float dieCheckInterval, dieProbability;

        private static readonly List<SheepController> _flock = new();

        private Vector3 _velocity;
        private Animator _anim;
        private Rigidbody _rb;

        private enum LifeState { Alive, Dying, Dead }
        private LifeState _life = LifeState.Alive;
        private bool _isSitting;

        // timers
        private float _standSlowTimer = 0f;
        private float _dyingTimer     = 0f;

        // neighbour tracking for infection
        private readonly HashSet<SheepController> _recentContacts = new();

        // variance constants
        private const float PARAM_VARIANCE  = 0.25f;
        private const float WEIGHT_VARIANCE = 0.30f;
        private const float JITTER_STRENGTH_BASE = 0.25f;

        private float jitterStrength;

        /* -------------- Awake -------------- */
        private void Awake()
        {
            float V(float v) => v * Random.Range(1f - PARAM_VARIANCE, 1f + PARAM_VARIANCE);
            float W(float w) => w * Random.Range(1f - WEIGHT_VARIANCE, 1f + WEIGHT_VARIANCE);

            neighbourRadius = V(baseNeighbourRadius);
            sepSideRadius   = Mathf.Max(V(baseSepSideRadius), 0.3f * neighbourRadius);
            sepForwardRadius= Mathf.Max(V(baseSepForwardRadius), 0.3f * neighbourRadius);

            separationWeight = W(baseSeparationWeight);
            alignmentWeight  = W(baseAlignmentWeight);
            cohesionWeight   = W(baseCohesionWeight);

            maxForce = V(baseMaxForce);
            maxNeighboursForFullCohesion = Mathf.Max(1, Mathf.RoundToInt(W(baseMaxNeighboursForFullCohesion)));

            sitCheckInterval = V(baseSitCheckInterval);
            sitProbability   = Mathf.Clamp01(W(baseSitProbability));
            minSitTime       = V(baseMinSitTime);
            maxSitTime       = V(baseMaxSitTime);

            dieCheckInterval = V(baseDieCheckInterval);
            dieProbability   = Mathf.Clamp01(W(baseDieProbability));

            jitterStrength = W(JITTER_STRENGTH_BASE);

            _velocity = Quaternion.Euler(0, Random.Range(0, 360f), 0) * Vector3.forward * amblerSpeed;
            _anim     = GetComponent<Animator>();
            _rb       = GetComponent<Rigidbody>();
            _rb.isKinematic = true; // we move manually except for death spasm

            _flock.Add(this);
        }

        private void Start()
        {
            StartCoroutine(RestRoutine());
            StartCoroutine(MortalityRoutine());
        }

        private void OnDestroy() => _flock.Remove(this);

        /* -------------- Update -------------- */
        private void Update()
        {
            switch (_life)
            {
                case LifeState.Dead:
                    return; // no updates after death
                case LifeState.Dying:
                    UpdateDying();
                    break;
                case LifeState.Alive:
                    UpdateAlive();
                    break;
            }
        }

        /* ---------- Alive behaviour ---------- */
        private int _lastNeighbourCount = 0;
        private void UpdateAlive()
        {
            if (_isSitting)
            {
                //if (!string.IsNullOrEmpty(sitIdleAnim)) _anim.Play(sitIdleAnim,0);
                return;
            }

            Vector3 steer = ComputeBoidSteering();
            Vector3 jitter = Random.insideUnitSphere; jitter.y = 0f;
            steer += jitter * maxForce * jitterStrength;

            _standSlowTimer = Mathf.Max(0f, _standSlowTimer - Time.deltaTime);
            float targetSpeed = (_lastNeighbourCount >= 2 && _standSlowTimer<=0f) ? trotSpeed : amblerSpeed;

            _velocity = Vector3.ClampMagnitude(_velocity + steer * Time.deltaTime, targetSpeed);
            if (_velocity.sqrMagnitude < 0.0001f) return;

            transform.position += _velocity * Time.deltaTime;
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(_velocity,Vector3.up), 5f*Time.deltaTime);

            string moveAnim = (_velocity.magnitude > (amblerSpeed + 0.1f)) ? trotAnim : walkAnim;
            if (!string.IsNullOrEmpty(moveAnim)) _anim.Play(moveAnim,0);
        }

        /* ---------- Dying behaviour ---------- */
        private void UpdateDying()
        {
            _dyingTimer += Time.deltaTime;
            float t = _dyingTimer / dyingDuration;
            float targetSpeed = Mathf.Lerp(amblerSpeed*dyingSlowFactor, 0f, t);
            _velocity = Vector3.ClampMagnitude(_velocity, targetSpeed);
            transform.position += _velocity * Time.deltaTime;

            if (t >= 1f)
            {
                StartCoroutine(Die());
            }
            else
            {
                if (!string.IsNullOrEmpty(walkAnim)) _anim.Play(walkAnim,0);
            }
        }

        private IEnumerator Die()
        {
        
            /*
            Animator anim = GetComponent<Animator>();
            RuntimeAnimatorController rac = anim.runtimeAnimatorController;
            foreach (var clip in rac.animationClips)
            {
                Debug.Log("Clip available: " + clip.name);
            }
            */
        
            _life = LifeState.Dead; // lock out further updates
            _velocity = Vector3.zero;
            if (!string.IsNullOrEmpty(deathAnim))
            {
                _anim.Play(idleAnim,0);
                _anim.Play(deathAnim,0);
            }

            // Expose contacts (infection) immediately
            InfectContacts();

            // wait for death animation (~2.5s assumed)
            yield return new WaitForSeconds(2.5f);

        }

        /* -------------- Boid logic -------------- */
        private Vector3 ComputeBoidSteering()
        {
            Vector3 pos = transform.position;
            Vector3 separation = Vector3.zero, alignment = Vector3.zero, cohesion = Vector3.zero;
            int neighbourCount = 0;
            float obstacleRadius = neighbourRadius * obstacleAvoidFactor;

            foreach (SheepController other in _flock)
            {
                if (other == this || other._life == LifeState.Dead) continue;

                Vector3 toOther = other.transform.position - pos;
                float dist = toOther.magnitude;
                if (dist > neighbourRadius) continue;

                // Track recent contacts for infection
                _recentContacts.Add(other);
                other._recentContacts.Add(this);

                if (other._life == LifeState.Dying) // avoid dying sheep strongly
                {
                    if (dist < obstacleRadius)
                        separation += (-toOther.normalized) * ((obstacleRadius - dist)/ obstacleRadius);
                    continue;
                }

                if (other._isSitting)
                {
                    if (dist < obstacleRadius)
                        separation += (-toOther.normalized) * ((obstacleRadius - dist)/ obstacleRadius);
                    continue;
                }

                neighbourCount++;
                alignment += other._velocity;
                cohesion  += other.transform.position;

                // oval separation
                Vector3 local = transform.InverseTransformDirection(toOther);
                float sx = local.x / sepSideRadius;
                float sz = local.z / sepForwardRadius;
                float inside = sx*sx + sz*sz;
                if (inside < 1f && dist>0.0001f)
                {
                    float strength = 1f - inside;
                    separation += (-toOther.normalized)*strength;
                }
            }
            _lastNeighbourCount = neighbourCount;

            if (neighbourCount==0 && separation==Vector3.zero) return separation;

            // density weighting
            float density = neighbourCount / (float)maxNeighboursForFullCohesion;
            float sepW = separationWeight * (1f + density*0.5f);
            float cohW = cohesionWeight   * Mathf.Clamp01(1f - density*0.5f);
            float alignW = alignmentWeight;

            if (cohesion.sqrMagnitude>0.0001f)
                cohesion=((cohesion/neighbourCount)-pos).normalized*trotSpeed - _velocity;
            if (alignment.sqrMagnitude>0.0001f)
                alignment=(alignment/neighbourCount).normalized*trotSpeed - _velocity;
            if (separation.sqrMagnitude>0.0001f)
                separation=separation.normalized*trotSpeed - _velocity;

            Vector3 steer = separation*sepW + alignment*alignW + cohesion*cohW;
            return Vector3.ClampMagnitude(steer, maxForce);
        }

        /* -------------- Resting logic -------------- */
        private IEnumerator RestRoutine()
        {
            while (true)
            {
                yield return new WaitForSeconds(sitCheckInterval + Random.Range(-2f,2f));
                if (_life!=LifeState.Alive) continue;
                if (!_isSitting && Random.value < sitProbability)
                    yield return StartCoroutine(SitAndSleep());
            }
        }

        private IEnumerator SitAndSleep()
        {
            _isSitting = true;
            _velocity = Vector3.zero;
            if (!string.IsNullOrEmpty(standToSitAnim)) _anim.Play(standToSitAnim,0);
            yield return new WaitForSeconds(1f);
            float sleepTime = Random.Range(minSitTime,maxSitTime);
            //if (!string.IsNullOrEmpty(sitIdleAnim)) _anim.Play(sitIdleAnim,0);
            yield return new WaitForSeconds(sleepTime);
            if (!string.IsNullOrEmpty(sitToStandAnim)) _anim.Play(sitToStandAnim,0);
            yield return new WaitForSeconds(1f);
            _isSitting=false;
            _standSlowTimer=standUpSlowTime;
        }

        /* -------------- Mortality logic -------------- */
        private IEnumerator MortalityRoutine()
        {
            while (true)
            {
                yield return new WaitForSeconds(dieCheckInterval + Random.Range(-3f,3f));
                if (_life!=LifeState.Alive || _isSitting) continue;
                if (Random.value < dieProbability)
                {
                    BeginDying();
                }
            }
        }

        private void BeginDying()
        {
            if (_life!=LifeState.Alive) return;
            _life = LifeState.Dying;
            _dyingTimer = 0f;
        }

        private void InfectContacts()
        {
            foreach (var other in _recentContacts)
            {
                if (other!=null && other._life==LifeState.Alive)
                {
                    if (Vector3.Distance(transform.position, other.transform.position) <= infectionRadius)
                        other.BeginDying();
                }
            }
        }
    }
}

