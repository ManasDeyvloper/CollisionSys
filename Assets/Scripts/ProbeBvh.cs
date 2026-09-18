using System;
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
    ///
    /// Also owns the per-triangle clearance data ProbeTriangleClearanceJob writes into each
    /// frame and the GPU-side mirror of it (TriangleClearanceBuffer), which
    /// ProbeClearanceHighlight.shader reads to color individual close/colliding triangles.
    /// This lives here rather than on the feedback controller because it's sized and
    /// allocated at the same time as (and for the lifetime of) the probe's own mesh/BVH.
    /// </summary>
    public class ProbeBvh : MonoBehaviour
    {
        public BvhTree Tree { get; private set; }
        public bool IsInitialized { get; private set; }

        /// <summary>Fired once Initialize() finishes -- i.e. once this GameObject actually has
        /// a mesh, a renderer (added by RuntimeMeshLoader's mesh combine step) and a BVH.
        /// ClearanceFeedbackController listens for this instead of assuming a renderer exists
        /// in Awake(), since the mesh may not arrive until well after scene load.</summary>
        public event Action Initialized;

        /// <summary>World-space AABB per node, same indexing as Tree.Nodes. Refreshed every
        /// frame by BvhBoundsTransformJob before broad-phase traversal reads it.</summary>
        public NativeArray<Aabb> WorldBounds { get; private set; }

        /// <summary>Per-ORIGINAL-triangle signed distance for this frame, sized to the probe's
        /// full triangle count. Written by ProbeTriangleClearanceJob, mirrored to the GPU via
        /// TriangleClearanceBuffer for ProbeClearanceHighlight.shader. float.MaxValue means
        /// "no candidate touched this triangle this frame".</summary>
        public NativeArray<float> TriangleClearance { get; private set; }

        /// <summary>Original-triangle indices written into TriangleClearance this frame; reset
        /// at the start of next frame's ProbeTriangleClearanceJob run instead of clearing the
        /// whole array every frame.</summary>
        public NativeList<int> TouchedOriginalTriangles { get; private set; }

        /// <summary>GPU mirror of TriangleClearance. Bound to the probe's material via
        /// ClearanceFeedbackController; refreshed once per frame by UploadTriangleClearance().</summary>
        public ComputeBuffer TriangleClearanceBuffer { get; private set; }

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

            int triangleCount = rawTriangles.Length / 3;
            TriangleClearance = new NativeArray<float>(triangleCount, Allocator.Persistent);
            FillWithMaxValue(TriangleClearance);
            TouchedOriginalTriangles = new NativeList<int>(1024, Allocator.Persistent);
            TriangleClearanceBuffer = new ComputeBuffer(triangleCount, sizeof(float));
            TriangleClearanceBuffer.SetData(TriangleClearance);

            rawTriangles.Dispose();
            IsInitialized = true;

            Initialized?.Invoke();
        }

        /// <summary>Pushes this frame's TriangleClearance values up to the GPU. Call once per
        /// frame, after ProbeTriangleClearanceJob's handle has been completed.</summary>
        public void UploadTriangleClearance()
        {
            if (TriangleClearanceBuffer != null)
                TriangleClearanceBuffer.SetData(TriangleClearance);
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

        private static void FillWithMaxValue(NativeArray<float> array)
        {
            for (int i = 0; i < array.Length; i++) array[i] = float.MaxValue;
        }

        private void OnDestroy() => DisposeNative();

        private void DisposeNative()
        {
            if (_localVertices.IsCreated) _localVertices.Dispose();
            if (WorldBounds.IsCreated) WorldBounds.Dispose();
            if (TriangleClearance.IsCreated) TriangleClearance.Dispose();
            if (TouchedOriginalTriangles.IsCreated) TouchedOriginalTriangles.Dispose();
            TriangleClearanceBuffer?.Dispose();
            TriangleClearanceBuffer = null;
            if (Tree.IsCreated) Tree.Dispose();
        }
    }
}