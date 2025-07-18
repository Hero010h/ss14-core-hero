using Robust.Shared.GameStates;

namespace Content.Shared._White.CharacterExamine;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class DetailExaminableComponent : Component
{
    [DataField("content", required: true)]
    [ViewVariables(VVAccess.ReadWrite)]
    [AutoNetworkedField]
    public string Content = "";
}
