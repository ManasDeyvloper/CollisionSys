using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace CollisionCheck
{
    public class CollisionDebugGizmos : MonoBehaviour
    {
        [SerializeField] private CollisionManager _manager;
        [SerializeField] private ProbeBvh _probe;
        [SerializeField] private TargetBvh _target;

        [Header("Visualization Options")]
        public bool DrawCandidateTriangles = true;
        public bool DrawProcessedProbeNodes = true;
        public bool DrawProcessedTargetNodes = true;

        [Header("Colors")]
        public Color ProbeTriColor = Color.cyan;
        public Color TargetTriColor = Color.magenta;
        public Color ProbeBvhBoxColor = new Color(0f, 1f, 0f, 0.4f);  // Green
        public Color TargetBvhBoxColor = new Color(1f, 0.5f, 0f, 0.4f); // Orange

        private void OnDrawGizmos()
        {
            if (_manager == null || !_manager.IsPipelineActive) return;

            // 1. Draw Active Probe BVH Bounds
            if (DrawProcessedProbeNodes && _manager.DebugVisitedProbeNodes.IsCreated && _probe.WorldBounds.IsCreated)
            {
                Gizmos.color = ProbeBvhBoxColor;
                NativeList<int> nodes = _manager.DebugVisitedProbeNodes;
                for (int i = 0; i < nodes.Length; i++)
                {
                    int nodeIdx = nodes[i];
                    if (nodeIdx < _probe.WorldBounds.Length)
                        DrawAabbGizmo(_probe.WorldBounds[nodeIdx]);
                }
            }

            // 2. Draw Active Target BVH Bounds
            if (DrawProcessedTargetNodes && _manager.DebugVisitedTargetNodes.IsCreated && _target.Tree.Nodes.IsCreated)
            {
                Gizmos.color = TargetBvhBoxColor;
                NativeList<int> nodes = _manager.DebugVisitedTargetNodes;
                for (int i = 0; i < nodes.Length; i++)
                {
                    int nodeIdx = nodes[i];
                    if (nodeIdx < _target.Tree.Nodes.Length)
                        DrawAabbGizmo(_target.Tree.Nodes[nodeIdx].Bounds);
                }
            }

            // 3. Draw Broadphase Candidate Triangles
            if (DrawCandidateTriangles && _manager.CandidatePairs.IsCreated)
            {
                NativeArray<int2> pairs = _manager.CandidatePairs.AsArray();
                
                for (int i = 0; i < pairs.Length; i++)
                {
                    int2 pair = pairs[i];

                    if (GetTriangleVertices(_probe.LocalVertices, _probe.Tree.TriangleIndices, pair.x, out float3 pA, out float3 pB, out float3 pC))
                    {
                        float4x4 pMat = _probe.transform.localToWorldMatrix;
                        Vector3 wA = math.transform(pMat, pA);
                        Vector3 wB = math.transform(pMat, pB);
                        Vector3 wC = math.transform(pMat, pC);

                        Gizmos.color = ProbeTriColor;
                        Gizmos.DrawLine(wA, wB);
                        Gizmos.DrawLine(wB, wC);
                        Gizmos.DrawLine(wC, wA);
                    }

                    if (GetTriangleVertices(_target.WorldVerticesRaw, _target.Tree.TriangleIndices, pair.y, out float3 tA, out float3 tB, out float3 tC))
                    {
                        Gizmos.color = TargetTriColor;
                        Gizmos.DrawLine(tA, tB);
                        Gizmos.DrawLine(tB, tC);
                        Gizmos.DrawLine(tC, tA);
                    }
                }
            }
        }

        private bool GetTriangleVertices(NativeArray<float3> verts, NativeArray<int> indices, int triIndex, out float3 a, out float3 b, out float3 c)
        {
            int baseIdx = triIndex * 3;
            if (baseIdx + 2 >= indices.Length)
            {
                a = b = c = float3.zero;
                return false;
            }
            a = verts[indices[baseIdx + 0]];
            b = verts[indices[baseIdx + 1]];
            c = verts[indices[baseIdx + 2]];
            return true;
        }

        private void DrawAabbGizmo(Aabb bounds)
        {
            Vector3 center = bounds.Center;
            Vector3 size = bounds.Max - bounds.Min;
            Gizmos.DrawWireCube(center, size);
        }
    }
}