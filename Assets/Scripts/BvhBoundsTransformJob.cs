using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace CollisionCheck
{
    /// <summary>
    /// Step 1 of the per-frame pipeline. Recomputes every probe BVH node's world-space AABB
    /// from its (fixed, load-time) local AABB and the probe's current world matrix. Runs over
    /// node count, not triangle count -- for a 1M-triangle mesh with 8 tris/leaf that's on the
    /// order of a few hundred thousand nodes at worst, and it's an embarrassingly parallel,
    /// branch-free transform, so this is the cheapest stage in the pipeline (~0.1ms class).
    /// </summary>
    [BurstCompile]
    public struct BvhBoundsTransformJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<BvhNode> LocalNodes;
        public float4x4 LocalToWorld;

        [WriteOnly] public NativeArray<Aabb> WorldBounds;

        public void Execute(int index)
        {
            WorldBounds[index] = Aabb.Transform(LocalNodes[index].Bounds, LocalToWorld);
        }
    }
}
