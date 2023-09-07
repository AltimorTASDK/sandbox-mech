using Sandbox;
using Conna.Projectiles;

namespace MyGame;

public partial class Pistol : Weapon
{
	public override string ModelPath => "weapons/rust_pistol/rust_pistol.vmdl";
	public override string ViewModelPath => "weapons/rust_pistol/v_rust_pistol.vmdl";
	public override float PrimaryRate => 9.5f;

	[ClientRpc]
	protected virtual void ShootEffects()
	{
		Game.AssertClient();

		Particles.Create("particles/pistol_muzzleflash.vpcf", EffectEntity, "muzzle");

		Pawn.SetAnimParameter("b_attack", true);
		ViewModelEntity?.SetAnimParameter("fire", true);
	}

	public override void PrimaryAttack()
	{
		ShootEffects();
		Pawn.PlaySound("rust_pistol.shoot");

		var projectile = Projectile.Create<Projectile>("data/rifle/bullet.proj");
		projectile.IgnoreEntity = this;
		projectile.Simulator = Pawn.Projectiles;
		projectile.Attacker = Pawn;

		var forward = Pawn.EyeRotation.Forward;
		var right = Pawn.EyeRotation.Right;
		var up = Pawn.EyeRotation.Up;

		var position = Pawn.EyePosition + right * 12f + forward * 12f + up * -3f;
		var velocity = forward * projectile.Data.Speed.GetValue();

		projectile.Initialize(position, velocity);
	}

	protected override void Animate()
	{
		Pawn.SetAnimParameter("holdtype", (int)CitizenAnimationHelper.HoldTypes.Pistol);
	}
}
