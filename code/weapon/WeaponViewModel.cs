using Sandbox;

namespace MyGame;

public partial class WeaponViewModel : BaseViewModel
{
    protected Weapon Weapon { get; init; }

    public WeaponViewModel(Weapon weapon)
    {
        Weapon = weapon;
    }

    public override void PlaceViewmodel()
    {
        if (Game.IsRunningInVR)
            return;

        Position = Camera.Position;
        //Rotation = Rotation.LookAt(Camera.Rotation.Forward.WithZ(0f));
        Rotation = Camera.Rotation;
    }
}
