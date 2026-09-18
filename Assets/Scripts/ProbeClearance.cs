using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace CollisionCheck
{
    /// <summary>
    /// Step 3b of the per-frame pipeline, runs right after NarrowPhaseJob. Scatters each
    /// candidate's signed distance into a per-ORIGINAL-triangle array sized to the probe's
    /// full triangle count, so ProbeClearanceHighlight.shader can index it directly with
    /// SV_PrimitiveID at draw time.
    ///
    /// "Original" matters here: NarrowPhaseJob's candidate pairs index into the BVH's
    /// reordered, leaf-contiguous triangle buffer (BvhTree.TriangleIndices), not the Mesh
    /// asset's own triangle order that SV_PrimitiveID counts against. BvhTree.OriginalTriangleIndex
    /// (built for free as a side effect of BvhBuilder's sort) remaps reordered -> original.
    ///
    /// Single-threaded on purpose, like ClearanceReduceJob, and -- more importantly -- it only
    /// touches the triangles that had a candidate THIS frame or LAST frame, never the full
    /// triangle array. For a 1M-triangle probe, clearing the whole array every frame just to
    /// reset stale highlights would undo the entire point of the broad phase; tracking last
    /// frame's touched set keeps this bounded by candidate count instead.
    /// </summary>
    [BurstCompile]
    public struct ProbeTriangleClearanceJob : IJob
    {
        [ReadOnly] public NativeArray<int2> CandidatePairs;
        [ReadOnly] public NativeArray<float> SignedDistances;
        [ReadOnly] public NativeArray<int> OriginalTriangleIndex;

        // Sized to the probe's full (original mesh draw-order) triangle count.
        // float.MaxValue = "nothing touched this triangle this frame" -> shader draws base color.
        public NativeArray<float> TriangleClearance;

        // Original-mesh triangle indices written this frame; consumed (reset to MaxValue) at
        // the start of next frame's run of this job before being repopulated.
        public NativeList<int> TouchedOriginalTriangles;
        

        public void Execute()
        {
            for (int i = 0; i < TouchedOriginalTriangles.Length; i++)
                TriangleClearance[TouchedOriginalTriangles[i]] = float.MaxValue;
            TouchedOriginalTriangles.Clear();

            for (int i = 0; i < CandidatePairs.Length; i++)
            {
                int reorderedProbeTri = CandidatePairs[i].x;
                int originalTri = OriginalTriangleIndex[reorderedProbeTri];
                float d = SignedDistances[i];

                float existing = TriangleClearance[originalTri];
                if (existing == float.MaxValue)
                    TouchedOriginalTriangles.Add(originalTri);

                // A triangle can appear in multiple candidate pairs in one frame (broad phase
                // found several nearby target triangles) -- keep the worst (smallest) one.
                if (d < existing)
                    TriangleClearance[originalTri] = d;
            }
        }
    }
}