﻿using Content.Shared.CCVar;
using Content.Shared.Ghost;
using Content.Shared.Mind;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.NPC;
using Content.Shared.SSDIndicator;
using Content.Shared.StatusIcon;
using Content.Shared.StatusIcon.Components;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;

namespace Content.Client.SSDIndicator;

/// <summary>
///     Handles displaying SSD indicator as status icon
/// </summary>
public sealed class SSDIndicatorSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SSDIndicatorComponent, GetStatusIconsEvent>(OnGetStatusIcon);
    }

    private void OnGetStatusIcon(EntityUid uid, SSDIndicatorComponent component, ref GetStatusIconsEvent args)
    {
        if (component.IsSSD &&
            _cfg.GetCVar(CCVars.ICShowSSDIndicator) &&
            !args.InContainer &&
            !_mobState.IsDead(uid) &&
            !HasComp<ActiveNPCComponent>(uid) &&
            TryComp<MindContainerComponent>(uid, out var mindContainer) &&
            mindContainer.ShowExamineInfo &&
            !IsAghosted(mindContainer)) // WD EDIT
        {
            args.StatusIcons.Add(_prototype.Index<StatusIconPrototype>(component.Icon));
        }
    }

    private bool IsAghosted(MindContainerComponent mindContainer) // WD
    {
        if (!TryComp(mindContainer.Mind, out MindComponent? mindComp))
            return false;

        return TryComp(mindComp.VisitingEntity, out GhostComponent? ghost) && ghost.CanGhostInteract;
    }
}
