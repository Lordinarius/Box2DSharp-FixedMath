using System;
using System.Diagnostics;
using System.Numerics;
using Box2DSharp.Common;

namespace Box2DSharp.Collision
{
    public static class DistanceAlgorithm
    {
        /// <summary>
        /// GJK碰撞检测
        /// </summary>
        /// <param name="output"></param>
        /// <param name="cache"></param>
        /// <param name="input"></param>
        /// <param name="gJkProfile"></param>
        public static void Distance(
            out DistanceOutput output,
            ref SimplexCache cache,
            in DistanceInput input,
            in GJkProfile gJkProfile = null)
        {
            if (gJkProfile != null)
            {
                ++gJkProfile.GjkCalls;
            }

            output = new DistanceOutput();
            ref readonly var proxyA = ref input.ProxyA;
            ref readonly var proxyB = ref input.ProxyB;

            var transformA = input.TransformA;
            var transformB = input.TransformB;

            // Initialize the simplex.
            var simplex = new Simplex();
            simplex.ReadCache(
                ref cache,
                proxyA,
                transformA,
                proxyB,
                transformB);

            // Get simplex vertices as an array.
            ref var vertices = ref simplex.Vertices;
            const int maxIters = 20;

            // These store the vertices of the last simplex so that we
            // can check for duplicates and prevent cycling.
            Span<int> saveA = stackalloc int[3];
            Span<int> saveB = stackalloc int[3];

            // Main iteration loop.
            var iter = 0;
            while (iter < maxIters)
            {
                // Copy simplex so we can identify duplicates.
                var saveCount = simplex.Count;
                for (var i = 0; i < simplex.Count; ++i)
                {
                    saveA[i] = vertices[i].IndexA;
                    saveB[i] = vertices[i].IndexB;
                }

                switch (simplex.Count)
                {
                case 1:
                    break;

                case 2:
                    simplex.Solve2();
                    break;

                case 3:
                    simplex.Solve3();
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(simplex.Count));
                }

                // If we have 3 points, then the origin is in the corresponding triangle.
                if (simplex.Count == 3)
                {
                    break;
                }

                // Get search direction.
                var d = simplex.GetSearchDirection();

                // Ensure the search direction is numerically fit.
                if (d.LengthSquared() < Settings.Epsilon * Settings.Epsilon)
                {
                    // The origin is probably contained by a line segment
                    // or triangle. Thus the shapes are overlapped.

                    // We can't return zero here even though there may be overlap.
                    // In case the simplex is a point, segment, or triangle it is difficult
                    // to determine if the origin is contained in the CSO or very close to it.
                    break;
                }

                // Compute a tentative new simplex vertex using support points.
                ref var vertex = ref vertices[simplex.Count];
                vertex.IndexA = proxyA.GetSupport(MathUtils.MulT(transformA.Rotation, -d));
                vertex.Wa = MathUtils.Mul(transformA, proxyA.GetVertex(vertex.IndexA));

                vertex.IndexB = proxyB.GetSupport(MathUtils.MulT(transformB.Rotation, d));
                vertex.Wb = MathUtils.Mul(transformB, proxyB.GetVertex(vertex.IndexB));
                vertex.W = vertex.Wb - vertex.Wa;

                // Iteration count is equated to the number of support point calls.
                ++iter;
                if (gJkProfile != null)
                {
                    ++gJkProfile.GjkIters;
                }

                // Check for duplicate support points. This is the main termination criteria.
                var duplicate = false;
                for (var i = 0; i < saveCount; ++i)
                {
                    if (vertex.IndexA == saveA[i] && vertex.IndexB == saveB[i])
                    {
                        duplicate = true;
                        break;
                    }
                }

                // If we found a duplicate support point we must exit to avoid cycling.
                if (duplicate)
                {
                    break;
                }

                // New vertex is ok and needed.
                ++simplex.Count;
            }

            if (gJkProfile != null)
            {
                gJkProfile.GjkMaxIters = F.Max(gJkProfile.GjkMaxIters, iter);
            }

            // Prepare output.
            simplex.GetWitnessPoints(out output.PointA, out output.PointB);
            output.Distance = V2.Distance(output.PointA, output.PointB);
            output.Iterations = iter;

            // Cache the simplex.
            simplex.WriteCache(ref cache);

            // Apply radii if requested.
            if (input.UseRadii)
            {
                var rA = proxyA.Radius;
                var rB = proxyB.Radius;

                if (output.Distance > rA + rB && output.Distance > Settings.Epsilon)
                {
                    // Shapes are still no overlapped.
                    // Move the witness points to the outer surface.
                    output.Distance -= rA + rB;
                    var normal = output.PointB - output.PointA;
                    normal.Normalize();
                    output.PointA += rA * normal;
                    output.PointB -= rB * normal;
                }
                else
                {
                    // Shapes are overlapped when radii are considered.
                    // Move the witness points to the middle.
                    var p = F.Half * (output.PointA + output.PointB);
                    output.PointA = p;
                    output.PointB = p;
                    output.Distance = F.Zero;
                }
            }
        }

        static readonly F tolerance = F.Half * Settings.LinearSlop;
        public static bool ShapeCast(out ShapeCastOutput output, in ShapeCastInput input)
        {
            output = new ShapeCastOutput
            {
                Iterations = 0,
                Lambda = F.One,
                Normal = V2.Zero,
                Point = V2.Zero
            };

            ref readonly var proxyA = ref input.ProxyA;
            ref readonly var proxyB = ref input.ProxyB;

            var radiusA = F.Max(proxyA.Radius, Settings.PolygonRadius);
            var radiusB = F.Max(proxyB.Radius, Settings.PolygonRadius);
            var radius = radiusA + radiusB;

            var xfA = input.TransformA;
            var xfB = input.TransformB;

            var r = input.TranslationB;
            var n = new V2(F.Zero, F.Zero);
            var lambda = F.Zero;

            // Initial simplex
            var simplex = new Simplex();

            // Get simplex vertices as an array.
            // ref var vertices = ref simplex.Vertices;

            // Get support point in -r direction
            var indexA = proxyA.GetSupport(MathUtils.MulT(xfA.Rotation, -r));
            var wA = MathUtils.Mul(xfA, proxyA.GetVertex(indexA));
            var indexB = proxyB.GetSupport(MathUtils.MulT(xfB.Rotation, r));
            var wB = MathUtils.Mul(xfB, proxyB.GetVertex(indexB));
            var v = wA - wB;

            // Sigma is the target distance between polygons
            var sigma = F.Max(Settings.PolygonRadius, radius - Settings.PolygonRadius);

            // Main iteration loop.
            // 迭代次数上限
            const int maxIters = 20;
            var iter = 0;
            while (iter < maxIters && F.Abs(v.Length() - sigma) > tolerance)
            {
                Debug.Assert(simplex.Count < 3);

                output.Iterations += 1;

                // Support in direction -v (A - B)
                indexA = proxyA.GetSupport(MathUtils.MulT(xfA.Rotation, -v));
                wA = MathUtils.Mul(xfA, proxyA.GetVertex(indexA));
                indexB = proxyB.GetSupport(MathUtils.MulT(xfB.Rotation, v));
                wB = MathUtils.Mul(xfB, proxyB.GetVertex(indexB));
                var p = wA - wB;

                // -v is a normal at p
                v.Normalize();

                // Intersect ray with plane
                var vp = V2.Dot(v, p);
                var vr = V2.Dot(v, r);
                if (vp - sigma > lambda * vr)
                {
                    if (vr <= F.Zero)
                    {
                        return false;
                    }

                    lambda = (vp - sigma) / vr;
                    if (lambda > F.One)
                    {
                        return false;
                    }

                    n = -v;
                    simplex.Count = 0;
                }

                // Reverse simplex since it works with B - A.
                // Shift by lambda * r because we want the closest point to the current clip point.
                // Note that the support point p is not shifted because we want the plane equation
                // to be formed in unshifted space.
                ref var vertex = ref simplex.Vertices[simplex.Count];
                vertex.IndexA = indexB;
                vertex.Wa = wB + lambda * r;
                vertex.IndexB = indexA;
                vertex.Wb = wA;
                vertex.W = vertex.Wb - vertex.Wa;
                vertex.A = F.One;

                simplex.Count += 1;

                switch (simplex.Count)
                {
                case 1:
                    break;

                case 2:
                    simplex.Solve2();
                    break;

                case 3:
                    simplex.Solve3();
                    break;

                default:
                    Debug.Assert(false);
                    break;
                }

                // If we have 3 points, then the origin is in the corresponding triangle.
                if (simplex.Count == 3)
                {
                    // Overlap
                    return false;
                }

                // Get search direction.
                v = simplex.GetClosestPoint();

                // Iteration count is equated to the number of support point calls.
                ++iter;
            }

            // Prepare output.
            //V2 pointA, pointB;
            simplex.GetWitnessPoints(out var pointB, out var pointA);

            if (v.LengthSquared() > F.Zero)
            {
                n = -v;
                n.Normalize();
            }

            output.Point = pointA + radiusA * n;
            output.Normal = n;
            output.Lambda = lambda;
            output.Iterations = iter;
            return true;
        }
    }
}