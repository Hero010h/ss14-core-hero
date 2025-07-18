using Robust.Shared.GameStates;

namespace Content.Shared._White.Economy;

[RegisterComponent, NetworkedComponent]
public sealed partial class BankCardComponent : Component
{
    [DataField]
    public int? AccountId;

    [DataField]
    public int StartingBalance;

    [DataField]
    public bool CommandBudgetCard;
}
