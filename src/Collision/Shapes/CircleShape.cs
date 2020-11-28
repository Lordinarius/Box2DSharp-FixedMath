using System;
using System.Numerics;
using Box2DSharp.Collision.Collider;
using Box2DSharp.Common;

namespace Box2DSharp.Collision.Shapes
{
    /// <summary>
    /// A solid circle shape
    /// </summary>
    public class CircleShape : Shape
    {
        /// Position
        public V2 Position;

        public new F Radius
        {
            get => base.Radius;
            set => base.Radius = value;
        }

        public CircleShape()
        {
            ShapeType = ShapeType.Circle;
            Radius = F.Zero;
            Position.SetZero();
        }

        /// Implement b2Shape.
        public override Shape Clone()
        {
            var clone = new CircleShape {Position = Position, Radius = Radius};
            return clone;
        }

        /// @see b2Shape::GetChildCount
        public override int GetChildCount()
        {
            return 1;
        }

        /// Implement b2Shape.
        public override bool TestPoint(in Transform transform, in V2 p)
        {
            var center = transform.Position + MathUtils.Mul(transform.Rotation, Position);
            var d = p - center;
            return V2.Dot(d, d) <= Radius * Radius;
        }

        /// <summary>
        /// Implement b2Shape.
        /// @note because the circle is solid, rays that start inside do not hit because the normal is
        /// not defined.
        /// </summary>
        /// <param name="output"></param>
        /// <param name="input"></param>
        /// <param name="transform"></param>
        /// <param name="childIndex"></param>
        /// <returns></returns>
        public override bool RayCast(
            out RayCastOutput output,
            in RayCastInput input,
            in Transform transform,
            int childIndex)
        {
            output = default;
            var position = transform.Position + MathUtils.Mul(transform.Rotation, Position);
            var s = input.P1 - position;
            var b = V2.Dot(s, s) - Radius * Radius;

            // Solve quadratic equation.
            var r = input.P2 - input.P1;
            var c = V2.Dot(s, r);
            var rr = V2.Dot(r, r);
            var sigma = c * c - rr * b;

            // Check for negative discriminant and short segment.
            if (sigma < F.Zero || rr < Settings.Epsilon)
            {
                return false;
            }

            // Find the point of intersection of the line with the circle.
            var a = -(c + (F) Math.Sqrt(sigma));

            // Is the intersection point on the segment?
            if (F.Zero <= a && a <= input.MaxFraction * rr)
            {
                a /= rr;
                output = new RayCastOutput {Fraction = a, Normal = s + a * r};
                output.Normal.Normalize();
                return true;
            }

            return false;
        }

        /// @see b2Shape::ComputeAABB
        public override void ComputeAABB(
            out AABB aabb,
            in Transform transform,
            int
                childIndex)
        {
            var p = transform.Position + MathUtils.Mul(transform.Rotation, Position);
            aabb = new AABB();
            aabb.LowerBound.Set(p.X - Radius, p.Y - Radius);
            aabb.UpperBound.Set(p.X + Radius, p.Y + Radius);
        }

        /// @see b2Shape::ComputeMass
        public override void ComputeMass(out MassData massData, F density)
        {
            massData = new MassData {Mass = density * Settings.Pi * Radius * Radius, Center = Position};

            // inertia about the local origin
            massData.RotationInertia = massData.Mass * (F.Half * Radius * Radius + V2.Dot(Position, Position));
        }
    }
}