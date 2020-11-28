﻿using System;
using System.Diagnostics;
using System.Numerics;
using Box2DSharp.Common;

namespace Box2DSharp.Ropes
{
    public class Rope
    {
        private RopeBend[] _bendConstraints;

        private int _bendCount;

        private V2[] _bindPositions;

        private int _count;

        private V2 _gravity;

        private F[] _invMasses;

        private V2[] _p0s;

        private V2 _position;

        private V2[] _ps;

        private RopeStretch[] _stretchConstraints;

        private int _stretchCount;

        private RopeTuning _tuning;

        private V2[] _vs;

        public void Create(in RopeDef def)
        {
            Debug.Assert(def.Count >= 3);
            _position = def.Position;
            _count = def.Count;
            _bindPositions = new V2[_count];
            _ps = new V2[_count];
            _p0s = new V2[_count];
            _vs = new V2[_count];
            _invMasses = new F[_count];

            for (var i = 0; i < _count; ++i)
            {
                _bindPositions[i] = def.Vertices[i];
                _ps[i] = def.Vertices[i] + _position;
                _p0s[i] = def.Vertices[i] + _position;
                _vs[i].SetZero();

                var m = def.Masses[i];
                if (m > F.Zero)
                {
                    _invMasses[i] = F.One / m;
                }
                else
                {
                    _invMasses[i] = F.Zero;
                }
            }

            _stretchCount = _count - 1;
            _bendCount = _count - 2;

            _stretchConstraints = new RopeStretch[_stretchCount];
            _bendConstraints = new RopeBend[_bendCount];

            for (var i = 0; i < _stretchCount; ++i)
            {
                ref var c = ref _stretchConstraints[i];
                var p1 = _ps[i];
                var p2 = _ps[i + 1];

                c.I1 = i;
                c.I2 = i + 1;
                c.L = V2.Distance(p1, p2);
                c.InvMass1 = _invMasses[i];
                c.InvMass2 = _invMasses[i + 1];
                c.Lambda = F.Zero;
                c.Damper = F.Zero;
                c.Spring = F.Zero;
            }

            for (var i = 0; i < _bendCount; ++i)
            {
                ref var c = ref _bendConstraints[i];

                var p1 = _ps[i];
                var p2 = _ps[i + 1];
                var p3 = _ps[i + 2];

                c.i1 = i;
                c.i2 = i + 1;
                c.i3 = i + 2;
                c.invMass1 = _invMasses[i];
                c.invMass2 = _invMasses[i + 1];
                c.invMass3 = _invMasses[i + 2];
                c.invEffectiveMass = F.Zero;
                c.L1 = V2.Distance(p1, p2);
                c.L2 = V2.Distance(p2, p3);
                c.lambda = F.Zero;

                // Pre-compute effective mass (TODO use flattened config)
                var e1 = p2 - p1;
                var e2 = p3 - p2;
                var l1Sqr = e1.LengthSquared();
                var l2Sqr = e2.LengthSquared();

                if ((l1Sqr * l2Sqr).Equals(0))
                {
                    continue;
                }

                var jd1 = -F.One / l1Sqr * e1.Skew();
                var jd2 = F.One / l2Sqr * e2.Skew();

                var j1 = -jd1;
                var j2 = jd1 - jd2;
                var j3 = jd2;

                c.invEffectiveMass = c.invMass1 * V2.Dot(j1, j1) + c.invMass2 * V2.Dot(j2, j2) + c.invMass3 * V2.Dot(j3, j3);

                var r = p3 - p1;

                var rr = r.LengthSquared();
                if (rr.Equals(0))
                {
                    continue;
                }

                // a1 = h2 / (h1 + h2)
                // a2 = h1 / (h1 + h2)
                c.alpha1 = V2.Dot(e2, r) / rr;
                c.alpha2 = V2.Dot(e1, r) / rr;
            }

            _gravity = def.Gravity;

            SetTuning(def.Tuning);
        }

        public void SetTuning(RopeTuning tuning)
        {
            _tuning = tuning;

            // Pre-compute spring and damper values based on tuning

            var bendOmega = F.Two * Settings.Pi * _tuning.BendHertz;

            for (var i = 0; i < _bendCount; ++i)
            {
                ref var c = ref _bendConstraints[i];

                var l1Sqr = c.L1 * c.L1;
                var l2Sqr = c.L2 * c.L2;

                if ((l1Sqr * l2Sqr).Equals(0))
                {
                    c.spring = F.Zero;
                    c.damper = F.Zero;
                    continue;
                }

                // Flatten the triangle formed by the two edges
                var j2 = F.One / c.L1 + F.One / c.L2;
                var sum = c.invMass1 / l1Sqr + c.invMass2 * j2 * j2 + c.invMass3 / l2Sqr;
                if (sum.Equals(0))
                {
                    c.spring = F.Zero;
                    c.damper = F.Zero;
                    continue;
                }

                var mass = F.One / sum;

                c.spring = mass * bendOmega * bendOmega;
                c.damper = F.Two * mass * _tuning.BendDamping * bendOmega;
            }

            var stretchOmega = F.Two * Settings.Pi * _tuning.StretchHertz;

            for (var i = 0; i < _stretchCount; ++i)
            {
                ref var c = ref _stretchConstraints[i];

                var sum = c.InvMass1 + c.InvMass2;
                if (sum.Equals(0))
                {
                    continue;
                }

                var mass = F.One / sum;

                c.Spring = mass * stretchOmega * stretchOmega;
                c.Damper = F.Two * mass * _tuning.StretchDamping * stretchOmega;
            }
        }

        public void Step(F dt, int iterations, V2 position)
        {
            if (dt.Equals(0))
            {
                return;
            }

            var invDt = F.One / dt;

            var d = Math.Exp(-dt * _tuning.Damping);

            // Apply gravity and damping
            for (var i = 0; i < _count; ++i)
            {
                if (_invMasses[i] > F.Zero)
                {
                    _vs[i] *= d;
                    _vs[i] += dt * _gravity;
                }
                else
                {
                    _vs[i] = invDt * (_bindPositions[i] + position - _p0s[i]);
                }
            }

            // Apply bending spring
            if (_tuning.BendingModel == BendingModel.SpringAngleBendingModel)
            {
                ApplyBendForces(dt);
            }

            for (var i = 0; i < _bendCount; ++i)
            {
                _bendConstraints[i].lambda = F.Zero;
            }

            for (var i = 0; i < _stretchCount; ++i)
            {
                _stretchConstraints[i].Lambda = F.Zero;
            }

            // Update position
            for (var i = 0; i < _count; ++i)
            {
                _ps[i] += dt * _vs[i];
            }

            // Solve constraints
            for (var i = 0; i < iterations; ++i)
            {
                switch (_tuning.BendingModel)
                {
                case BendingModel.PbdAngleBendingModel:
                    SolveBend_PBD_Angle();
                    break;
                case BendingModel.XpdAngleBendingModel:
                    SolveBend_XPBD_Angle(dt);
                    break;
                case BendingModel.PbdDistanceBendingModel:
                    SolveBend_PBD_Distance();
                    break;
                case BendingModel.PbdHeightBendingModel:
                    SolveBend_PBD_Height();
                    break;
                }

                switch (_tuning.StretchingModel)
                {
                case StretchingModel.PbdStretchingModel:
                    SolveStretch_PBD();
                    break;
                case StretchingModel.XpbdStretchingModel:
                    SolveStretch_XPBD(dt);
                    break;
                }
            }

            // Constrain velocity
            for (var i = 0; i < _count; ++i)
            {
                _vs[i] = invDt * (_ps[i] - _p0s[i]);
                _p0s[i] = _ps[i];
            }
        }

        public void Reset(V2 position)
        {
            _position = position;

            for (var i = 0; i < _count; ++i)
            {
                _ps[i] = _bindPositions[i] + _position;
                _p0s[i] = _bindPositions[i] + _position;
                _vs[i].SetZero();
            }

            for (var i = 0; i < _bendCount; ++i)
            {
                _bendConstraints[i].lambda = F.Zero;
            }

            for (var i = 0; i < _stretchCount; ++i)
            {
                _stretchConstraints[i].Lambda = F.Zero;
            }
        }

        private void SolveStretch_PBD()
        {
            var stiffness = _tuning.StretchStiffness;
            for (var i = 0; i < _stretchCount; ++i)
            {
                ref var c = ref _stretchConstraints[i];

                var p1 = _ps[c.I1];
                var p2 = _ps[c.I2];

                var d = p2 - p1;
                var l = d.Normalize();

                var sum = c.InvMass1 + c.InvMass2;
                if (sum.Equals(0))
                {
                    continue;
                }

                var s1 = c.InvMass1 / sum;
                var s2 = c.InvMass2 / sum;

                p1 -= stiffness * s1 * (c.L - l) * d;
                p2 += stiffness * s2 * (c.L - l) * d;

                _ps[c.I1] = p1;
                _ps[c.I2] = p2;
            }
        }

        private void SolveStretch_XPBD(F dt)
        {
            Debug.Assert(dt > F.Zero);
            for (var i = 0; i < _stretchCount; ++i)
            {
                ref var ropeStretch = ref _stretchConstraints[i];

                var p1 = _ps[ropeStretch.I1];
                var p2 = _ps[ropeStretch.I2];

                var dp1 = p1 - _p0s[ropeStretch.I1];
                var dp2 = p2 - _p0s[ropeStretch.I2];

                var u = p2 - p1;
                var l = u.Normalize();

                var j1 = -u;
                var j2 = u;

                var sum = ropeStretch.InvMass1 + ropeStretch.InvMass2;
                if (sum.Equals(0))
                {
                    continue;
                }

                var alpha = F.One / (ropeStretch.Spring * dt * dt); // 1 / kg
                var beta = dt * dt * ropeStretch.Damper;           // kg * s
                var sigma = alpha * beta / dt;                     // non-dimensional
                var stretchL = l - ropeStretch.L;

                // This is using the initial velocities
                var cDot = V2.Dot(j1, dp1) + V2.Dot(j2, dp2);

                var b = stretchL + alpha * ropeStretch.Lambda + sigma * cDot;
                var sum2 = (F.One + sigma) * sum + alpha;

                var impulse = -b / sum2;

                p1 += ropeStretch.InvMass1 * impulse * j1;
                p2 += ropeStretch.InvMass2 * impulse * j2;

                _ps[ropeStretch.I1] = p1;
                _ps[ropeStretch.I2] = p2;
                ropeStretch.Lambda += impulse;
            }
        }

        private void SolveBend_PBD_Angle()
        {
            var stiffness = _tuning.BendStiffness;
            for (var i = 0; i < _bendCount; ++i)
            {
                ref var c = ref _bendConstraints[i];

                var p1 = _ps[c.i1];
                var p2 = _ps[c.i2];
                var p3 = _ps[c.i3];

                var d1 = p2 - p1;
                var d2 = p3 - p2;
                var a = MathUtils.Cross(d1, d2);
                var b = V2.Dot(d1, d2);

                var angle = F.Atan2(a, b);

                F L1sqr, L2sqr;

                if (_tuning.Isometric)
                {
                    L1sqr = c.L1 * c.L1;
                    L2sqr = c.L2 * c.L2;
                }
                else
                {
                    L1sqr = d1.LengthSquared();
                    L2sqr = d2.LengthSquared();
                }

                if ((L1sqr * L2sqr).Equals(0))
                {
                    continue;
                }

                var Jd1 = -F.One / L1sqr * d1.Skew();
                var Jd2 = F.One / L2sqr * d2.Skew();

                var J1 = -Jd1;
                var J2 = Jd1 - Jd2;
                var J3 = Jd2;

                F sum;
                if (_tuning.FixedEffectiveMass)
                {
                    sum = c.invEffectiveMass;
                }
                else
                {
                    sum = c.invMass1 * V2.Dot(J1, J1) + c.invMass2 * V2.Dot(J2, J2) + c.invMass3 * V2.Dot(J3, J3);
                }

                if (sum.Equals(0))
                {
                    sum = c.invEffectiveMass;
                }

                var impulse = -stiffness * angle / sum;

                p1 += c.invMass1 * impulse * J1;
                p2 += c.invMass2 * impulse * J2;
                p3 += c.invMass3 * impulse * J3;

                _ps[c.i1] = p1;
                _ps[c.i2] = p2;
                _ps[c.i3] = p3;
            }
        }

        private void SolveBend_XPBD_Angle(F dt)
        {
            Debug.Assert(dt > F.Zero);
            for (var i = 0; i < _bendCount; ++i)
            {
                ref var c = ref _bendConstraints[i];

                var p1 = _ps[c.i1];
                var p2 = _ps[c.i2];
                var p3 = _ps[c.i3];

                var dp1 = p1 - _p0s[c.i1];
                var dp2 = p2 - _p0s[c.i2];
                var dp3 = p3 - _p0s[c.i3];

                var d1 = p2 - p1;
                var d2 = p3 - p2;

                F L1sqr, L2sqr;

                if (_tuning.Isometric)
                {
                    L1sqr = c.L1 * c.L1;
                    L2sqr = c.L2 * c.L2;
                }
                else
                {
                    L1sqr = d1.LengthSquared();
                    L2sqr = d2.LengthSquared();
                }

                if ((L1sqr * L2sqr).Equals(0))
                {
                    continue;
                }

                var a = MathUtils.Cross(d1, d2);
                var b = V2.Dot(d1, d2);

                var angle = F.Atan2(a, b);

                var Jd1 = -F.One / L1sqr * d1.Skew();
                var Jd2 = F.One / L2sqr * d2.Skew();

                var J1 = -Jd1;
                var J2 = Jd1 - Jd2;
                var J3 = Jd2;

                F sum;
                if (_tuning.FixedEffectiveMass)
                {
                    sum = c.invEffectiveMass;
                }
                else
                {
                    sum = c.invMass1 * V2.Dot(J1, J1) + c.invMass2 * V2.Dot(J2, J2) + c.invMass3 * V2.Dot(J3, J3);
                }

                if (sum.Equals(0))
                {
                    continue;
                }

                var alpha = F.One / (c.spring * dt * dt);
                var beta = dt * dt * c.damper;
                var sigma = alpha * beta / dt;
                var C = angle;

                // This is using the initial velocities
                var Cdot = V2.Dot(J1, dp1) + V2.Dot(J2, dp2) + V2.Dot(J3, dp3);

                var B = C + alpha * c.lambda + sigma * Cdot;
                var sum2 = (F.One + sigma) * sum + alpha;

                var impulse = -B / sum2;

                p1 += c.invMass1 * impulse * J1;
                p2 += c.invMass2 * impulse * J2;
                p3 += c.invMass3 * impulse * J3;

                _ps[c.i1] = p1;
                _ps[c.i2] = p2;
                _ps[c.i3] = p3;
                c.lambda += impulse;
            }
        }

        private void ApplyBendForces(F dt)
        {
            // omega = 2 * pi * hz
            var omega = F.Two * Settings.Pi * _tuning.BendHertz;
            for (var i = 0; i < _bendCount; ++i)
            {
                ref var c = ref _bendConstraints[i];

                var p1 = _ps[c.i1];
                var p2 = _ps[c.i2];
                var p3 = _ps[c.i3];

                var v1 = _vs[c.i1];
                var v2 = _vs[c.i2];
                var v3 = _vs[c.i3];

                var d1 = p2 - p1;
                var d2 = p3 - p2;

                F L1sqr, L2sqr;

                if (_tuning.Isometric)
                {
                    L1sqr = c.L1 * c.L1;
                    L2sqr = c.L2 * c.L2;
                }
                else
                {
                    L1sqr = d1.LengthSquared();
                    L2sqr = d2.LengthSquared();
                }

                if ((L1sqr * L2sqr).Equals(0))
                {
                    continue;
                }

                var a = MathUtils.Cross(d1, d2);
                var b = V2.Dot(d1, d2);

                var angle = F.Atan2(a, b);

                var Jd1 = -F.One / L1sqr * d1.Skew();
                var Jd2 = F.One / L2sqr * d2.Skew();

                var J1 = -Jd1;
                var J2 = Jd1 - Jd2;
                var J3 = Jd2;

                F sum;
                if (_tuning.FixedEffectiveMass)
                {
                    sum = c.invEffectiveMass;
                }
                else
                {
                    sum = c.invMass1 * V2.Dot(J1, J1) + c.invMass2 * V2.Dot(J2, J2) + c.invMass3 * V2.Dot(J3, J3);
                }

                if (sum.Equals(0))
                {
                    continue;
                }

                var mass = F.One / sum;

                var spring = mass * omega * omega;
                var damper = F.Two * mass * _tuning.BendDamping * omega;

                var C = angle;
                var Cdot = V2.Dot(J1, v1) + V2.Dot(J2, v2) + V2.Dot(J3, v3);

                var impulse = -dt * (spring * C + damper * Cdot);

                _vs[c.i1] += c.invMass1 * impulse * J1;
                _vs[c.i2] += c.invMass2 * impulse * J2;
                _vs[c.i3] += c.invMass3 * impulse * J3;
            }
        }

        private void SolveBend_PBD_Distance()
        {
            var stiffness = _tuning.BendStiffness;
            for (var i = 0; i < _bendCount; ++i)
            {
                ref var c = ref _bendConstraints[i];

                var i1 = c.i1;
                var i2 = c.i3;

                var p1 = _ps[i1];
                var p2 = _ps[i2];

                var d = p2 - p1;
                var L = d.Normalize();

                var sum = c.invMass1 + c.invMass3;
                if (sum.Equals(0))
                {
                    continue;
                }

                var s1 = c.invMass1 / sum;
                var s2 = c.invMass3 / sum;

                p1 -= stiffness * s1 * (c.L1 + c.L2 - L) * d;
                p2 += stiffness * s2 * (c.L1 + c.L2 - L) * d;

                _ps[i1] = p1;
                _ps[i2] = p2;
            }
        }

        // Constraint based implementation of:
        // P. Volino: Simple Linear Bending Stiffness in Particle Systems
        private void SolveBend_PBD_Height()
        {
            var stiffness = _tuning.BendStiffness;
            for (var i = 0; i < _bendCount; ++i)
            {
                ref var c = ref _bendConstraints[i];

                var p1 = _ps[c.i1];
                var p2 = _ps[c.i2];
                var p3 = _ps[c.i3];

                // Barycentric coordinates are held constant
                var d = c.alpha1 * p1 + c.alpha2 * p3 - p2;
                var dLen = d.Length();

                if (dLen.Equals(0))
                {
                    continue;
                }

                var dHat = F.One / dLen * d;

                var J1 = c.alpha1 * dHat;
                var J2 = -dHat;
                var J3 = c.alpha2 * dHat;

                var sum = c.invMass1 * c.alpha1 * c.alpha1 + c.invMass2 + c.invMass3 * c.alpha2 * c.alpha2;

                if (sum.Equals(0))
                {
                    continue;
                }

                var C = dLen;
                var mass = F.One / sum;
                var impulse = -stiffness * mass * C;

                p1 += c.invMass1 * impulse * J1;
                p2 += c.invMass2 * impulse * J2;
                p3 += c.invMass3 * impulse * J3;

                _ps[c.i1] = p1;
                _ps[c.i2] = p2;
                _ps[c.i3] = p3;
            }
        }

        public void Draw(IDrawer draw)
        {
            var c = Color.FromArgb(0.4f, F.Half, 0.7f);

            var pg = Color.FromArgb(0.1f, 0.8f, 0.1f);

            var pd = Color.FromArgb(0.7f, 0.2f, 0.4f);
            for (var i = 0; i < _count - 1; ++i)
            {
                draw.DrawSegment(_ps[i], _ps[i + 1], c);

                var pc = _invMasses[i] > F.Zero ? pd : pg;
                draw.DrawPoint(_ps[i], 5.0f, pc);
            }

            {
                var pc = _invMasses[_count - 1] > F.Zero ? pd : pg;
                draw.DrawPoint(_ps[_count - 1], 5.0f, pc);
            }
        }
    }
}