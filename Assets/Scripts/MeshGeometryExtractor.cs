using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace CollisionCheck
{
    /// <summary>
    /// One-time conversion from Unity's managed Mesh data to NativeArrays. Used by both
    /// ProbeBvh (vertices left in local space) and TargetBvh (vertices baked into world
    /// space once, since the target never moves).
    /// </summary>
    public static class MeshGeometryExtractor
    {
        public static NativeArray<float3> ExtractVertices(Mesh mesh, Allocator allocator, float4x4? bakeTransform = null)
        {
            Vector3[] verts = mesh.vertices;
            var result = new NativeArray<float3>(verts.Length, allocator);
            if (bakeTransform.HasValue)
            {
                float4x4 m = bakeTransform.Value;
                for (int i = 0; i < verts.Length; i++)
                    result[i] = math.transform(m, verts[i]);
            }
            else
            {
                for (int i = 0; i < verts.Length; i++)
                    result[i] = verts[i];
            }
            return result;
        }

        public static NativeArray<int> ExtractTriangles(Mesh mesh, Allocator allocator)
        {
            int[] tris = mesh.triangles;
            var result = new NativeArray<int>(tris.Length, allocator);
            for (int i = 0; i < tris.Length; i++) result[i] = tris[i];
            return result;
        }
    }
}
