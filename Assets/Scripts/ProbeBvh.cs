using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace CollisionCheck
{
    /// <summary>
    /// Rigid probe (handheld tool / gear). The BVH is built ONCE, in local space, when
    /// Initialize() is called -- NOT in Awake(), since the mesh now arrives asynchronously
    /// from a runtime glTF import (see RuntimeMeshLoader) rather than being assigned in the
    /// editor. Every frame after that we do NOT rebuild or refit the tree -- we just recompute
    /// each node's world-space AABB by transforming its local AABB with the current world
    /// matrix (BvhBoundsTransformJob). That's an O(nodeCount) job, not an O(triangleCount)
    /// one, and it's the only per-frame cost this side of the pipeline pays.
    /// </summary>
    public class ProbeBvh : MonoBehaviour
    {
        public BvhTree Tree { get; private set; }
        public bool IsInitialized { get; private set; }

        /// <summary>World-space AABB per node, same indexing as Tree.Nodes. Refreshed every
        /// frame by BvhBoundsTransformJob before broad-phase traversal reads it.</summary>
        public NativeArray<Aabb> WorldBounds { get; private set; }

        private NativeArray<float3> _localVertices;

        /// <summary>Builds the local-space BVH from a mesh that has just finished loading at
        /// runtime. Safe to call again later (e.g. the user picks a different file) -- disposes
        /// the previous tree first.</summary>
        public void Initialize(Mesh mesh)
        {
            if (IsInitialized) DisposeNative();

            _localVertices = MeshGeometryExtractor.ExtractVertices(mesh, Allocator.Persistent);
            var rawTriangles = MeshGeometryExtractor.ExtractTriangles(mesh, Allocator.Temp);

            Tree = BvhBuilder.Build(_localVertices, rawTriangles, Allocator.Persistent);
            WorldBounds = new NativeArray<Aabb>(Tree.Nodes.Length, Allocator.Persistent);

            rawTriangles.Dispose();
            IsInitialized = true;
        }

        /// <summary>Vertex positions for a given triangle, transformed into world space on
        /// demand for the (small) set of narrow-phase candidates.</summary>
        public void GetTriangleVerticesLocal(int triangleIndex, out float3 a, out float3 b, out float3 c)
        {
            int i0 = Tree.TriangleIndices[triangleIndex * 3 + 0];
            int i1 = Tree.TriangleIndices[triangleIndex * 3 + 1];
            int i2 = Tree.TriangleIndices[triangleIndex * 3 + 2];
            a = _localVertices[i0];
            b = _localVertices[i1];
            c = _localVertices[i2];
        }

        public NativeArray<float3> LocalVertices => _localVertices;

        private void OnDestroy() => DisposeNative();

        private void DisposeNative()
        {
            if (_localVertices.IsCreated) _localVertices.Dispose();
            if (WorldBounds.IsCreated) WorldBounds.Dispose();
            if (Tree.IsCreated) Tree.Dispose();
        }
    }
}
