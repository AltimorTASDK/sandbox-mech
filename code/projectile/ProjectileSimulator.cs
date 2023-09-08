using Sandbox;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Conna.Projectiles;

public partial class ProjectileSimulator : IValid
{
	public HashSet<Projectile> Projectiles { get; private set; }
	public Entity Owner { get; private set; }
	public bool IsValid => Owner.IsValid();

	public ProjectileSimulator(Entity owner)
	{
		Projectiles = new();
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
