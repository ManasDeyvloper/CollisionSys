using Unity.Mathematics;

namespace CollisionCheck
{
    /// <summary>
    /// Exact triangle-triangle intersection (SAT) and distance routines. Static, Burst-friendly
    /// (no managed allocations, no branches on managed types), used from NarrowPhaseJob.
    ///
    /// This mirrors the source spec directly:
    /// - Intersect: 11 separating axes = 2 face normals + 9 edge-cross-products.
    /// - Distance (only when not intersecting): min over 9 edge-edge segment distances and
    ///   6 vertex-triangle distances (3 verts of A to triangle B, 3 verts of B to triangle A).
    /// Distance is returned as a positive clearance gap; SignedDistance() below folds in the
    /// intersecting case as a negative "penetration" value using deepest-axis overlap, since a
    /// fit-check tool needs a graded number, not a boolean.
    /// </summary>
    public static class TriangleTests
    {
        private const float Epsilon = 1e-8f;

        public static bool Intersects(float3 a0, float3 a1, float3 a2, float3 b0, float3 b1, float3 b2)
        {
            float3 ae0 = a1 - a0, ae1 = a2 - a1, ae2 = a0 - a2;
            float3 be0 = b1 - b0, be1 = b2 - b1, be2 = b0 - b2;

            float3 normalA = math.cross(ae0, ae1);
            float3 normalB = math.cross(be0, be1);

            // 2 face-normal axes.
            if (SeparatedOnAxis(normalA, a0, a1, a2, b0, b1, b2)) return false;
            if (SeparatedOnAxis(normalB, a0, a1, a2, b0, b1, b2)) return false;

            // 9 edge-cross-product axes.
            Span3 aEdges = new Span3(ae0, ae1, ae2);
            Span3 bEdges = new Span3(be0, be1, be2);

            for (int i = 0; i < 3; i++)
            {
                for (int j = 0; j < 3; j++)
                {
                    float3 axis = math.cross(aEdges[i], bEdges[j]);
                    // Skip near-parallel edge pairs (near-zero cross product) -- the coplanar
                    // case is already covered by the two face-normal axes plus the other
                    // edge-cross axes, per the source spec.
                    if (math.lengthsq(axis) < Epsilon) continue;
                    if (SeparatedOnAxis(axis, a0, a1, a2, b0, b1, b2)) return false;
                }
            }

            return true; // no separating axis found
        }

        /// <summary>
        /// Positive = clearance gap between two non-intersecting triangles. Only meaningful
        /// when Intersects() is false; call that first (NarrowPhaseJob does).
        /// </summary>
        public static float Distance(float3 a0, float3 a1, float3 a2, float3 b0, float3 b1, float3 b2)
        {
            float best = float.MaxValue;

            // 9 edge-edge segment distances.
            Span3 aVerts = new Span3(a0, a1, a2);
            Span3 bVerts = new Span3(b0, b1, b2);

            for (int i = 0; i < 3; i++)
            {
                float3 p0 = aVerts[i];
                float3 p1 = aVerts[(i + 1) % 3];
                for (int j = 0; j < 3; j++)
                {
                    float3 q0 = bVerts[j];
                    float3 q1 = bVerts[(j + 1) % 3];
                    best = math.min(best, SegmentSegmentDistance(p0, p1, q0, q1));
                }
            }

            // 3 vertex(A)-to-triangle(B) distances.
            for (int i = 0; i < 3; i++)
                best = math.min(best, math.distance(aVerts[i], ClosestPointOnTriangle(aVerts[i], b0, b1, b2)));

            // 3 vertex(B)-to-triangle(A) distances.
            for (int i = 0; i < 3; i++)
                best = math.min(best, math.distance(bVerts[i], ClosestPointOnTriangle(bVerts[i], a0, a1, a2)));

            return best;
        }

        /// <summary>
        /// Convenience for the narrow-phase job: positive clearance if separated, negative
        /// penetration depth if intersecting (approximated as the negative of the smallest
        /// axis overlap found while proving intersection -- adequate for a color-gradient
        /// display; not a substitute for a physics-grade penetration solver).
        /// </summary>
        public static float SignedDistance(float3 a0, float3 a1, float3 a2, float3 b0, float3 b1, float3 b2)
        {
            if (!Intersects(a0, a1, a2, b0, b1, b2))
                return Distance(a0, a1, a2, b0, b1, b2);

            return -MinPenetrationDepth(a0, a1, a2, b0, b1, b2);
        }

        private static float MinPenetrationDepth(float3 a0, float3 a1, float3 a2, float3 b0, float3 b1, float3 b2)
        {
            float3 ae0 = a1 - a0, ae1 = a2 - a1, ae2 = a0 - a2;
            float3 be0 = b1 - b0, be1 = b2 - b1, be2 = b0 - b2;

            float3 normalA = math.cross(ae0, ae1);
            float3 normalB = math.cross(be0, be1);

            float minOverlap = float.MaxValue;
            minOverlap = math.min(minOverlap, AxisOverlap(normalA, a0, a1, a2, b0, b1, b2));
            minOverlap = math.min(minOverlap, AxisOverlap(normalB, a0, a1, a2, b0, b1, b2));

            Span3 aEdges = new Span3(ae0, ae1, ae2);
            Span3 bEdges = new Span3(be0, be1, be2);
            for (int i = 0; i < 3; i++)
            {
                for (int j = 0; j < 3; j++)
                {
                    float3 axis = math.cross(aEdges[i], bEdges[j]);
                    if (math.lengthsq(axis) < Epsilon) continue;
                    minOverlap = math.min(minOverlap, AxisOverlap(axis, a0, a1, a2, b0, b1, b2));
                }
            }
            return minOverlap;
        }

        private static float AxisOverlap(float3 axis, float3 a0, float3 a1, float3 a2, float3 b0, float3 b1, float3 b2)
        {
            float lenSq = math.lengthsq(axis);
            if (lenSq < Epsilon) return float.MaxValue; // degenerate axis contributes nothing
            float3 n = axis * math.rsqrt(lenSq);

            Project(n, a0, a1, a2, out float aMin, out float aMax);
            Project(n, b0, b1, b2, out float bMin, out float bMax);

            return math.min(aMax, bMax) - math.max(aMin, bMin);
        }

        private static bool SeparatedOnAxis(float3 axis, float3 a0, float3 a1, float3 a2, float3 b0, float3 b1, float3 b2)
        {
            if (math.lengthsq(axis) < Epsilon) return false; // degenerate axis, skip

            Project(axis, a0, a1, a2, out float aMin, out float aMax);
            Project(axis, b0, b1, b2, out float bMin, out float bMax);

            return aMax < bMin || bMax < aMin;
        }

        private static void Project(float3 axis, float3 v0, float3 v1, float3 v2, out float min, out float max)
        {
            float p0 = math.dot(axis, v0);
            float p1 = math.dot(axis, v1);
            float p2 = math.dot(axis, v2);
            min = math.min(p0, math.min(p1, p2));
            max = math.max(p0, math.max(p1, p2));
        }

        private static float SegmentSegmentDistance(float3 p0, float3 p1, float3 q0, float3 q1)
        {
            float3 d1 = p1 - p0;
            float3 d2 = q1 - q0;
            float3 r = p0 - q0;

            float a = math.dot(d1, d1);
            float e = math.dot(d2, d2);
            float f = math.dot(d2, r);

            float s, t;

            if (a <= Epsilon && e <= Epsilon)
            {
                return math.distance(p0, q0);
            }
            if (a <= Epsilon)
            {
                s = 0f;
                t = math.clamp(f / e, 0f, 1f);
            }
            else
            {
                float c = math.dot(d1, r);
                if (e <= Epsilon)
                {
                    t = 0f;
                    s = math.clamp(-c / a, 0f, 1f);
                }
                else
                {
                    float b = math.dot(d1, d2);
                    float denom = a * e - b * b;

                    s = denom > Epsilon ? math.clamp((b * f - c * e) / denom, 0f, 1f) : 0f;
                    t = (b * s + f) / e;

                    if (t < 0f) { t = 0f; s = math.clamp(-c / a, 0f, 1f); }
                    else if (t > 1f) { t = 1f; s = math.clamp((b - c) / a, 0f, 1f); }
                }
            }

            float3 closestOnP = p0 + d1 * s;
            float3 closestOnQ = q0 + d2 * t;
            return math.distance(closestOnP, closestOnQ);
        }

        private static float3 ClosestPointOnTriangle(float3 p, float3 a, float3 b, float3 c)
        {
            // Standard Ericson (Real-Time Collision Detection) closest-point-on-triangle,
            // via barycentric region tests -- result is always clamped to the triangle.
            float3 ab = b - a;
            float3 ac = c - a;
            float3 ap = p - a;

            float d1 = math.dot(ab, ap);
            float d2 = math.dot(ac, ap);
            if (d1 <= 0f && d2 <= 0f) return a;

            float3 bp = p - b;
            float d3 = math.dot(ab, bp);
            float d4 = math.dot(ac, bp);
            if (d3 >= 0f && d4 <= d3) return b;

            float vc = d1 * d4 - d3 * d2;
            if (vc <= 0f && d1 >= 0f && d3 <= 0f)
            {
                float v = d1 / (d1 - d3);
                return a + v * ab;
            }

            float3 cp = p - c;
            float d5 = math.dot(ab, cp);
            float d6 = math.dot(ac, cp);
            if (d6 >= 0f && d5 <= d6) return c;

            float vb = d5 * d2 - d1 * d6;
            if (vb <= 0f && d2 >= 0f && d6 <= 0f)
            {
                float w = d2 / (d2 - d6);
                return a + w * ac;
            }

            float va = d3 * d6 - d5 * d4;
            if (va <= 0f && (d4 - d3) >= 0f && (d5 - d6) >= 0f)
            {
                float w = (d4 - d3) / ((d4 - d3) + (d5 - d6));
                return b + w * (c - b);
            }

            float denom = 1f / (va + vb + vc);
            float vv = vb * denom;
            float ww = vc * denom;
            return a + ab * vv + ac * ww;
        }

        /// <summary>Tiny fixed-size 3-float3 accessor so edges/verts can be indexed in a loop
        /// without a managed array allocation (Burst-compiled code can't allocate on GC heap).</summary>
        private readonly struct Span3
        {
            private readonly float3 _v0, _v1, _v2;
            public Span3(float3 v0, float3 v1, float3 v2) { _v0 = v0; _v1 = v1; _v2 = v2; }
            public float3 this[int i] => i == 0 ? _v0 : (i == 1 ? _v1 : _v2);
        }
    }
}
