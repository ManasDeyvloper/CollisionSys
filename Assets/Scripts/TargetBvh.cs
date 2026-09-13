using Unity.Collections;
using UnityEngine;

namespace CollisionCheck
{
    /// <summary>
    /// Static target (engine bay / housing). Vertices are baked into world space ONCE when
    /// Initialize() is called -- using this object's transform at that moment -- and the BVH
    /// is built over those world-space vertices. No per-frame work at all -- if this object
    /// ever needs to move after loading, this is the piece that would need to switch to the
    /// "keep local, transform per frame" pattern ProbeBvh uses instead.
    /// </summary>
    public class TargetBvh : MonoBehaviour
    {
        public BvhTree Tree { get; private set; }
        public bool IsInitialized { get; private set; }

        private NativeArray<Unity.Mathematics.float3> _worldVertices;

        /// <summary>Bakes the given mesh into world space using this transform's CURRENT
        /// matrix and builds the BVH over it. Call once after a runtime glTF import finishes
        /// and the target has been placed where it belongs. Safe to call again if the user
        /// loads a different target file -- disposes the previous tree first.</summary>
        public void Initialize(Mesh mesh)
        {
            if (IsInitialized) DisposeNative();

            var bake = (Unity.Mathematics.float4x4)transform.localToWorldMatrix;

            _worldVertices = MeshGeometryExtractor.ExtractVertices(mesh, Allocator.Persistent, bake);
            var rawTriangles = MeshGeometryExtractor.ExtractTriangles(mesh, Allocator.Temp);

            Tree = BvhBuilder.Build(_worldVertices, rawTriangles, Allocator.Persistent);

            rawTriangles.Dispose();
            IsInitialized = true;
        }

        /// <summary>Vertex positions for a given triangle, for narrow-phase distance/SAT tests.</summary>
        public void GetTriangleVertices(int triangleIndex, out Unity.Mathematics.float3 a, out Unity.Mathematics.float3 b, out Unity.Mathematics.float3 c)
        {
            int i0 = Tree.TriangleIndices[triangleIndex * 3 + 0];
            int i1 = Tree.TriangleIndices[triangleIndex * 3 + 1];
            int i2 = Tree.TriangleIndices[triangleIndex * 3 + 2];
            a = _worldVertices[i0];
            b = _worldVertices[i1];
            c = _worldVertices[i2];
        }

        public NativeArray<Unity.Mathematics.float3> WorldVerticesRaw => _worldVertices;

        private void OnDestroy() => DisposeNative();

        private void DisposeNative()
        {
            if (_worldVertices.IsCreated) _worldVertices.Dispose();
            if (Tree.IsCreated) Tree.Dispose();
        }
    }
}
