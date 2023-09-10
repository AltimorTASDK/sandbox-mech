using Sandbox;
using System;
using System.Collections.Generic;

namespace MyGame;

public partial class PawnController : EntityComponent<Pawn>
{
	public const float StepSize = 50f;
	public const float GroundAngle = 89f;
	public const float StepGroundAngle = 60f;
	public const float Gravity = 800f;
	public const float Friction = 4f;
	public const float MaxSpeed = 400f;
	public const float Acceleration = 5f;
	public const float AirAcceleration = .5f;
	public const float StopSpeed = 100f;
	public const float SlipSpeed = 500f;

	public const float JetEnergyCutoff = 5f;
	public const float JetEnergyDrain = 17f;
	public const float JetEnergyCharge = 15f;
	public const float JetEnergyChargeDelay = .3f;
	public const float JetAcceleration = 1200f;
	public const float JetMaxSideFraction = .8f;
	public const float JetMaxForwardSpeed = 900f;

	/// <summary>
	/// The maximum dot product between the velocity and ground normal for ground to be considered valid.
	/// </summary>
	public const float MaxGroundVelocityDot = 100f;

	public float MaxEnergy => 30f;

	public bool Grounded => Entity.GroundEntity.IsValid();

	public bool IsJetting => !JetDepleted && Input.Down("jump");

	[Net, Predicted]
	public float Energy { get; protected set; }

	[Net, Predicted]
	protected bool JetDepleted { get; set; }

	[Predicted]
	protected float TimeSinceLastJet { get; set; }

	protected TraceResult GroundTrace;

	protected Vector3 CurrentAcceleration;

	private HashSet<string> ControllerEvents = new(StringComparer.OrdinalIgnoreCase);

	public bool HasEvent(string eventName) => ControllerEvents.Contains(eventName);

	public PawnController()
	{
		Energy = MaxEnergy;
	}

	public void Simulate(IClient cl)
	{
		ControllerEvents.Clear();
                CurrentAcceleration = Vector3.Zero;

		var movement = Entity.InputDirection.Normal;
		var angles = Entity.ViewAngles.WithPitch(0);
		var moveVector = Rotation.From(angles) * movement;
		var wasGrounded = Grounded;
		Entity.GroundEntity = CheckForGround();

		if (Grounded && !wasGrounded)
			AddEvent("grounded");

		TimeSinceLastJet += Time.Delta;

		if (IsJetting)
			DoJet(moveVector);

		UpdateEnergy();
		AddAcceleration(Vector3.Down * Gravity);
                CheckToLeaveGround();

		if (Grounded)
		{
                        var traction = GetTraction();
                        var adjustedFriction = Friction * traction;
                        var adjustedAcceleration = AirAcceleration.LerpTo(Acceleration, traction);
                        var projectedMoveVector = moveVector.ProjectZ(GroundTrace.Normal);

			Entity.Velocity -= Entity.Velocity.ProjectOnNormal(GroundTrace.Normal);
			ApplyFriction(adjustedFriction);
			Accelerate(projectedMoveVector, MaxSpeed, adjustedAcceleration);
		}
		else
		{
			Accelerate(moveVector, MaxSpeed, AirAcceleration);
		}

		var mh = new MoveHelper(Entity.Position, Entity.Velocity);
		mh.MaxStandableAngle = StepGroundAngle;
		mh.Trace = mh.Trace.Size(Entity.Hull).Ignore(Entity);

		if (mh.TryMoveWithStep(Time.Delta, StepSize) > 0)
		{
			if (Grounded)
				mh.Position = StayOnGround(mh.Position);

			Entity.Position = mh.Position;
			Entity.Velocity = mh.Velocity;
		}
	}

        protected void AddAcceleration(Vector3 acceleration)
        {
                CurrentAcceleration += acceleration;
                Entity.Velocity += acceleration * Time.Delta;
        }

        /// <summary>
        /// Gets traction as a multiple of the normal force exerted by gravity on flat ground.
        /// Only gives a valid result when the pawn is grounded.
        /// </summary>
	protected float GetTraction()
	{
                return (CurrentAcceleration.Dot(GroundTrace.Normal) / -Gravity).Max(0f);
        }

        /// <returns>Fraction of requested energy that was successfully consumed</returns>
        protected float DrainEnergy(float amount)
        {
                if (amount >= Energy)
                {
                        Energy = 0f;
                        JetDepleted = true;
                        return Energy / amount;
                }

                Energy -= amount;
                return 1f;
        }

        protected void AddEnergy(float amount)
        {
                Energy = (Energy + amount).Min(MaxEnergy);

		if (Energy >= JetEnergyCutoff)
			JetDepleted = false;
        }

	protected void UpdateEnergy()
	{
		if (TimeSinceLastJet >= JetEnergyChargeDelay)
                        AddEnergy(JetEnergyCharge * Time.Delta);
	}

	protected void DoJet(Vector3 moveVector)
        {
                // Reduce acceleration if we don't have enough energy for a full tick of jets
                var energyFraction = DrainEnergy(JetEnergyDrain * Time.Delta);

                // Lerp horizontal thrust towards 0 as we approach JetMaxForwardSpeed
		var forwardSpeed = Vector3.Dot(Entity.Velocity, moveVector.Normal);
		var maxSideFraction = moveVector.Length.Min(JetMaxSideFraction);
                var sideFraction = (1f - (forwardSpeed / JetMaxForwardSpeed)).Clamp(0f, maxSideFraction);

                // Any remaining thrust is directed upward
		var upFraction = MathF.Sqrt(1f - sideFraction * sideFraction);
		var jetDirection = moveVector.Normal * sideFraction + Vector3.Up * upFraction;

                AddAcceleration(jetDirection * JetAcceleration * energyFraction);
                TimeSinceLastJet = 0f;
        }

	protected void ApplyFriction(float friction)
	{
		var speed = Entity.Velocity.Length;

		// Scale friction with speed within range.
		var drop = speed.Clamp(StopSpeed, SlipSpeed) * friction * Time.Delta;

		// scale the velocity
                if (drop < speed)
                        Entity.Velocity *= (speed - drop) / speed;
                else
                        Entity.Velocity = Vector3.Zero;
	}

	protected void Accelerate(Vector3 moveVector, float maxSpeed, float acceleration)
	{
		var wishspeed = moveVector.Length * maxSpeed;
		if (wishspeed <= 0f)
			return;

		var addspeed = wishspeed - Entity.Velocity.Dot(moveVector.Normal);
		if (addspeed <= 0f)
			return;

		var accelspeed = acceleration * maxSpeed * Time.Delta;
		Entity.Velocity += moveVector.Normal * accelspeed.Min(addspeed);
	}

	protected Entity CheckForGround()
	{
		GroundTrace = Entity.TraceBBox(Entity.Position, Entity.Position + Vector3.Down, 2f);

		if (!GroundTrace.Hit)
			return null;

		if (GroundTrace.Normal.Angle(Vector3.Up) > GroundAngle)
			return null;

                if (!ShouldStayOnGround(GroundTrace.Normal))
			return null;

		return GroundTrace.Entity;
	}

        /// <summary>
        /// Check to leave the ground after a velocity update using the cached trace
        /// </summary>
        protected void CheckToLeaveGround()
        {
                if (Grounded && !ShouldStayOnGround(GroundTrace.Normal))
			Entity.GroundEntity = null;
        }

        protected bool ShouldStayOnGround(Vector3 normal)
        {
		var GroundDot = Vector3.Dot(Entity.Velocity, normal);
		return GroundDot < 0 || (!IsJetting && GroundDot <= MaxGroundVelocityDot);
        }

	protected Vector3 StayOnGround(Vector3 position)
	{
		var start = position + Vector3.Up * 2;
		var end = position + Vector3.Down * StepSize;

		// See how far up we can go without getting stuck
		var trace = Entity.TraceBBox(position, start);
		start = trace.EndPosition;

		// Now trace down from a known safe position
		trace = Entity.TraceBBox(start, end);

		if (trace.Fraction <= 0) return position;
		if (trace.Fraction >= 1) return position;
		if (trace.StartedSolid) return position;
		if (Vector3.GetAngle(Vector3.Up, trace.Normal) > GroundAngle) return position;
		if (!ShouldStayOnGround(trace.Normal)) return position;

		return trace.EndPosition;
	}

	protected void AddEvent(string eventName)
	{
		if (!HasEvent(eventName))
			ControllerEvents.Add(eventName);
	}
}
