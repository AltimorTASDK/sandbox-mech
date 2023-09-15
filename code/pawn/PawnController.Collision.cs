using Sandbox;
using System;

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
        return velocity - normal * velocity.Dot(normal);
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

        if (!trace.Hit || trace.StartedSolid || !IsValidGroundNormal(GetClippingNormal(trace)))
            return position;

        if (!IsValidGroundNormal(trace.Normal.Normal2D(), checkAngle: false))
        {
            var checkStart = trace.EndPosition + trace.Normal.Normal2D() * Entity.Radius;
            var checkEnd = checkStart + Vector3.Down * StepSize;

            if (!Entity.TraceCapsule(checkStart, checkEnd).Hit)
                return position;
        }

        return trace.EndPosition;
    }

    private void Move(float deltaTime)
    {
        var state = new MoveState(Entity.Position, Entity.Velocity);

        if (TryMove(state, deltaTime) > 0f)
        {
            Entity.Velocity = state.Velocity;
            Entity.Position = Grounded ? StayOnGround(state.Position) : state.Position;
        }
    }

    private float TryMove(MoveState state, float deltaTime, bool canStep = true, int maxIterations = 8)
    {
        Vector3? lastNormal = null;

        for (var iterations = 0; iterations < maxIterations; iterations++)
        {
            var moveDelta = state.Velocity * deltaTime * state.FractionRemaining;

            if (moveDelta.IsNearZeroLength)
            {
                state.FractionRemaining = 0f;
                break;
            }

            var trace = TraceMove(state, moveDelta);

            if (!trace.Hit)
            {
                state.FractionRemaining = 0f;
                break;
            }

            state.FractionRemaining *= 1f - trace.Fraction;

            if (canStep && moveDelta.Length2DSquared() > 0f)
            {
                var wallNormal = GetClippingNormal(trace, biasToWall: true);

                if (wallNormal.Angle(Vector3.Up) > StepGroundAngle)
                {
                    if (TryStep(state, wallNormal, deltaTime))
                    {
                        continue;
                    }
                }
            }

            if (lastNormal == null || lastNormal.Value.Dot(trace.Normal) > 0f)
            {
                state.Velocity = ClipVelocity(state.Velocity, GetClippingNormal(trace));

                // Clip against capsule normal if still opposed
                if (state.Velocity.Dot(trace.Normal) < 0f)
                    state.Velocity = ClipVelocity(state.Velocity, trace.Normal);
            }
            else
            {
                // Don't get pushed back into the previous surface
                var direction = lastNormal.Value.Cross(trace.Normal).Normal;
                state.Velocity = direction * direction.Dot(state.Velocity);
            }

            lastNormal = trace.Normal;
        }

        return 1f - state.FractionRemaining;
    }

    private bool TryStep(MoveState outState, Vector3 wallNormal, float deltaTime)
    {
        // Test for flat ground with a downward bbox trace slightly nudged into the wall
        var bboxTraceStart = outState.Position - wallNormal.Normal2D();
        var bboxTrace = Entity.TraceBBox(bboxTraceStart, bboxTraceStart + Vector3.Down * 2f, StepSize);

        if (!bboxTrace.Hit || bboxTrace.StartedSolid || bboxTrace.Normal.Angle(Vector3.Up) > StepGroundAngle)
            return false;

        var state = new MoveState(outState);
        var bboxStepSize = bboxTrace.EndPosition.z - bboxTraceStart.z + 2f;

        // Use flat trace to check stairs movement
        state.Velocity.z = 0f;

        //  Up, down, and over
        TraceMove(state, Vector3.Up * bboxStepSize);
        TryMove(state, deltaTime, canStep: false, maxIterations: 1);
        var downTrace = TraceMove(state, Vector3.Down * (bboxStepSize + 2f));

        if (!downTrace.Hit || downTrace.StartedSolid)
            return false;

        state.Velocity = outState.Velocity;
        outState.CopyFrom(state);

        return true;
    }

    private Vector3 GetClippingNormal(TraceResult trace, bool biasToWall = false)
    {
        const float TraceDepth = 64f;
        const float TraceBias = 2f;

        if (trace.Normal.Angle(Vector3.Up) > GroundAngle)
            return trace.Normal;

        // We need some X/Y direction
        if (trace.Normal.Length2D() < TraceBias / TraceDepth)
            return trace.Normal;

        var radius = Entity.Radius;
        var height = Entity.Height;

        var normalRight = trace.Normal.Cross(Vector3.Up).Normal;
        var normalUp = normalRight.Cross(trace.Normal).Normal;

        // Find the center of the end of the capsule that collided
        var start = trace.EndPosition + Vector3.Up * (trace.Normal.z >= 0f ? radius : height + radius);

        // Trace outward past radius with a slight centerward bias to break ties on corner collisions
        // Flip to an outward bias if biasToWall is true
        var bias = (trace.Normal.z < 0) != biasToWall ? -1f : 1f;
        var delta = -trace.Normal * (radius + TraceDepth) + normalUp * bias * TraceBias;

        // We won't get a valid result if the bias inverted the yaw
        if (delta.Dot(trace.Normal.Normal2D()) > 0f)
            return trace.Normal;

        var ray = Entity.TraceRay(start, start + delta);

        if (!biasToWall && trace.Normal.Angle(Vector3.Up) > GroundAngle)
            return trace.Normal;

        if (!ray.Hit || ray.Entity != trace.Entity)
            return trace.Normal;

        // Retain curved X/Y normals, but don't lose traction on steps
        var xyLength = MathF.Sqrt(1f - ray.Normal.z.Min(1f).Squared());
        var xyNormal = trace.Normal.Normal2D();
        return (xyNormal * xyLength + Vector3.Up * ray.Normal.z).Normal;
    }
}