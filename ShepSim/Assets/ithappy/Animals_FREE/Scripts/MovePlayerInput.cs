using UnityEngine;
using System.Collections;

namespace Controller
{
    [RequireComponent(typeof(CreatureMover))]
    public class MovePlayerInput : MonoBehaviour
    {
        [Header("Dog Random Wander Settings")]
        public float wanderRadius = 10f;        // Max distance from current position for picking next random point
        public float closeEnoughDistance = 1f;  // Distance at which we consider the dog to have arrived
        public float runSpeedMultiplier = 1f;   // Optional multiplier if you want to adjust run speed

        private CreatureMover m_Mover;
        
        // We'll keep these around because CreatureMover.SetInput(...) expects them:
        private Vector2 m_Axis;
        private bool m_IsRun;
        private bool m_IsJump;
        private Vector3 m_Target;    // The dog's current destination

        private void Awake()
        {
            // CreatureMover reference
            m_Mover = GetComponent<CreatureMover>();
        }

        private void Start()
        {
            // Begin the infinite random wander
            StartCoroutine(RandomRunLoop());
        }

        /// <summary>
        /// Coroutine that endlessly picks a random point, runs there, then repeats.
        /// </summary>
        private IEnumerator RandomRunLoop()
        {
            while (true)
            {
                // 1. Pick a new random point within wanderRadius of the dog's current position
                Vector3 randomDestination = GetRandomDestinationAround(transform.position, wanderRadius);

                // 2. Keep moving until we arrive
                while (Vector3.Distance(transform.position, randomDestination) > closeEnoughDistance)
                {
                    // Set our input each frame so the CreatureMover moves us
                    SetDogRunningInput(randomDestination);

                    yield return null;  // Wait one frame
                }

                // Optionally: pause or do something upon arrival
                // yield return new WaitForSeconds(1f);
                // Then loop will pick another point
            }
        }

        /// <summary>
        /// Sets m_Axis, m_IsRun, m_IsJump so CreatureMover will run toward the given target.
        /// </summary>
        private void SetDogRunningInput(Vector3 destination)
        {
            // We'll assign this to m_Target so CreatureMover knows where to face
            m_Target = destination;

            // We want the dog to move "forward" in local space. By default, 
            // many CreatureMover setups interpret:
            //    (x=0, y=1) => "move forward"
            // If you need strafing or turning, adjust how your CreatureMover is set up.
            m_Axis = new Vector2(0f, 1f);

            // Force "run" on
            m_IsRun = true;
            // No jumping
            m_IsJump = false;

            // Finally, tell the mover
            if (m_Mover != null)
            {
                // If your CreatureMover supports a run speed multiplier, you can integrate that here.
                // For now we just pass the booleans and axes.
                m_Mover.SetInput(in m_Axis, in m_Target, in m_IsRun, m_IsJump);
            }
        }

        /// <summary>
        /// Returns a random position within a circle on the XZ plane (centered around currentPos).
        /// </summary>
        private Vector3 GetRandomDestinationAround(Vector3 currentPos, float radius)
        {
            float angle = Random.Range(0f, Mathf.PI * 2f);
            float r = radius * Mathf.Sqrt(Random.value);  // Uniform distribution over the circle
            Vector3 offset = new Vector3(r * Mathf.Cos(angle), 0f, r * Mathf.Sin(angle));
            return currentPos + offset;
        }
    }
}

