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

    private float TryMove(MoveState state, float deltaTime, bool canStep = true, int maxIterations = 8)
    {
        Vector3? lastNormal = null;

        for (var iterations = 0; iterations < maxIterations; iterations++)
        {
            if (state.Velocity.IsNearZeroLength || state.FractionRemaining <= 1e-4f)
                break;

            var moveDelta = state.Velocity * deltaTime * state.FractionRemaining;
            var trace = TraceMove(state, moveDelta);

            state.FractionRemaining *= 1f - trace.Fraction;

            // There's a bug with sweeping where sometimes the end position is starting in solid, so we get stuck.
            // Push back by a small margin so this should never happen.
            if (trace.Hit)
                state.Position += trace.Normal * .03125f;
            else
                break;

            if (canStep)
            {
                var wallNormal = GetClippingNormal(trace, biasToWall: true);

                if (wallNormal.Angle(Vector3.Up) > GroundAngle)
                {
                    // Stepping consumes a trace
                    iterations++;

                    if (iterations >= maxIterations)
                        break;

                    if (TryStep(state, wallNormal, deltaTime))
                    {
                        // Another 3 traces on success
                        iterations += 3;
                        continue;
                    }
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

    private bool TryStep(MoveState outState, Vector3 wallNormal, float deltaTime)
    {
        // Test for flat ground with a downward bbox trace slightly nudged into the wall
        var bboxTraceStart = outState.Position - wallNormal;
        var bboxTrace = Entity.TraceBBox(bboxTraceStart, bboxTraceStart + Vector3.Down * 2f, StepSize);

        if (!bboxTrace.Hit || bboxTrace.StartedSolid || bboxTrace.Normal.Angle(Vector3.Up) > StepGroundAngle)
            return false;

        var state = new MoveState(outState);
        var bboxStepSize = (bboxTrace.EndPosition - bboxTraceStart).z;

        // Use flat trace to check stairs movement
        state.Velocity = state.Velocity.WithZ(0f);

        //  Up, down, and over
        TraceMove(state, Vector3.Up * bboxStepSize);
        TryMove(state, deltaTime, canStep: false, maxIterations: 1);
        var downTrace = TraceMove(state, Vector3.Down * bboxStepSize);

        if (!downTrace.Hit)
            return false;

        //Log.Info($"fraccy {stepDownTrace.Fraction} guyname {stepDownTrace.Entity.Name} normie {stepDownTrace.Normal} end possy {stepDownTrace.EndPosition}");

        state.Velocity = outState.Velocity;
        outState.CopyFrom(state);

        return true;
    }

	private Vector3 GetClippingNormal(TraceResult trace, bool biasToWall = false)
    {
        if (trace.Normal.Angle(Vector3.Up) > GroundAngle)
            return trace.Normal;

        var radius = Entity.Radius;
        var height = Entity.Height;

        var normalRight = trace.Normal.Cross(Vector3.Up);
        var normalUp = normalRight.Cross(trace.Normal);

        // Find the center of the end of the capsule that collided
        var start = trace.EndPosition + Vector3.Up * (trace.Normal.z >= 0f ? radius : height + radius);

        // Trace outward past radius with a slight centerward bias to break ties on corner collisions
        // Flip to an outward bias if biasToWall is true
        var bias = (trace.Normal.z < 0) != biasToWall ? -1f : 1f;
        var end = start - trace.Normal * (radius + 64f) + normalUp * bias * 2f;
        var ray = Entity.TraceRay(start, end);

        if (!ray.Hit || ray.Entity != trace.Entity)
            return trace.Normal;

        // Retain curved X/Y normals, but don't lose traction on steps
        var xyLength = MathF.Sqrt(1f - ray.Normal.z.Squared());
        var xyNormal = trace.Normal.Normal2D();
        return (xyNormal * xyLength + Vector3.Up * ray.Normal.z).Normal;
    }
}