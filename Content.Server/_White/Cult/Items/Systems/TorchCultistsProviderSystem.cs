﻿using Content.Server.Atmos.EntitySystems;
using Content.Server.Popups;
using Content.Server.Station.Systems;
using Content.Server.Station.Components;
using Content.Server._White.Cult.Items.Components;
using Content.Shared._White.Cult.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Item;
using Content.Shared.Mind.Components;
using Content.Shared.Physics;
using Content.Shared._White.Cult.Items;
using Content.Shared.Movement.Pulling.Systems;
using Robust.Server.GameObjects;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Player;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._White.Cult.Items.Systems;

public sealed class TorchCultistsProviderSystem : EntitySystem
{
    [Dependency] private readonly AtmosphereSystem _atmosphere = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly SharedTransformSystem _xform = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly StationSystem _station = default!;
    [Dependency] private readonly SharedPointLightSystem _pointLight = default!;
    [Dependency] private readonly SharedInteractionSystem _interactionSystem = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly PullingSystem _pulling = default!;
    [Dependency] private readonly MapSystem _map = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<TorchCultistsProviderComponent, AfterInteractEvent>(OnInteract);
        SubscribeLocalEvent<TorchCultistsProviderComponent, TorchWindowItemSelectedMessage>(OnCultistSelected);

        SubscribeLocalEvent<TorchCultistsProviderComponent, ComponentInit>(OnInit);
    }

    private void OnInit(EntityUid uid, TorchCultistsProviderComponent component, ComponentInit args)
    {
        UpdateAppearance(uid, component);
    }

    private void OnInteract(EntityUid uid, TorchCultistsProviderComponent comp, AfterInteractEvent args)
    {
        if (!args.Target.HasValue)
            return;

        if (!_interactionSystem.InRangeUnobstructed(args.User, args.Target.Value))
            return;

        if (!TryComp<TorchCultistsProviderComponent>(uid, out var provider))
            return;

        if (!HasComp<CultistComponent>(args.User))
        {
            _hands.TryDrop(args.User);
            _popup.PopupEntity(Loc.GetString("cult-torch-not-cultist"), args.User, args.User);
            return;
        }

        if (!provider.Active || provider.UsesLeft <= 0)
        {
            _popup.PopupEntity(Loc.GetString("cult-torch-drained"), args.User, args.User);
            return;
        }

        if (provider.NextUse > _timing.CurTime)
        {
            _popup.PopupEntity(Loc.GetString("cult-torch-cooldown"), args.User, args.User);
            return;
        }

        if (HasComp<MindContainerComponent>(args.Target))
        {
            TeleportToRandomLocation(uid, args, comp);
            return;
        }

        if (!HasComp<ItemComponent>(args.Target))
            return;

        provider.ItemSelected = args.Target;

        var cultistsQuery = EntityQueryEnumerator<CultistComponent>();
        var list = new Dictionary<string, string>();

        while (cultistsQuery.MoveNext(out var cultistUid, out _))
        {
            if (!TryComp<MetaDataComponent>(cultistUid, out var meta))
                return;

            if (cultistUid == args.User)
                continue;

            list.Add(cultistUid.ToString(), meta.EntityName);
        }

        if (list.Count == 0)
        {
            _popup.PopupEntity(Loc.GetString("cult-torch-cultists-not-found"), args.User, args.User);
            return;
        }

        _ui.SetUiState(uid, comp.UserInterfaceKey, new TorchWindowBUIState(list));

        if (!HasComp<ActorComponent>(args.User))
            return;

        _ui.TryToggleUi(uid, comp.UserInterfaceKey, args.User);
    }

    private void OnCultistSelected(
        EntityUid uid,
        TorchCultistsProviderComponent component,
        TorchWindowItemSelectedMessage args)
    {
        var entityUid = args.Actor;
        var cultistsQuery = EntityQueryEnumerator<CultistComponent>();

        while (cultistsQuery.MoveNext(out var cultistUid, out _))
        {
            if (cultistUid.ToString() == args.EntUid)
                entityUid = cultistUid;
        }

        if (entityUid == args.Actor)
        {
            _popup.PopupEntity(Loc.GetString("cult-torch-no-cultist"), entityUid, entityUid);
            return;
        }

        if (component.ItemSelected != null)
        {
            var item = component.ItemSelected.Value;

            if (!TryComp<TransformComponent>(entityUid, out var xForm))
                return;

            _xform.SetCoordinates(item, xForm.Coordinates);
            _hands.PickupOrDrop(entityUid, item);
        }

        UpdateUsesCount(uid, args.Actor, component);
    }

    private void UpdateAppearance(EntityUid uid, TorchCultistsProviderComponent component)
    {
        AppearanceComponent? appearance = null;
        if (!Resolve(uid, ref appearance, false))
            return;

        _appearance.SetData(uid, VoidTorchVisuals.Activated, component.Active, appearance);
    }

    private void TeleportToRandomLocation(EntityUid torch, InteractEvent args, TorchCultistsProviderComponent component)
    {
        if (!args.Target.HasValue)
        {
            return;
        }

        if (_pulling.TryGetPulledEntity(args.User, out var pulled) || pulled != args.Target.Value)
        {
            return;
        }

        var ownerTransform = Transform(args.User);

        if (_station.GetStationInMap(ownerTransform.MapID) is not { } station ||
            !TryComp<StationDataComponent>(station, out var stationData) ||
            _station.GetLargestGrid(stationData) is not { } grid)
        {
            if (ownerTransform.GridUid == null)
                return;

            grid = ownerTransform.GridUid.Value;
        }

        if (!TryComp<MapGridComponent>(grid, out var mapGrid))
        {
            return;
        }

        var gridTransform = Transform(grid);
        var gridBounds = mapGrid.LocalAABB.Scale(0.7f); // чтобы не заспавнить на самом краю станции

        var targetCoords = gridTransform.Coordinates;

        for (var i = 0; i < 25; i++)
        {
            var randomX = _random.Next((int) gridBounds.Left, (int) gridBounds.Right);
            var randomY = _random.Next((int) gridBounds.Bottom, (int) gridBounds.Top);

            var tile = new Vector2i(randomX, randomY);

            // no air-blocked areas.
            if (_atmosphere.IsTileSpace(grid, gridTransform.MapUid, tile) ||
                _atmosphere.IsTileAirBlocked(grid, tile, mapGridComp: mapGrid))
            {
                continue;
            }

            // don't spawn inside of solid objects
            var physQuery = GetEntityQuery<PhysicsComponent>();
            var valid = true;
            foreach (var ent in _map.GetAnchoredEntities(grid, mapGrid, tile))
            {
                if (!physQuery.TryGetComponent(ent, out var body))
                    continue;

                if (body.BodyType != BodyType.Static ||
                    !body.Hard ||
                    (body.CollisionLayer & (int) CollisionGroup.LargeMobMask) == 0)
                    continue;

                valid = false;
                break;
            }

            if (!valid)
                continue;

            targetCoords = _map.GridTileToLocal(grid, mapGrid, tile);
            break;
        }

        _xform.SetCoordinates(args.User, targetCoords);
        _xform.SetCoordinates(args.Target.Value, targetCoords);

        UpdateUsesCount(torch, args.User, component);
    }

    private void UpdateUsesCount(EntityUid torch, EntityUid? user, TorchCultistsProviderComponent component)
    {
        component.ItemSelected = null;
        component.NextUse = _timing.CurTime + component.Cooldown;
        component.UsesLeft--;

        if (user.HasValue)
        {
            _popup.PopupEntity(Loc.GetString("cult-torch-item-send"), user.Value);
        }

        if (component.UsesLeft <= 0)
        {
            component.Active = false;
            UpdateAppearance(torch, component);

            if (!TryComp<PointLightComponent>(torch, out var light))
                return;

            _pointLight.SetEnabled(torch, false, light);
        }
    }
}
