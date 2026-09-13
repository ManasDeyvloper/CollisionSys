using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace CollisionCheck
{
    /// <summary>
    /// Orchestrator. Owns no geometry itself -- just references the probe, the target, and the
    /// feedback controller, and drives the five-stage pipeline every frame:
    ///
    ///   1. BvhBoundsTransformJob   - refresh probe node bounds into world space
    ///   2. BroadPhaseTraversalJob  - dual-BVH descent -> candidate triangle pairs
    ///   3. NarrowPhaseJob          - exact SAT + distance per candidate (parallel)
    ///   4. ClearanceReduceJob      - reduce to one signed distance for the frame
    ///   5. ClearanceFeedbackController.ApplyClearance - update the visual gradient
    ///
    /// This first version completes the whole job chain synchronously within Update() (a
    /// Complete() call at the end), matching the "discrete per-frame test is sufficient for
    /// slow/deliberate motion" conclusion in the source doc. If you later add VR controller
    /// input where frame-to-frame latency matters more, look at spreading stages 3-4 across
    /// two frames (schedule this frame, complete + apply next frame) instead of blocking here.
    /// </summary>
    public class CollisionManager : MonoBehaviour
    {
        [SerializeField] private ProbeBvh _probe;
        [SerializeField] private TargetBvh _target;
        [SerializeField] private ClearanceFeedbackController _feedback;

        [Tooltip("Upper bound on candidate triangle pairs per frame. Increase if you see " +
                 "the candidate list saturating (large, complex contact regions); decrease " +
                 "to cap worst-case frame cost.")]
        [SerializeField] private int _maxCandidatePairs = 65536;

        [Tooltip("Upper bound on the broad-phase traversal stack. Increase only if deep/unbalanced " +
                 "trees cause traversal to stop early (see BroadPhaseTraversalJob comments).")]
        [SerializeField] private int _maxTraversalStackSize = 8192;

        private NativeList<int2> _candidatePairs;
        private NativeArray<int2> _traversalStack;
        private NativeArray<float> _signedDistances;
        private NativeReference<float> _minSignedDistance;
        public bool IsPipelineActive => _probe != null && _probe.IsInitialized && _target != null && _target.IsInitialized;
        public int DebugCandidateCount => _candidatePairs.IsCreated ? _candidatePairs.Length : 0;
        public float DebugMinClearance => _minSignedDistance.IsCreated ? _minSignedDistance.Value : float.MaxValue;
        public int DebugProbeNodeCount => (_probe != null && _probe.IsInitialized) ? _probe.Tree.Nodes.Length : 0;
        public int DebugTargetNodeCount => (_target != null && _target.IsInitialized) ? _target.Tree.Nodes.Length : 0;
        public int MaxCandidatePairs => _maxCandidatePairs;
        public NativeList<int2> CandidatePairs => _candidatePairs;
        private NativeList<int> _debugVisitedProbeNodes;
        private NativeList<int> _debugVisitedTargetNodes;

        public NativeList<int> DebugVisitedProbeNodes => _debugVisitedProbeNodes;
        public NativeList<int> DebugVisitedTargetNodes => _debugVisitedTargetNodes;



        private void Awake()
        {
            _candidatePairs = new NativeList<int2>(_maxCandidatePairs, Allocator.Persistent);
            _traversalStack = new NativeArray<int2>(_maxTraversalStackSize, Allocator.Persistent);
            _signedDistances = new NativeArray<float>(_maxCandidatePairs, Allocator.Persistent);
            _minSignedDistance = new NativeReference<float>(Allocator.Persistent);

            // Initialize debug node lists
            _debugVisitedProbeNodes = new NativeList<int>(1024, Allocator.Persistent);
            _debugVisitedTargetNodes = new NativeList<int>(1024, Allocator.Persistent);
        }

        private void Update()
        {
            // Meshes now arrive asynchronously via RuntimeMeshLoader (file browser + glTF
            // import), so both BVHs may not exist yet -- especially in the first few seconds
            // after the scene loads, or between the user picking one file and the other.
            if (!_probe.IsInitialized || !_target.IsInitialized) return;

            // --- Stage 1: transform probe BVH bounds into world space ---
            var transformJob = new BvhBoundsTransformJob
            {
                LocalNodes = _probe.Tree.Nodes,
                LocalToWorld = _probe.transform.localToWorldMatrix,
                WorldBounds = _probe.WorldBounds
            };
            JobHandle transformHandle = transformJob.Schedule(_probe.Tree.Nodes.Length, 64);

            // --- Stage 2: broad-phase dual-BVH traversal ---
            var broadPhaseJob = new BroadPhaseTraversalJob
            {
                ProbeTopology = _probe.Tree.Nodes,
                ProbeWorldBounds = _probe.WorldBounds,
                TargetNodes = _target.Tree.Nodes,
                CandidatePairs = _candidatePairs,
                Stack = _traversalStack,
                DebugVisitedProbeNodes = _debugVisitedProbeNodes,
                DebugVisitedTargetNodes = _debugVisitedTargetNodes
            };
            JobHandle broadPhaseHandle = broadPhaseJob.Schedule(transformHandle);

            // Broad phase must finish before we know how many candidates exist, so complete
            // here before scheduling the narrow phase over that exact count.
            broadPhaseHandle.Complete();

            int candidateCount = _candidatePairs.Length;
            if (candidateCount == 0)
            {
                _feedback.ApplyClearance(float.MaxValue);
                return;
            }

            // --- Stage 3: narrow-phase exact SAT + distance, parallel over candidates ---
            var narrowPhaseJob = new NarrowPhaseJob
            {
                CandidatePairs = _candidatePairs.AsArray(),
                ProbeLocalVertices = _probe.LocalVertices,
                ProbeTriangleIndices = _probe.Tree.TriangleIndices,
                ProbeLocalToWorld = _probe.transform.localToWorldMatrix,
                TargetWorldVertices = _target.WorldVerticesRaw,
                TargetTriangleIndices = _target.Tree.TriangleIndices,
                SignedDistances = _signedDistances
            };
            JobHandle narrowPhaseHandle = narrowPhaseJob.Schedule(candidateCount, 32);

            // --- Stage 4: reduce to a single worst-case signed distance ---
            var reduceJob = new ClearanceReduceJob
            {
                SignedDistances = _signedDistances.GetSubArray(0, candidateCount),
                MinSignedDistance = _minSignedDistance
            };
            JobHandle reduceHandle = reduceJob.Schedule(narrowPhaseHandle);

            reduceHandle.Complete();

            // --- Stage 5: visual feedback ---
            _feedback.ApplyClearance(_minSignedDistance.Value);
        }

        private void OnDestroy()
        {
            if (_candidatePairs.IsCreated) _candidatePairs.Dispose();
            if (_traversalStack.IsCreated) _traversalStack.Dispose();
            if (_signedDistances.IsCreated) _signedDistances.Dispose();
            if (_minSignedDistance.IsCreated) _minSignedDistance.Dispose();

            if (_debugVisitedProbeNodes.IsCreated) _debugVisitedProbeNodes.Dispose();
            if (_debugVisitedTargetNodes.IsCreated) _debugVisitedTargetNodes.Dispose();
        }
    }
}
