using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;

namespace CollisionCheck
{
    [BurstCompile]
    public struct BroadPhaseTraversalJob : IJob
    {
        [ReadOnly] public NativeArray<BvhNode> ProbeTopology;
        [ReadOnly] public NativeArray<Aabb> ProbeWorldBounds;
        [ReadOnly] public NativeArray<BvhNode> TargetNodes;

        public NativeList<int2> CandidatePairs;

        [NativeDisableContainerSafetyRestriction]
        public NativeArray<int2> Stack;

        // --- DEBUG OUTPUTS ---
        public NativeList<int> DebugVisitedProbeNodes;
        public NativeList<int> DebugVisitedTargetNodes;

        public void Execute()
        {
            CandidatePairs.Clear();

            if (DebugVisitedProbeNodes.IsCreated) DebugVisitedProbeNodes.Clear();
            if (DebugVisitedTargetNodes.IsCreated) DebugVisitedTargetNodes.Clear();

            int stackTop = 0;
            Stack[stackTop++] = new int2(0, 0);

            while (stackTop > 0)
            {
                int2 pair = Stack[--stackTop];
                int probeNode = pair.x;
                int targetNode = pair.y;

                // Record traversed nodes for rendering
                if (DebugVisitedProbeNodes.IsCreated && DebugVisitedProbeNodes.Length < DebugVisitedProbeNodes.Capacity)
                    DebugVisitedProbeNodes.Add(probeNode);

                if (DebugVisitedTargetNodes.IsCreated && DebugVisitedTargetNodes.Length < DebugVisitedTargetNodes.Capacity)
                    DebugVisitedTargetNodes.Add(targetNode);

                if (!Aabb.Overlaps(ProbeWorldBounds[probeNode], TargetNodes[targetNode].Bounds))
                    continue;

                bool probeLeaf = ProbeTopology[probeNode].IsLeaf;
                bool targetLeaf = TargetNodes[targetNode].IsLeaf;

                if (probeLeaf && targetLeaf)
                {
                    EmitLeafPairs(ProbeTopology[probeNode], TargetNodes[targetNode]);
                    continue;
                }

                if (probeLeaf)
                {
                    int targetLeft = targetNode + 1;
                    int targetRight = TargetNodes[targetNode].RightChild;
                    if (stackTop < Stack.Length) Stack[stackTop++] = new int2(probeNode, targetLeft);
                    if (stackTop < Stack.Length) Stack[stackTop++] = new int2(probeNode, targetRight);
                }
                else if (targetLeaf)
                {
                    int probeLeft = probeNode + 1;
                    int probeRight = ProbeTopology[probeNode].RightChild;
                    if (stackTop < Stack.Length) Stack[stackTop++] = new int2(probeLeft, targetNode);
                    if (stackTop < Stack.Length) Stack[stackTop++] = new int2(probeRight, targetNode);
                }
                else
                {
                    int probeLeft = probeNode + 1;
                    int probeRight = ProbeTopology[probeNode].RightChild;
                    int targetLeft = targetNode + 1;
                    int targetRight = TargetNodes[targetNode].RightChild;

                    if (stackTop < Stack.Length) Stack[stackTop++] = new int2(probeLeft, targetLeft);
                    if (stackTop < Stack.Length) Stack[stackTop++] = new int2(probeLeft, targetRight);
                    if (stackTop < Stack.Length) Stack[stackTop++] = new int2(probeRight, targetLeft);
                    if (stackTop < Stack.Length) Stack[stackTop++] = new int2(probeRight, targetRight);
                }
            }
        }

        private void EmitLeafPairs(in BvhNode probeLeaf, in BvhNode targetLeaf)
        {
            for (int i = 0; i < probeLeaf.TriangleCount; i++)
            {
                int probeTri = probeLeaf.TriangleStart + i;
                for (int j = 0; j < targetLeaf.TriangleCount; j++)
                {
                    int targetTri = targetLeaf.TriangleStart + j;
                    if (CandidatePairs.Length < CandidatePairs.Capacity)
                        CandidatePairs.Add(new int2(probeTri, targetTri));
                }
            }
        }
    }
}