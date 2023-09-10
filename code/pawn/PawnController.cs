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
    /// The maximum dot product between the velocity and ground normal before the ground is no longer valid.
    /// </summary>
    public const float MaxGroundVelocityDot = 100f;

    public float MaxEnergy => 30f;

    [Net, Predicted]
    public float Energy { get; protected set; }

    public bool Grounded => Entity.GroundEntity.IsValid();

    public bool IsJetting => !JetDepleted && Input.Down("jump");

    public Vector3 GroundNormal => Grounded ? GroundTrace.Normal : Vector3.Zero;

    public Vector3 ClippingNormal { get; private set; }

    [Net, Predicted]
    protected bool JetDepleted { get; set; }

    [Predicted]
    protected float TimeSinceLastJet { get; set; }

    private Vector3 CurrentAcceleration;

    private TraceResult GroundTrace;

    private HashSet<string> ControllerEvents = new(StringComparer.OrdinalIgnoreCase);

    public PawnController()
    {
        Energy = MaxEnergy;
    }

    public void Simulate(IClient cl)
    {
        ControllerEvents.Clear();
        CurrentAcceleration = Vector3.Zero;
        TimeSinceLastJet += Time.Delta;

        var movement = Entity.InputDirection.Normal;
        var angles = Entity.ViewAngles.WithPitch(0);
        var moveVector = Rotation.From(angles) * movement;
        var wasGrounded = Grounded;

        UpdateGroundEntity();

        if (Grounded && !wasGrounded)
            AddEvent("grounded");

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
            var projectedMoveVector = moveVector.ProjectZ(GroundNormal);

            Entity.Velocity -= Entity.Velocity.ProjectOnNormal(GroundNormal);
            ApplyFriction(adjustedFriction);
            Accelerate(projectedMoveVector, MaxSpeed, adjustedAcceleration);
        }
        else
        {
            Accelerate(moveVector, MaxSpeed, AirAcceleration);
        }

        Move(Time.Delta);
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
        return (CurrentAcceleration.Dot(ClippingNormal) / -Gravity).Max(0f);
    }

    protected Vector3 GetProjectedMoveVector(Vector3 moveVector)
    {
        var projectedMoveVector = moveVector.ProjectZ(GroundNormal);
    }

    /// <returns>Fraction of requested energy that was successfully consumed</returns>
    protected float DrainEnergy(float amount)
    {
        if (amount >= Energy)
        {
            var fraction = Energy / amount;
            Energy = 0f;
            JetDepleted = true;
            return fraction;
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
        var forwardSpeed = Entity.Velocity.Dot(moveVector.Normal);
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
        // Scale friction with speed within range
        var speed = Entity.Velocity.Length;
        var frictionDelta = speed.Clamp(StopSpeed, SlipSpeed) * friction * Time.Delta;

        if (frictionDelta < speed)
            Entity.Velocity *= (speed - frictionDelta) / speed;
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

    protected void UpdateGroundEntity()
    {
        GroundTrace = Entity.TraceCapsule(Entity.Position, Entity.Position + Vector3.Down, 2f);

        if (!GroundTrace.Hit || !IsValidGroundNormal(GroundTrace.Normal))
            return;

        ClippingNormal = GetClippingNormal(GroundTrace);
        Entity.GroundEntity = GroundTrace.Entity;
    }

    /// <summary>
    /// Check to leave the ground after a velocity update using the cached trace
    /// </summary>
    protected void CheckToLeaveGround()
    {
        if (Grounded && !IsValidGroundNormal(GroundNormal))
            Entity.GroundEntity = null;
    }

    protected bool IsValidGroundNormal(Vector3 normal)
    {
        return GroundTrace.Normal.Angle(Vector3.Up) <= GroundAngle &&
            Entity.Velocity.Dot(normal) < (IsJetting ? 0 : MaxGroundVelocityDot);
    }

    public bool HasEvent(string eventName) => ControllerEvents.Contains(eventName);

    protected void AddEvent(string eventName)
    {
        if (!HasEvent(eventName))
            ControllerEvents.Add(eventName);
    }
}
