using System.Numerics;
using Box2DSharp.Common;

namespace Box2DSharp.Dynamics.Joints
{
    /// Mouse joint definition. This requires a world target point,
    /// tuning parameters, and the time step.
    public class MouseJointDef : JointDef
    {
        /// The damping ratio. 0 = no damping, 1 = critical damping.
        public F DampingRatio;

        /// The response speed.
        public F FrequencyHz;

        /// The maximum constraint force that can be exerted
        /// to move the candidate body. Usually you will express
        /// as some multiple of the weight (multiplier * mass * gravity).
        public F MaxForce;

        /// The initial world target point. This is assumed
        /// to coincide with the body anchor initially.
        public V2 Target;

        public MouseJointDef()
        {
            JointType = JointType.MouseJoint;
            Target.Set(F.Zero, F.Zero);
            MaxForce = F.Zero;
            FrequencyHz = new F(21474836480L); //5.0f;
            DampingRatio = new F(3006477056L); //0.7f;
        }
    }
}