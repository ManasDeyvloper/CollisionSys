using System;
using Unity.Collections;

namespace CollisionCheck
{
    /// <summary>
    /// Owns the native memory for one BVH: the flat node array and the reordered triangle
    /// index buffer the leaves point into. One instance per mesh (probe gets one built in
    /// local space, target gets one built in world space -- see ProbeBvh / TargetBvh).
    /// Call Dispose() when the owning component is destroyed.
    /// </summary>
    public struct BvhTree : IDisposable
    {
        public NativeArray<BvhNode> Nodes;

        // Triangle indices grouped in threes (triangle i occupies TriangleIndices[3*i..3*i+2]),
        // reordered during the build so each leaf's triangles are contiguous.
        public NativeArray<int> TriangleIndices;

        public int RootIndex => 0;

        public BvhTree(NativeArray<BvhNode> nodes, NativeArray<int> triangleIndices)
        {
            Nodes = nodes;
            TriangleIndices = triangleIndices;
        }

        public bool IsCreated => Nodes.IsCreated;

        public void Dispose()
        {
            if (Nodes.IsCreated) Nodes.Dispose();
            if (TriangleIndices.IsCreated) TriangleIndices.Dispose();
        }
    }
}
