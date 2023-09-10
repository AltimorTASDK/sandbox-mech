using Sandbox;
using System;
using System.Collections.Generic;

namespace MyGame;

public partial class PawnController : EntityComponent<Pawn>
{
    private class MoveState
    {
        public Vector3 Position;
        public Vector3 Velocity;
        public float FractionRemaining = 1f;

        public MoveState(Vector3 position, Vector3 velocity)
        {
            Position = position;
            Velocity = velocity;
        }

        public MoveState(MoveState other) => CopyFrom(other);

        public void CopyFrom(MoveState other)
        {
            Position = other.Position;
            Velocity = other.Velocity;
            FractionRemaining = other.FractionRemaining;
        }
    };

    private static Vector3 ClipVelocity(Vector3 velocity, Vector3 normal)
    {
        // Overclip to 1 u/s away from the surface like Source
        return velocity - normal * (velocity.Dot(normal) + 1f);
    }

    private TraceResult TraceFromTo(Vector3 start, Vector3 end)
    {
        return Entity.CapsuleTrace.FromTo(start, end).Run();
    }

    private TraceResult TraceMove(MoveState state, Vector3 delta)
    {
        var trace = TraceFromTo(state.Position, state.Position + delta);
        state.Position = trace.EndPosition;
        return trace;
    }

	protected Vector3 StayOnGround(Vector3 position)
	{
		var start = position + Vector3.Up * 2;
		var end = position + Vector3.Down * StepSize;

		// See how far up we can go without getting stuck
		var trace = Entity.TraceCapsule(position, start);
		start = trace.EndPosition;

		// Now trace down from a known safe position
		trace = Entity.TraceCapsule(start, end);

        if (!trace.Hit || trace.StartedSolid || !IsValidGroundNormal(trace.Normal))
            return position;

		return trace.EndPosition;
	}

    private void Move(float deltaTime)
    {
        var state = new MoveState(Entity.Position, Entity.Velocity);

        if (TryMove(state, deltaTime) > 0f)
        {
            Entity.Position = Grounded ? StayOnGround(state.Position) : state.Position;
            Entity.Velocity = state.Velocity;
        }
    }

    private float TryMove(MoveState state, float deltaTime, bool canStep = true)
    {
        Vector3? lastNormal = null;

        for (var iterations = 0; iterations < 8; iterations++)
        {
            if (state.Velocity.IsNearZeroLength || state.FractionRemaining <= 1e-4f)
                break;

            var moveDelta = state.Velocity * deltaTime * state.FractionRemaining;
            var trace = TraceFromTo(state.Position, state.Position + moveDelta);

            state.FractionRemaining *= 1f - trace.Fraction;

            if (trace.Hit)
            {
                // There's a bug with sweeping where sometimes the end position is starting in solid, so we get stuck.
                // Push back by a small margin so this should never happen.
                state.Position = trace.EndPosition + trace.Normal * 0.03125f;
            }
            else
            {
                state.Position = trace.EndPosition;
                break;
            }

            if (canStep)
            {
                var wallNormal = GetClippingNormal(trace, biasToWall: true);
                Log.Info($"wallNormal {wallNormal}");

                if (wallNormal.Angle(Vector3.Up) > GroundAngle)
                {
                    if (TryStep(state, deltaTime))
                        continue;
                }
            }

            if (lastNormal == null)
            {
                state.Velocity = ClipVelocity(state.Velocity, trace.Normal);
            }
            else
            {
                // Don't get pushed back into the previous surface
                var direction = lastNormal.Value.Cross(trace.Normal);
                state.Velocity = direction.Normal * direction.Dot(state.Velocity);
            }

            lastNormal = trace.Normal;
        }

        return 1f - state.FractionRemaining;
    }

    private bool TryStep(MoveState outState, float deltaTime)
    {
        var state = new MoveState(outState);

        // Go up, over, and back down
        TraceMove(state, Vector3.Up * StepSize);
        TryMove(state, deltaTime, canStep: false);
        var downTrace = TraceMove(state, Vector3.Down * StepSize);
        var surfaceNormal = GetClippingNormal(downTrace);
        Log.Info($"surface normal {surfaceNormal}");

        if (!downTrace.Hit || surfaceNormal.Angle(Vector3.Up) > StepGroundAngle)
            return false;

        Log.Info($"steppin. delta {state.Position - outState.Position}");

        outState.CopyFrom(state);
        return true;
    }

	private Vector3 GetClippingNormal(TraceResult trace, bool biasToWall = false)
    {
        if (trace.Normal.Angle(Vector3.Up) > GroundAngle)
            return trace.Normal;

        var radius = Entity.Radius;
        var height = Entity.Height;

        // Find the center of the end of the capsule that collided
        var start = trace.EndPosition + Vector3.Up * (trace.Normal.z >= 0f ? radius : height + radius);

        // Trace outward past radius with a slight centerward bias to break ties on corner collisions
        // Flip to an outward bias if biasToWall is true
        var bias = (trace.Normal.z < 0) != biasToWall ? -.01f : .01f;
        var end = start - trace.Normal * (radius + 2f) + Vector3.Up * bias;

        var ray = Entity.TraceRay(start, end);

        if (!ray.Hit)
            return Vector3.Zero;

        // Retain curved X/Y normals, but don't lose traction on steps
        var xyLength = MathF.Sqrt(1f - ray.Normal.z.Squared());
        var xyNormal = trace.Normal.WithZ(0f).Normal;
        return (xyNormal * xyLength + Vector3.Up * ray.Normal.z).Normal;
    }
}