namespace Content.Shared._White.Cult.Components;

[RegisterComponent]
public sealed partial class CultItemComponent : Component
{
    [DataField]
    public bool CanPickUp = true;
}
