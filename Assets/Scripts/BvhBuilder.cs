using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;

namespace CollisionCheck
{
    /// <summary>
    /// Builds a BVH once, at load time, on the main thread. This is intentionally NOT a Burst
    /// job: per the source spec this build is a one-time ~50-150ms cost that is irrelevant to
    /// the per-frame budget, so plain managed C# (easier to read/debug) is fine here. The
    /// result is copied into NativeArrays for cheap, safe reads from the runtime Burst jobs.
    ///
    /// Split strategy: top-down median split on the axis of greatest extent, using each
    /// triangle's centroid. This is simpler than a full SAH build and still gives O(log n)
    /// descent cost; swap in an SAH build later if profiling shows the tree quality (not the
    /// per-frame traversal) is the bottleneck.
    /// </summary>
    public static class BvhBuilder
    {
        private const int MaxTrianglesPerLeaf = 8;

        public static BvhTree Build(NativeArray<float3> vertices, NativeArray<int> triangleIndices, Allocator allocator)
        {
            int triangleCount = triangleIndices.Length / 3;

            // Working list of triangle indices (0..triangleCount-1 into the ORIGINAL triangle
            // array) that gets reordered in place as the tree is built.
            var order = new int[triangleCount];
            var centroids = new float3[triangleCount];
            var bounds = new Aabb[triangleCount];

            for (int t = 0; t < triangleCount; t++)
            {
                order[t] = t;
                float3 a = vertices[triangleIndices[t * 3 + 0]];
                float3 b = vertices[triangleIndices[t * 3 + 1]];
                float3 c = vertices[triangleIndices[t * 3 + 2]];
                bounds[t] = Aabb.FromTriangle(a, b, c);
                centroids[t] = (a + b + c) / 3f;
            }

            var nodes = new List<BvhNode>(triangleCount * 2);
            BuildRecursive(order, 0, triangleCount, bounds, centroids, nodes);

            // Expand the final triangle order (indices into the ORIGINAL triangle array) into
            // the flat vertex-index buffer leaves actually reference at query time.
            var reorderedTriangleIndices = new NativeArray<int>(triangleCount * 3, allocator);
            for (int t = 0; t < triangleCount; t++)
            {
                int srcTri = order[t];
                reorderedTriangleIndices[t * 3 + 0] = triangleIndices[srcTri * 3 + 0];
                reorderedTriangleIndices[t * 3 + 1] = triangleIndices[srcTri * 3 + 1];
                reorderedTriangleIndices[t * 3 + 2] = triangleIndices[srcTri * 3 + 2];
            }

            var nodeArray = new NativeArray<BvhNode>(nodes.Count, allocator);
            for (int i = 0; i < nodes.Count; i++) nodeArray[i] = nodes[i];

            return new BvhTree(nodeArray, reorderedTriangleIndices);
        }

        // Returns the index of the node just created; children (if any) are appended
        // immediately after using the flat "left = self+1, right = explicit index" layout.
        private static int BuildRecursive(int[] order, int start, int count, Aabb[] triBounds, float3[] triCentroids, List<BvhNode> nodes)
        {
            Aabb nodeBounds = Aabb.Empty;
            for (int i = start; i < start + count; i++)
                nodeBounds.Encapsulate(triBounds[order[i]]);

            int nodeIndex = nodes.Count;
            nodes.Add(new BvhNode { Bounds = nodeBounds }); // placeholder, patched below

            if (count <= MaxTrianglesPerLeaf)
            {
                nodes[nodeIndex] = new BvhNode
                {
                    Bounds = nodeBounds,
                    RightChild = -1,
                    TriangleStart = start,
                    TriangleCount = count
                };
                return nodeIndex;
            }

            // Split on the axis of greatest extent, at the median centroid.
            float3 extents = nodeBounds.Extents;
            int axis = extents.x > extents.y
                ? (extents.x > extents.z ? 0 : 2)
                : (extents.y > extents.z ? 1 : 2);

            System.Array.Sort(order, start, count, System.Collections.Generic.Comparer<int>.Create(
                (ia, ib) => triCentroids[ia][axis].CompareTo(triCentroids[ib][axis])));

            int mid = count / 2;

            // Left child is always appended immediately (index == nodeIndex + 1); only the
            // right child's index needs to be stored explicitly.
            BuildRecursive(order, start, mid, triBounds, triCentroids, nodes);
            int rightIndex = BuildRecursive(order, start + mid, count - mid, triBounds, triCentroids, nodes);

            nodes[nodeIndex] = new BvhNode
            {
                Bounds = nodeBounds,
                RightChild = rightIndex,
                TriangleStart = -1,
                TriangleCount = 0
            };
            return nodeIndex;
        }
    }
}
