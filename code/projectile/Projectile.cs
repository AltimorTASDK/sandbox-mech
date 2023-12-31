﻿using Sandbox;
using System;
using System.Linq;

namespace Conna.Projectiles;

public partial class Projectile : ModelEntity
{
    public static T Create<T>(string dataPath) where T : Projectile, new()
    {
        var data = ResourceLibrary.GetAll<ProjectileData>()
            .FirstOrDefault(d => d.ResourcePath.ToLower() == dataPath.ToLower());

        if (data == null)
            throw new Exception($"Unable to find Projectile Data by path {dataPath}");

        return new T { Data = data };
    }

    [Net, Predicted] public ProjectileData Data { get; set; }

    public Action<Projectile, TraceResult> Callback { get; private set; }
    public ProjectileSimulator Simulator { get; set; }
    public string Attachment { get; set; } = null;
    public Entity Attacker { get; set; } = null;
    public Entity IgnoreEntity { get; set; }
    public Vector3 StartPosition { get; private set; }
    public bool Debug { get; set; } = false;

    protected float GravityModifier { get; set; }
    protected RealTimeUntil DestroyTime { get; set; }
    protected ModelEntity ModelEntity { get; set; }
    protected Vector3 InitialVelocity { get; set; }
    protected Sound LaunchSound { get; set; }
    protected Particles Follower { get; set; }
    protected Particles Trail { get; set; }
    protected float LifeTime { get; set; }
    protected float Gravity { get; set; }

    public void Initialize(Vector3 start, Vector3 velocity, Action<Projectile, TraceResult> callback = null)
    {
        ModelEntity = new ModelEntity(Data.ModelName, this);
        LifeTime = Data.LifeTime.GetValue();
        Gravity = Data.Gravity.GetValue();

        if (LifeTime > 0f)
            DestroyTime = LifeTime;

        InitialVelocity = velocity;
        StartPosition = start;
        EnableDrawing = false;
        Velocity = velocity;
        Callback = callback;
        Position = start;

        if (IsClientOnly)
        {
            using (Prediction.Off())
            {
                CreateEffects();
            }
        }

        if (Simulator.IsValid())
        {
            Simulator.Add(this);
            Owner = Simulator.Owner;

            if (Game.IsServer)
            {
                using (LagCompensation())
                {
                    // Work out the number of ticks for this client's latency that it took for us to receive this input.
                    var tickDifference = ((float)(Owner.Client.Ping / 2000f) / Time.Delta).CeilToInt();

                    // Advance the simulation by that number of ticks.
                    for (var i = 0; i < tickDifference; i++)
                    {
                        if (IsValid)
                            Simulate();
                    }
                }
            }
        }
    }

    public override void Spawn()
    {
        Predictable = true;

        base.Spawn();
    }

    public virtual void CreateEffects()
    {
        if (!string.IsNullOrEmpty(Data.TrailEffect))
        {
            Trail = Particles.Create(Data.TrailEffect, this);

            if (Trail != null)
            {
                if (!string.IsNullOrEmpty(Attachment))
                    Trail.SetEntityAttachment(0, this, Attachment);
                else
                    Trail.SetEntity(0, this);
            }
        }

        if (!string.IsNullOrEmpty(Data.FollowEffect))
            Follower = Particles.Create(Data.FollowEffect, this);

        if (!string.IsNullOrEmpty(Data.LaunchSound))
            LaunchSound = PlaySound(Data.LaunchSound);
    }

    public virtual void Simulate()
    {
        if (Data.FaceDirection)
            Rotation = Rotation.LookAt(Velocity.Normal);

        if (Debug)
            DebugOverlay.Sphere(Position, Data.Radius, Game.IsClient ? Color.Blue : Color.Red);

        Velocity += Vector3.Down * Gravity * Time.Delta;
        var newPosition = Position + Velocity * Time.Delta;

        var trace = Trace.Ray(Position, newPosition)
            .UseHitboxes()
            .WithAnyTags("player", "npc")
            .Size(Data.Radius)
            .Ignore(this)
            .Ignore(IgnoreEntity)
            .Run();

        if (!trace.Hit)
        {
            trace = Trace.Ray(Position, newPosition)
                .UseHitboxes()
                .WithAnyTags("solid")
                .Ignore(this)
                .Ignore(IgnoreEntity)
                .Run();
        }

        Position = trace.EndPosition;

        if (LifeTime > 0f && DestroyTime)
        {
            if (Data.ExplodeOnDestroy)
            {
                PlayHitEffects(Vector3.Zero);
                Callback?.Invoke(this, trace);
            }

            Delete();

            return;
        }

        if (trace.Hit)
        {
            PlayHitEffects(trace.Normal);
            CreateDecal(trace);
            Callback?.Invoke(this, trace);
            Delete();
        }
    }

    public bool IsServerSideCopy()
    {
        return !IsClientOnly && Owner.IsValid() && Owner.IsLocalPawn;

    }

    [ClientRpc]
    protected virtual void PlayHitEffects(Vector3 normal)
    {
        if (IsServerSideCopy())
        {
            // We don't want to play hit effects if we're the server-side copy.
            return;
        }

        if (!string.IsNullOrEmpty(Data.ExplosionEffect))
        {
            var explosion = Particles.Create(Data.ExplosionEffect);

            if (explosion != null)
            {
                explosion.SetPosition(0, Position);
                explosion.SetPosition(1, normal);
                explosion.SetForward(2, normal);
                explosion.SetPosition(3, StartPosition);
                explosion.SetPosition(4, Position + normal);
            }
        }

        if (!string.IsNullOrEmpty(Data.HitSound))
            Sound.FromWorld(Data.HitSound, Position);
    }

    protected virtual void CreateDecal(TraceResult trace)
    {
        if (Data.HitDecal != null)
            Decal.Place(Data.HitDecal, trace);
    }

    [GameEvent.PreRender]
    protected virtual void PreRender()
    {
        Trail?.SetPosition(1, Velocity);
        Trail?.SetForward(2, Velocity);
        Trail?.SetPosition(3, StartPosition);
        Trail?.SetPosition(4, Position + Velocity.Normal);
    }

    [GameEvent.Tick.Server]
    protected virtual void ServerTick()
    {
        if (!Simulator.IsValid())
            Simulate();
    }

    protected override void OnDestroy()
    {
        Simulator?.Remove(this);
        RemoveEffects();
        base.OnDestroy();
    }

    private void RemoveEffects()
    {
        ModelEntity?.Delete();
        LaunchSound.Stop();
        Follower?.Destroy();
        Trail?.Destroy(true);
    }
}
