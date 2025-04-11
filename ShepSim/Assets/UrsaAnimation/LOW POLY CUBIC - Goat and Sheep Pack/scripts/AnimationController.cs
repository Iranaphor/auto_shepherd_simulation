using UnityEngine;
using System.Collections;

namespace Ursaanimation.CubicFarmAnimals
{
    public class SheepRandomWalker : MonoBehaviour
    {
        [Header("Movement Settings")]
        public float speed = 2f;                  // Movement speed (units per second)
        public float rotationSpeed = 5f;          // How quickly the sheep rotates to face direction of travel
        public float stoppingThreshold = 0.3f;    // Distance at which we consider the sheep to have arrived

        [Header("Wander Bounds")]
        public float maxWanderDistance = 75f;     // Max distance from current position to pick a new point (updated to 50)
        public float centerAvoidRadius = 15f;      // Avoid points that are within this distance from (0,0)

        [Header("Sit Chance")]
        [Range(0f,1f)]
        public float sitChance = 0.3f;            // Probability that the sheep decides to sit (0.3 = 30%)

        [Header("Sit Duration")]
        public float minSitTime = 20f;            // Minimum time (seconds) to remain seated (updated to 20)
        public float maxSitTime = 45f;            // Maximum time (seconds) to remain seated (updated to 45)

        [Header("Animation")]
        public Animator animator;
        public string walkForwardAnimation = "walk_forward";
        public string standToSitAnimation = "stand_to_sit";
        public string sitToStandAnimation = "sit_to_stand";

        private void Start()
        {
            if (animator == null)
            {
                animator = GetComponent<Animator>();
            }

            // Begin the endless random walk
            StartCoroutine(RandomWalkLoop());
        }

        /// <summary>
        /// Main loop that continuously either sits or wanders to a new point.
        /// </summary>
        private IEnumerator RandomWalkLoop()
        {
            while (true)
            {
                // Decide whether to sit or to move
                if (Random.value < sitChance)
                {
                    yield return StartCoroutine(SitAndWait());
                }
                else
                {
                    // Pick a point around the sheep that’s more likely to be close,
                    // and not too near the origin if possible.
                    Vector3 randomTarget = RandomPreferredPoint();
                    // Move there
                    yield return StartCoroutine(MoveToTarget(randomTarget));
                }
            }
        }

        /// <summary>
        /// Moves the sheep in a straight line to the given target,
        /// rotating smoothly, then exits when close enough.
        /// </summary>
        private IEnumerator MoveToTarget(Vector3 target)
        {
            while (Vector3.Distance(transform.position, target) > stoppingThreshold)
            {
                Vector3 direction = target - transform.position;
                direction.y = 0f; // ignore vertical tilt

                // Rotate smoothly toward target
                if (direction.sqrMagnitude > 0.0001f)
                {
                    Quaternion targetRot = Quaternion.LookRotation(direction, Vector3.up);
                    transform.rotation = Quaternion.Slerp(
                        transform.rotation,
                        targetRot,
                        rotationSpeed * Time.deltaTime
                    );
                }

                // Move forward
                transform.position += transform.forward * speed * Time.deltaTime;

                // Play walking animation while moving
                if (animator != null && !string.IsNullOrEmpty(walkForwardAnimation))
                {
                    animator.Play(walkForwardAnimation);
                }

                yield return null; // wait one frame
            }
        }

        /// <summary>
        /// Plays the sit animation, waits a random amount of time, then stands up.
        /// </summary>
        private IEnumerator SitAndWait()
        {
            // Play stand-to-sit
            if (animator != null && !string.IsNullOrEmpty(standToSitAnimation))
            {
                animator.Play(standToSitAnimation);
            }

            // Wait a bit to let the animation start
            yield return new WaitForSeconds(1f);

            // Remain seated for a random duration
            float waitTime = Random.Range(minSitTime, maxSitTime);
            yield return new WaitForSeconds(waitTime);

            // Play sit-to-stand
            if (animator != null && !string.IsNullOrEmpty(sitToStandAnimation))
            {
                animator.Play(sitToStandAnimation);
            }

            // Wait for stand-up animation
            yield return new WaitForSeconds(1f);
        }

        /// <summary>
        /// Returns a random point around the sheep that:
        /// 1) Prefers shorter distances over longer ones (so it doesn't wander too far).
        /// 2) Avoids the congested center near (0,0) if possible.
        /// </summary>
        private Vector3 RandomPreferredPoint()
        {
            // We'll try multiple attempts to find a suitable point
            // that isn't too close to (0,0).
            const int maxAttempts = 20;

            for (int i = 0; i < maxAttempts; i++)
            {
                // We pick a distance that's more likely to be small:
                // Using Random.value^2 skews distribution toward shorter distances.
                float r = maxWanderDistance * (Random.value * Random.value);
                float angle = Random.Range(0f, Mathf.PI * 2f);

                // local offset from the sheep's position
                Vector3 offset = new Vector3(r * Mathf.Cos(angle), 0f, r * Mathf.Sin(angle));
                Vector3 candidate = transform.position + offset;

                // If candidate is outside the congested center area, accept it
                float distFromCenter = new Vector3(candidate.x, 0f, candidate.z).magnitude;
                if (distFromCenter > centerAvoidRadius)
                {
                    return candidate;
                }
            }

            // If we couldn't find a better point after many attempts,
            // return the sheep's current position (or a minimal offset).
            return transform.position;
        }
    }
}

