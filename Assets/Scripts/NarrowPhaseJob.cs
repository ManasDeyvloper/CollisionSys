using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace CollisionCheck
{
    /// <summary>
    /// Step 3 of the per-frame pipeline. Runs once per candidate pair produced by
    /// BroadPhaseTraversalJob -- typically thousands, never millions -- so this is safe to
    /// parallelize across cores despite doing real per-pair math (11-axis SAT, then distance
    /// only if not intersecting). Per the source doc this stage costs ~0.5-2ms at the
    /// estimated candidate counts for a handheld-tool-scale fit check.
    ///
    /// Probe triangle vertices are transformed from local to world space here, per candidate,
    /// rather than maintaining a persistent world-space vertex buffer for the whole probe
    /// mesh -- there are only ever a few thousand candidates, so this transform is cheap
    /// (<0.1ms per the doc) and avoids a redundant full-mesh transform pass every frame.
    /// </summary>
    [BurstCompile]
    public struct NarrowPhaseJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<int2> CandidatePairs;

        [ReadOnly] public NativeArray<float3> ProbeLocalVertices;
        [ReadOnly] public NativeArray<int> ProbeTriangleIndices;
        public float4x4 ProbeLocalToWorld;

        [ReadOnly] public NativeArray<float3> TargetWorldVertices;
        [ReadOnly] public NativeArray<int> TargetTriangleIndices;

        // Signed distance per candidate: positive = clearance gap, negative = penetration.
        [WriteOnly] public NativeArray<float> SignedDistances;

        public void Execute(int index)
        {
            int2 pair = CandidatePairs[index];
            int probeTri = pair.x;
            int targetTri = pair.y;

            float3 a0 = math.transform(ProbeLocalToWorld, ProbeLocalVertices[ProbeTriangleIndices[probeTri * 3 + 0]]);
            float3 a1 = math.transform(ProbeLocalToWorld, ProbeLocalVertices[ProbeTriangleIndices[probeTri * 3 + 1]]);
            float3 a2 = math.transform(ProbeLocalToWorld, ProbeLocalVertices[ProbeTriangleIndices[probeTri * 3 + 2]]);

            float3 b0 = TargetWorldVertices[TargetTriangleIndices[targetTri * 3 + 0]];
            float3 b1 = TargetWorldVertices[TargetTriangleIndices[targetTri * 3 + 1]];
            float3 b2 = TargetWorldVertices[TargetTriangleIndices[targetTri * 3 + 2]];

            SignedDistances[index] = TriangleTests.SignedDistance(a0, a1, a2, b0, b1, b2);
        }
    }
}
