using Content.Server.GameTicking;
using Content.Server.GameTicking.Rules.Components;
using Content.Server._White.AspectsSystem.Aspects.Components;
using Content.Server._White.AspectsSystem.Base;
using Content.Server.GameTicking.Components;
using Content.Shared.Drunk;
using Content.Shared.Humanoid;

namespace Content.Server._White.AspectsSystem.Aspects;

public sealed class DrunkAspect : AspectSystem<DrunkAspectComponent>
{
    [Dependency] private readonly SharedDrunkSystem _drunkSystem = default!;
    [Dependency] private readonly ChatHelper _chatHelper = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(HandleLateJoin);
    }

    protected override void Started(EntityUid uid, DrunkAspectComponent component, GameRuleComponent gameRule, GameRuleStartedEvent args)
    {
        base.Started(uid, component, gameRule, args);

        var query = EntityQueryEnumerator<HumanoidAppearanceComponent>();
        while (query.MoveNext(out var ent, out _))
        {
            _drunkSystem.TryApplyDrunkenness(ent, 50);
        }
    }

    private void HandleLateJoin(PlayerSpawnCompleteEvent ev)
    {
        var query = EntityQueryEnumerator<DrunkAspectComponent, GameRuleComponent>();
        while (query.MoveNext(out var ruleEntity, out _, out var gameRule))
        {
            if (!GameTicker.IsGameRuleAdded(ruleEntity, gameRule))
                continue;

            if (!ev.LateJoin)
                return;

            var mob = ev.Mob;

            _drunkSystem.TryApplyDrunkenness(mob, 50);

            _chatHelper.SendAspectDescription(mob, Loc.GetString("drunk-aspect-desc"));
        }
    }
}
