﻿using Content.Client._White.UserInterface.Radial;
using Content.Shared._White.Cult;
using Content.Shared._White.Cult.Components;
using Content.Shared._White.Cult.UI;
using Robust.Client.Player;
using Robust.Shared.Prototypes;

namespace Content.Client._White.Cult.UI.CultistFactory;

public sealed class CultistFactoryBUI : BoundUserInterface
{
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly EntityManager _entityManager = default!;
    private RadialContainer? _radialContainer;

    private bool _updated = false;

    public CultistFactoryBUI(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
        IoCManager.InjectDependencies(this);
    }
    private void ResetUI()
    {
        _radialContainer?.Dispose();
        _radialContainer = null;
        _updated = false;
    }

    protected override void Open()
    {
        base.Open();

        if (_radialContainer != null)
            ResetUI();

        if(!CanOpen())
            return;

        _radialContainer = new RadialContainer();

        _radialContainer.Closed += Close;

        if (State != null)
            UpdateState(State);
    }

    private void PopulateRadial(IReadOnlyCollection<string> ids)
    {
        foreach (var id in ids)
        {
            if (!_prototypeManager.TryIndex<CultistFactoryProductionPrototype>(id, out var prototype))
                return;

            if (_radialContainer == null)
                continue;

            var button = _radialContainer.AddButton(prototype.Name, prototype.Icon);
            button.Controller.OnPressed += _ =>
            {
                Select(id);
            };
        }
    }

    private void Select(string id)
    {
        SendPredictedMessage(new CultistFactoryItemSelectedMessage(id));
        ResetUI();
        Close();
    }

    private bool CanOpen()
    {
        var localPlayer = IoCManager.Resolve<IPlayerManager>().LocalPlayer;

        var uid = localPlayer?.ControlledEntity;

        return uid != null && _entityManager.HasComponent<CultistComponent>(uid);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        ResetUI();
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (_updated)
            return;

        if (state is CultistFactoryBUIState newState)
        {
            PopulateRadial(newState.Ids);
        }

        if (_radialContainer == null)
            return;

        _radialContainer?.OpenAttachedLocalPlayer();
        _updated = true;
    }
}
