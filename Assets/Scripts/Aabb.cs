using Unity.Mathematics;

namespace CollisionCheck
{
    /// <summary>
    /// Axis-aligned bounding box. We use AABB (not OBB/RSS) for the BVH nodes to keep the
    /// build and per-frame transform code simple. This is looser than an OBB for thin/angled
    /// CAD geometry, which means the broad phase may pass slightly more candidates to the
    /// narrow phase than a tighter volume would -- acceptable here since narrow-phase cost
    /// is already tiny (see NarrowPhaseJob). Swap in an OBB/RSS node type later if profiling
    /// shows the broad phase itself is the bottleneck.
    /// </summary>
    public struct Aabb
    {
        public float3 Min;
        public float3 Max;

        public static Aabb Empty => new Aabb
        {
            Min = new float3(float.MaxValue),
            Max = new float3(float.MinValue)
        };

        public float3 Center => (Min + Max) * 0.5f;
        public float3 Extents => (Max - Min) * 0.5f;

        public void Encapsulate(float3 point)
        {
            Min = math.min(Min, point);
            Max = math.max(Max, point);
        }

        public void Encapsulate(Aabb other)
        {
            Min = math.min(Min, other.Min);
            Max = math.max(Max, other.Max);
        }

        public static Aabb FromTriangle(float3 a, float3 b, float3 c)
        {
            Aabb box = Empty;
            box.Encapsulate(a);
            box.Encapsulate(b);
            box.Encapsulate(c);
            return box;
        }

        public static bool Overlaps(in Aabb a, in Aabb b)
        {
            return a.Min.x <= b.Max.x && a.Max.x >= b.Min.x
                && a.Min.y <= b.Max.y && a.Max.y >= b.Min.y
                && a.Min.z <= b.Max.z && a.Max.z >= b.Min.z;
        }

        /// <summary>
        /// Transforms a local-space AABB by a world matrix by transforming all 8 corners and
        /// re-fitting. This is the standard, cheap way to keep an AABB valid under rotation
        /// (an AABB does not "rotate" -- it has to be recomputed). Called once per probe BVH
        /// node per frame; see BvhBoundsTransformJob.
        /// </summary>
        public static Aabb Transform(in Aabb local, in float4x4 matrix)
        {
            Aabb result = Empty;
            for (int i = 0; i < 8; i++)
            {
                float3 corner = new float3(
                    (i & 1) == 0 ? local.Min.x : local.Max.x,
                    (i & 2) == 0 ? local.Min.y : local.Max.y,
                    (i & 4) == 0 ? local.Min.z : local.Max.z);
                float3 world = math.transform(matrix, corner);
                result.Encapsulate(world);
            }
            return result;
        }
    }
}
