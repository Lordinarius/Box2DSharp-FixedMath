using System;
using Box2DSharp.Collision.Shapes;
using Box2DSharp.Common;
using Box2DSharp.Dynamics;
using Box2DSharp.Dynamics.Joints;
using OpenToolkit.Windowing.Common;
using OpenToolkit.Windowing.Common.Input;
using Testbed.Basics;
using Vector2 = System.Numerics.Vector2;

namespace Testbed.Tests
{
    [TestCase("Examples", "Theo Jansen")]
    public class TheoJansen : Test
    {
        private Body _chassis;

        private RevoluteJoint _motorJoint;

        private bool _motorOn;

        private float _motorSpeed;

        private V2 _offset;

        private Body _wheel;

        public TheoJansen()
        {
            _offset.Set(0.0f, 8.0f);
            _motorSpeed = 2.0f;
            _motorOn = true;
            var pivot = new V2(0.0f, 0.8f);

            // Ground
            {
                var bd = new BodyDef();
                var ground = World.CreateBody(bd);

                var shape = new EdgeShape();
                shape.SetTwoSided(new V2(-50.0f, 0.0f), new V2(50.0f, 0.0f));
                ground.CreateFixture(shape, 0.0f);

                shape.SetTwoSided(new V2(-50.0f, 0.0f), new V2(-50.0f, 10.0f));
                ground.CreateFixture(shape, 0.0f);

                shape.SetTwoSided(new V2(50.0f, 0.0f), new V2(50.0f, 10.0f));
                ground.CreateFixture(shape, 0.0f);
            }

            // Balls
            for (var i = 0; i < 40; ++i)
            {
                var shape = new CircleShape();
                shape.Radius = 0.25f;

                var bd = new BodyDef();
                bd.BodyType = BodyType.DynamicBody;
                bd.Position.Set(-40.0f + 2.0f * i, 0.5f);

                var body = World.CreateBody(bd);
                body.CreateFixture(shape, 1.0f);
            }

            // Chassis
            {
                var shape = new PolygonShape();
                shape.SetAsBox(2.5f, 1.0f);

                var sd = new FixtureDef();
                sd.Density = 1.0f;
                sd.Shape = shape;
                sd.Filter.GroupIndex = -1;
                var bd = new BodyDef();
                bd.BodyType = BodyType.DynamicBody;
                bd.Position = pivot + _offset;
                _chassis = World.CreateBody(bd);
                _chassis.CreateFixture(sd);
            }

            {
                var shape = new CircleShape();
                shape.Radius = 1.6f;
                var sd = new FixtureDef();
                sd.Density = 1.0f;
                sd.Shape = shape;
                sd.Filter.GroupIndex = -1;
                var bd = new BodyDef();
                bd.BodyType = BodyType.DynamicBody;
                bd.Position = pivot + _offset;
                _wheel = World.CreateBody(bd);
                _wheel.CreateFixture(sd);
            }

            {
                var jd = new RevoluteJointDef();
                jd.Initialize(_wheel, _chassis, pivot + _offset);
                jd.CollideConnected = false;
                jd.MotorSpeed = _motorSpeed;
                jd.MaxMotorTorque = 400.0f;
                jd.EnableMotor = _motorOn;
                _motorJoint = (RevoluteJoint)World.CreateJoint(jd);
            }

            V2 wheelAnchor;

            wheelAnchor = pivot + new V2(0.0f, -0.8f);

            CreateLeg(-1.0f, wheelAnchor);
            CreateLeg(1.0f, wheelAnchor);

            _wheel.SetTransform(_wheel.GetPosition(), 120.0f * (float)Math.PI / 180.0f);
            CreateLeg(-1.0f, wheelAnchor);
            CreateLeg(1.0f, wheelAnchor);

            _wheel.SetTransform(_wheel.GetPosition(), -120.0f * (float)Math.PI / 180.0f);
            CreateLeg(-1.0f, wheelAnchor);
            CreateLeg(1.0f, wheelAnchor);
        }

        private void CreateLeg(float s, V2 wheelAnchor)
        {
            var p1 = new V2(5.4f * s, -6.1f);
            var p2 = new V2(7.2f * s, -1.2f);
            var p3 = new V2(4.3f * s, -1.9f);
            var p4 = new V2(3.1f * s, 0.8f);
            var p5 = new V2(6.0f * s, 1.5f);
            var p6 = new V2(2.5f * s, 3.7f);

            var fd1 = new FixtureDef {Filter = {GroupIndex = -1}, Density = 1.0f};
            var fd2 = new FixtureDef {Filter = {GroupIndex = -1}, Density = 1.0f};

            var poly1 = new PolygonShape();
            var poly2 = new PolygonShape();

            if (s > 0.0f)
            {
                var vertices = new V2[3];

                vertices[0] = p1;
                vertices[1] = p2;
                vertices[2] = p3;
                poly1.Set(vertices);

                vertices[0] = V2.Zero;
                vertices[1] = p5 - p4;
                vertices[2] = p6 - p4;
                poly2.Set(vertices);
            }
            else
            {
                var vertices = new V2[3];

                vertices[0] = p1;
                vertices[1] = p3;
                vertices[2] = p2;
                poly1.Set(vertices);

                vertices[0] = V2.Zero;
                vertices[1] = p6 - p4;
                vertices[2] = p5 - p4;
                poly2.Set(vertices);
            }

            fd1.Shape = poly1;
            fd2.Shape = poly2;

            var bd1 = new BodyDef();
            var bd2 = new BodyDef();
            bd1.BodyType = BodyType.DynamicBody;
            bd2.BodyType = BodyType.DynamicBody;
            bd1.Position = _offset;
            bd2.Position = p4 + _offset;

            bd1.AngularDamping = 10.0f;
            bd2.AngularDamping = 10.0f;

            var body1 = World.CreateBody(bd1);
            var body2 = World.CreateBody(bd2);

            body1.CreateFixture(fd1);
            body2.CreateFixture(fd2);

            var djd = new DistanceJointDef();

            // Using a soft distance constraint can reduce some jitter.
            // It also makes the structure seem a bit more fluid by
            // acting like a suspension system.
            djd.DampingRatio = 0.5f;
            djd.FrequencyHz = 10.0f;

            djd.Initialize(body1, body2, p2 + _offset, p5 + _offset);
            World.CreateJoint(djd);

            djd.Initialize(body1, body2, p3 + _offset, p4 + _offset);
            World.CreateJoint(djd);

            djd.Initialize(body1, _wheel, p3 + _offset, wheelAnchor + _offset);
            World.CreateJoint(djd);

            djd.Initialize(body2, _wheel, p6 + _offset, wheelAnchor + _offset);
            World.CreateJoint(djd);

            var rjd = new RevoluteJointDef();

            rjd.Initialize(body2, _chassis, p4 + _offset);
            World.CreateJoint(rjd);
        }

        /// <inheritdoc />
        /// <inheritdoc />
        public override void OnKeyDown(KeyboardKeyEventArgs key)
        {
            if (key.Key == Key.A)
            {
                _motorJoint.SetMotorSpeed(-_motorSpeed);
            }

            if (key.Key == Key.S)
            {
                _motorJoint.SetMotorSpeed(0.0f);
            }

            if (key.Key == Key.D)
            {
                _motorJoint.SetMotorSpeed(_motorSpeed);
            }

            if (key.Key == Key.M)
            {
                _motorJoint.EnableMotor(!_motorJoint.IsMotorEnabled());
            }
        }

        protected override void OnRender()
        {
            DrawString("Keys: left = a, brake = s, right = d, toggle motor = m");
        }
    }
}