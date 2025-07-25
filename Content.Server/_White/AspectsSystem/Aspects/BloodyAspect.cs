using Content.Server.GameTicking.Rules.Components;
using Content.Server._White.AspectsSystem.Aspects.Components;
using Content.Server._White.AspectsSystem.Base;
using Content.Server.GameTicking.Components;
using Content.Shared._White;
using Robust.Shared.Configuration;

namespace Content.Server._White.AspectsSystem.Aspects;

public sealed class BloodyAspect : AspectSystem<BloodyAspectComponent>
{
    [Dependency] private readonly IConfigurationManager _cfg = default!;

    protected override void Started(EntityUid uid, BloodyAspectComponent component, GameRuleComponent gameRule, GameRuleStartedEvent args)
    {
        base.Started(uid, component, gameRule, args);

        _cfg.SetCVar(WhiteCVars.DamageGetModifier, 2.5f);
    }

    protected override void Ended(EntityUid uid, BloodyAspectComponent component, GameRuleComponent gameRule, GameRuleEndedEvent args)
    {
        base.Ended(uid, component, gameRule, args);

        _cfg.SetCVar(WhiteCVars.DamageGetModifier, 1.0f);
    }
}
