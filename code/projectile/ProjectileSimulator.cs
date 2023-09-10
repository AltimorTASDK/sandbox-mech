using Sandbox;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Conna.Projectiles;

public partial class ProjectileSimulator : IValid
{
	public Entity Owner { get; private set; }
	public bool IsValid => Owner.IsValid();
	private readonly HashSet<Projectile> Projectiles = new();

	public ProjectileSimulator(Entity owner)
	{
		Owner = owner;
	}

	public void Add(Projectile projectile)
	{
		Projectiles.Add(projectile);
	}

	public void Remove(Projectile projectile)
	{
		Projectiles.Remove(projectile);
	}

	public void Clear()
	{
		foreach (var projectile in Projectiles)
		{
			projectile.Delete();
		}

		Projectiles.Clear();
	}

	public void Simulate()
	{
		Projectiles.RemoveWhere(projectile => !projectile.IsValid());

		foreach (var projectile in Projectiles)
		{
			if (Prediction.FirstTime)
				projectile.Simulate();
		}
	}
}

public static class ProjectileSimulatorExtensions
{
	public static bool IsValid([NotNullWhen(true)] this ProjectileSimulator? simulator)
	{
		return simulator?.Owner.IsValid() ?? false;
	}
}
