using System.Numerics;
using Box2DSharp.Collision.Collider;

namespace Box2DSharp.Dynamics
{
    public interface IQueryCallback
    {
        bool QueryCallback(Fixture fixture);
    }

    public interface IRayCastCallback
    {
        /// Callback for ray casts.
        /// See b2World::RayCast
        /// Called for each fixture found in the query. You control how the ray cast
        /// proceeds by returning a F:
        /// return -1: ignore this fixture and continue
        /// return 0: terminate the ray cast
        /// return fraction: clip the ray to this point
        /// return 1: don't clip the ray and continue
        /// @param fixture the fixture hit by the ray
        /// @param point the point of initial intersection
        /// @param normal the normal vector at the point of intersection
        /// @return -1 to filter, 0 to terminate, fraction to clip the ray for
        /// closest hit, 1 to continue
        F RayCastCallback(Fixture fixture, in V2 point, in V2 normal, F fraction);
    }
}

namespace Box2DSharp.Dynamics.Internal
{
    public interface ITreeQueryCallback
    {
        bool QueryCallback(int proxyId);
    }

    public interface ITreeRayCastCallback
    {
        F RayCastCallback(in RayCastInput input, int proxyId);
    }

    public interface IAddPairCallback
    {
        void AddPairCallback(object proxyUserDataA, object proxyUserDataB);
    }
}