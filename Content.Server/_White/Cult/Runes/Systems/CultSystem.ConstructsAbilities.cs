﻿using System.Linq;
using Content.Server.GameTicking;
using Content.Server.GameTicking.Rules.Components;
using Content.Server.Popups;
using Content.Server._White.Cult.GameRule;
using Content.Server._White.IncorporealSystem;
using Content.Server.GameTicking.Components;
using Content.Shared.Actions;
using Content.Shared.Coordinates.Helpers;
using Content.Shared.Interaction.Events;
using Content.Shared.Maps;
using Content.Shared.Mobs.Components;
using Content.Shared.Physics;
using Content.Shared.StatusEffect;
using Content.Shared._White.Cult;
using Content.Shared._White.Cult.Components;
using Content.Shared.Throwing;

namespace Content.Server._White.Cult.Runes.Systems;

public partial class CultSystem
{
    [Dependency] private readonly TileSystem _tileSystem = default!;
    [Dependency] private readonly PopupSystem _popupSystem = default!;
    [Dependency] private readonly TurfSystem _turf = default!;
    [Dependency] private readonly StatusEffectsSystem _statusEffectsSystem = default!;
    [Dependency] private readonly SharedActionsSystem _actionsSystem = default!;
    [Dependency] private readonly GameTicker _gameTicker = default!;

    public void InitializeConstructsAbilities()
    {
        SubscribeLocalEvent<ArtificerCreateSoulStoneActionEvent>(OnArtificerCreateSoulStone);
        SubscribeLocalEvent<ArtificerCreateConstructShellActionEvent>(OnArtificerCreateConstructShell);
        SubscribeLocalEvent<ArtificerConvertCultistFloorActionEvent>(OnArtificerConvertCultistFloor);
        SubscribeLocalEvent<ArtificerCreateCultistWallActionEvent>(OnArtificerCreateCultistWall);
        SubscribeLocalEvent<ArtificerCreateCultistAirlockActionEvent>(OnArtificerCreateCultistAirlock);

        SubscribeLocalEvent<WraithPhaseActionEvent>(OnWraithPhase);
        SubscribeLocalEvent<IncorporealComponent, AttackAttemptEvent>(OnAttackAttempt);
        SubscribeLocalEvent<IncorporealComponent, ThrowAttemptEvent>(OnThrowAttempt);

        SubscribeLocalEvent<JuggernautCreateWallActionEvent>(OnJuggernautCreateWall);

        SubscribeLocalEvent<ConstructComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<ConstructComponent, ComponentInit>(OnConstructInit);
        SubscribeLocalEvent<ConstructComponent, ComponentRemove>(OnConstructComponentRemoved);
    }

    private void OnMapInit(EntityUid uid, ConstructComponent component, MapInitEvent args)
    {
        foreach (var action in component.Actions.Select(id => _actionsSystem.AddAction(uid, id)))
        {
            _actionsSystem.StartUseDelay(action);
            component.ActionEntities.Add(action);
        }
    }

    private void OnConstructInit(EntityUid uid, ConstructComponent component, ComponentInit args)
    {
        var query = EntityQueryEnumerator<CultRuleComponent, GameRuleComponent>();

        while (query.MoveNext(out var ruleEnt, out var cultRuleComponent, out _))
        {
            if (!_gameTicker.IsGameRuleAdded(ruleEnt))
                continue;

            cultRuleComponent.Constructs.Add(component);
        }
    }

    private void OnConstructComponentRemoved(EntityUid uid, ConstructComponent component, ComponentRemove args)
    {
        foreach (var entity in component.ActionEntities)
        {
            _actionsSystem.RemoveAction(uid, entity);
        }

        var query = EntityQueryEnumerator<CultRuleComponent, GameRuleComponent>();
        while (query.MoveNext(out var ruleEnt, out var cultRuleComponent, out _))
        {
            if (!_gameTicker.IsGameRuleAdded(ruleEnt))
                continue;

            cultRuleComponent.Constructs.Remove(component);
        }
    }

    private void OnArtificerCreateSoulStone(ArtificerCreateSoulStoneActionEvent ev)
    {
        var transform = Transform(ev.Performer);
        Spawn(ev.SoulStonePrototypeId, transform.Coordinates);

        ev.Handled = true;
    }

    private void OnArtificerCreateConstructShell(ArtificerCreateConstructShellActionEvent ev)
    {
        var transform = Transform(ev.Performer);
        Spawn(ev.ShellPrototypeId, transform.Coordinates);

        ev.Handled = true;
    }

    private void OnArtificerConvertCultistFloor(ArtificerConvertCultistFloorActionEvent ev)
    {
        var transform = Transform(ev.Performer);
        var gridUid = transform.GridUid;

        if (!gridUid.HasValue)
        {
            _popupSystem.PopupEntity("Нельзя строить в космосе...", ev.Performer, ev.Performer);
            return;
        }

        var tileRef = transform.Coordinates.GetTileRef();

        if (!tileRef.HasValue)
        {
            _popupSystem.PopupEntity("Нельзя строить в космосе...", ev.Performer, ev.Performer);
            return;
        }

        var cultistTileDefinition = (ContentTileDefinition) _tileDefinition[ev.FloorTileId];
        _tileSystem.ReplaceTile(tileRef.Value, cultistTileDefinition);
        Spawn("CultTileSpawnEffect", transform.Coordinates);
        ev.Handled = true;
    }

    private void OnArtificerCreateCultistWall(ArtificerCreateCultistWallActionEvent ev)
    {
        if (!TrySpawnWall(ev.Performer, ev.WallPrototypeId))
        {
            return;
        }

        ev.Handled = true;
    }

    private void OnArtificerCreateCultistAirlock(ArtificerCreateCultistAirlockActionEvent ev)
    {
        if (!TrySpawnWall(ev.Performer, ev.AirlockPrototypeId))
        {
            return;
        }

        ev.Handled = true;
    }

    private void OnWraithPhase(WraithPhaseActionEvent ev)
    {
        if (_statusEffectsSystem.HasStatusEffect(ev.Performer, ev.StatusEffectId))
        {
            _popupSystem.PopupEntity("Вы уже в потустороннем мире", ev.Performer, ev.Performer);
            return;
        }

        _statusEffectsSystem.TryAddStatusEffect<IncorporealComponent>(ev.Performer, ev.StatusEffectId,
            TimeSpan.FromSeconds(ev.Duration), false);

        Spawn("EffectEmpPulse", Transform(ev.Performer).Coordinates);

        ev.Handled = true;
    }

    private void OnAttackAttempt(EntityUid uid, IncorporealComponent component, AttackAttemptEvent args)
    {
        if (_statusEffectsSystem.HasStatusEffect(args.Uid, "Incorporeal"))
        {
            _statusEffectsSystem.TryRemoveStatusEffect(args.Uid, "Incorporeal");
        }
    }

    private void OnThrowAttempt(Entity<IncorporealComponent> ent, ref ThrowAttemptEvent args)
    {
        if (_statusEffectsSystem.HasStatusEffect(args.Uid, "Incorporeal"))
        {
            _statusEffectsSystem.TryRemoveStatusEffect(args.Uid, "Incorporeal");
        }
    }

    private void OnJuggernautCreateWall(JuggernautCreateWallActionEvent ev)
    {
        if (!TrySpawnWall(ev.Performer, ev.WallPrototypeId))
        {
            return;
        }

        ev.Handled = true;
    }

    private bool TrySpawnWall(EntityUid performer, string wallPrototypeId)
    {
        var xform = Transform(performer);

        var offsetValue = xform.LocalRotation.ToWorldVec().Normalized();
        var coords = xform.Coordinates.Offset(offsetValue).SnapToGrid(EntityManager);
        var tile = coords.GetTileRef();
        if (tile == null)
            return false;

        // Check there are no walls there
        if (_turf.IsTileBlocked(tile.Value, CollisionGroup.Impassable))
        {
            _popupSystem.PopupEntity(Loc.GetString("mime-invisible-wall-failed"), performer, performer);
            return false;
        }

        // Check there are no mobs there;
        foreach (var entity in tile.Value.GetEntitiesInTile())
        {
            if (HasComp<MobStateComponent>(entity) && entity != performer)
            {
                _popupSystem.PopupEntity(Loc.GetString("mime-invisible-wall-failed"), performer, performer);
                return false;
            }
        }

        _popupSystem.PopupEntity(Loc.GetString("mime-invisible-wall-popup", ("mime", performer)), performer);
        // Make sure we set the invisible wall to despawn properly
        Spawn(wallPrototypeId, coords);
        return true;
    }
}
