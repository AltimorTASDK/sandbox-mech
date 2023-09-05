using Sandbox;
using System;
using System.Collections.Generic;

namespace MyGame;

public partial class PawnController : EntityComponent<Pawn>
{
	public const float StepSize = 24f;
	public const float GroundAngle = 89f;
	public const float Gravity = 800f;
	public const float FrictionMult = 4f;

	public const float MaxEnergy = 30f;
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

	HashSet<string> ControllerEvents = new(StringComparer.OrdinalIgnoreCase);

	bool Grounded => Entity.GroundEntity.IsValid();

	bool IsJetting => !JetDepleted && Input.Down("jump");

        [Net, Predicted]
	public float Energy { get; protected set; } = MaxEnergy;

        [Net, Predicted]
	bool JetDepleted { get; set; }

        [Predicted]
	float TimeSinceLastJet { get; set; }

	TraceResult GroundTrace;


	public void Simulate(IClient cl)
	{
		ControllerEvents.Clear();

		var movement = Entity.InputDirection.Normal;
		var angles = Entity.ViewAngles.WithPitch(0);
		var moveVectorNormalized = Rotation.From(angles) * movement;
		var moveVector = moveVectorNormalized * 400f;
		var wasGrounded = Grounded;
		Entity.GroundEntity = CheckForGround();

		if (Grounded && !wasGrounded)
			AddEvent("grounded");

		TimeSinceLastJet += Time.Delta;

		if (IsJetting)
		{
			Entity.Velocity = DoJet(Entity.Velocity, moveVectorNormalized);
			TimeSinceLastJet = 0f;
		}

		RechargeEnergy();
		FinalizeEnergy();

		Entity.GroundEntity = CheckForGround();
		Entity.Velocity += Vector3.Down * Gravity * Time.Delta;

		if (Grounded)
		{
			Entity.Velocity -= GroundTrace.Normal * Entity.Velocity.Dot(GroundTrace.Normal);
			Entity.Velocity = ApplyFriction(Entity.Velocity, FrictionMult, Time.Delta);
			Entity.Velocity = Accelerate(Entity.Velocity, moveVector.Normal, moveVector.Length, 400f, 5f);
		}
		else
		{
			Entity.Velocity = Accelerate(Entity.Velocity, moveVector.Normal, moveVector.Length, 400f, .5f);
		}

		var mh = new MoveHelper(Entity.Position, Entity.Velocity);
		mh.MaxStandableAngle = GroundAngle;
		mh.Trace = mh.Trace.Size(Entity.Hull).Ignore(Entity);

		if (mh.TryMoveWithStep(Time.Delta, StepSize) > 0)
		{
			if (Grounded)
				mh.Position = StayOnGround(mh.Position);

			Entity.Position = mh.Position;
			Entity.Velocity = mh.Velocity;
		}
	}

	void RechargeEnergy()
	{
		if (TimeSinceLastJet < JetEnergyChargeDelay)
			return;

		Energy += JetEnergyCharge * Time.Delta;

		if (Energy >= JetEnergyCutoff)
			JetDepleted = false;
	}

	void FinalizeEnergy()
	{
		if (Energy <= 0f)
		{
			JetDepleted = true;
			Energy = 0f;
		}

		Energy = MathF.Min(Energy, MaxEnergy);
	}

	Vector3 DoJet(Vector3 input, Vector3 moveVector)
	{
		Energy -= JetEnergyDrain * Time.Delta;

		float sideFraction;
		var forwardSpeed = Vector3.Dot(input, moveVector.Normal);

		if (forwardSpeed > JetMaxForwardSpeed)
			sideFraction = 0f;
		else if (forwardSpeed < 0f)
			sideFraction = JetMaxSideFraction;
		else
			sideFraction = MathF.Min(1 - (forwardSpeed / JetMaxForwardSpeed), JetMaxSideFraction);

		sideFraction = MathF.Min(sideFraction, moveVector.Length);

		var upFraction = MathF.Sqrt(1f - sideFraction * sideFraction);
		var jetDirection = moveVector.Normal * sideFraction + Vector3.Up * upFraction;
		return input + jetDirection * JetAcceleration * Time.Delta;
	}

	Entity CheckForGround()
	{
		GroundTrace = Entity.TraceBBox(Entity.Position, Entity.Position + Vector3.Down, 2f);

		if (!GroundTrace.Hit)
			return null;

		if (GroundTrace.Normal.Angle(Vector3.Up) > GroundAngle)
			return null;

		var GroundDot = Vector3.Dot(Entity.Velocity, GroundTrace.Normal);

		if (GroundDot > MaxGroundVelocityDot)
			return null;

		if (IsJetting && GroundDot >= 0)
			return null;

		return GroundTrace.Entity;
	}

	Vector3 ApplyFriction(Vector3 input, float frictionAmount, float deltaTime)
	{
		float StopSpeed = 100.0f;

		var speed = input.Length;
		if (speed < 0.1f) return input;

		// Bleed off some speed, but if we have less than the bleed
		// threshold, bleed the threshold amount.
		float control = (speed < StopSpeed) ? StopSpeed : speed;

		// Add the amount to the drop amount.
		var drop = control * deltaTime * frictionAmount;

		if (Grounded)
			drop *= GroundTrace.Normal.z;

		// scale the velocity
		float newspeed = speed - drop;
		if (newspeed < 0) newspeed = 0;
		if (newspeed == speed) return input;

		newspeed /= speed;
		input *= newspeed;

		return input;
	}

	Vector3 Accelerate(Vector3 input, Vector3 wishdir, float wishspeed, float speedLimit, float acceleration)
	{
		if (speedLimit > 0 && wishspeed > speedLimit)
			wishspeed = speedLimit;

		var currentspeed = input.Dot(wishdir);
		var addspeed = wishspeed - currentspeed;

		if (addspeed <= 0)
			return input;

		var accelspeed = acceleration * Time.Delta * wishspeed;

		if (Grounded)
			accelspeed *= GroundTrace.Normal.z;

		if (accelspeed > addspeed)
			accelspeed = addspeed;

		input += wishdir * accelspeed;

		return input;
	}

	Vector3 StayOnGround(Vector3 position)
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

		return trace.EndPosition;
	}

	public bool HasEvent(string eventName)
	{
		return ControllerEvents.Contains(eventName);
	}

	void AddEvent(string eventName)
	{
		if (HasEvent(eventName))
			return;

		ControllerEvents.Add(eventName);
	}
}
