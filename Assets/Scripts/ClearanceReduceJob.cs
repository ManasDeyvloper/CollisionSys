using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace CollisionCheck
{
    /// <summary>
    /// Step 4 of the per-frame pipeline. Reduces the (typically low-thousands-sized) signed
    /// distance array from NarrowPhaseJob down to a single worst-case value: the smallest
    /// signed distance, i.e. the closest clearance or deepest penetration found anywhere on
    /// the probe. Single-threaded on purpose -- at these array sizes a sequential min pass
    /// is faster than the overhead of splitting it across worker threads.
    ///
    /// If you later want a localized gradient (e.g. color only the specific probe faces that
    /// are close) rather than one global number, keep SignedDistances around and look it up
    /// per-triangle in ClearanceFeedbackController instead of reducing here.
    /// </summary>
    [BurstCompile]
    public struct ClearanceReduceJob : IJob
    {
        [ReadOnly] public NativeArray<float> SignedDistances;
        public NativeReference<float> MinSignedDistance;

        public void Execute()
        {
            if (SignedDistances.Length == 0)
            {
                // No candidates at all this frame -- probe isn't near the target anywhere the
                // broad phase found overlap. Report a large clearance value.
                MinSignedDistance.Value = float.MaxValue;
                return;
            }

            float min = float.MaxValue;
            for (int i = 0; i < SignedDistances.Length; i++)
                min = SignedDistances[i] < min ? SignedDistances[i] : min;

            MinSignedDistance.Value = min;
        }
    }
}
