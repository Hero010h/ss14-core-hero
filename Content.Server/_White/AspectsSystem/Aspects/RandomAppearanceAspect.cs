using Content.Server.GameTicking;
using Content.Server.GameTicking.Rules.Components;
using Content.Server._White.AspectsSystem.Aspects.Components;
using Content.Server._White.AspectsSystem.Base;
using Content.Server._White.Other.RandomHumanSystem;
using Content.Server.GameTicking.Components;
using Content.Server.StationRecords.Systems;
using Content.Shared.Humanoid;

namespace Content.Server._White.AspectsSystem.Aspects;

public sealed class RandomAppearanceAspect : AspectSystem<RandomAppearanceAspectComponent>
{
    [Dependency] private readonly ChatHelper _chatHelper = default!;
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(HandleLateJoin, after: new[] { typeof(StationRecordsSystem) });
    }

    protected override void Started(EntityUid uid, RandomAppearanceAspectComponent component, GameRuleComponent gameRule, GameRuleStartedEvent args)
    {
        base.Started(uid, component, gameRule, args);
        var query = EntityQueryEnumerator<HumanoidAppearanceComponent>();
        while (query.MoveNext(out var ent, out _))
        {
            EnsureComp<RandomHumanComponent>(ent);
        }
    }

    private void HandleLateJoin(PlayerSpawnCompleteEvent ev)
    {
        var query = EntityQueryEnumerator<RandomAppearanceAspectComponent, GameRuleComponent>();
        while (query.MoveNext(out var ruleEntity, out _, out var gameRule))
        {
            if (!GameTicker.IsGameRuleAdded(ruleEntity, gameRule))
                continue;

            if (!ev.LateJoin)
                return;

            var mob = ev.Mob;

            EnsureComp<RandomHumanComponent>(mob);
            _chatHelper.SendAspectDescription(mob, Loc.GetString("random-appearance-aspect-desc"));
        }
    }
}
