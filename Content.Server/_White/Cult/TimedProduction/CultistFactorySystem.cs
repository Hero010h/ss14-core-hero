﻿using Content.Shared.DoAfter;
using Content.Shared.Examine;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared._White.Cult;
using Content.Shared._White.Cult.Components;
using Content.Shared._White.Cult.Structures;
using Content.Shared._White.Cult.Systems;
using Content.Shared._White.Cult.UI;
using Content.Shared.UserInterface;
using Robust.Server.GameObjects;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using CultistComponent = Content.Shared._White.Cult.Components.CultistComponent;

namespace Content.Server._White.Cult.TimedProduction;

public sealed class CultistFactorySystem : EntitySystem
{
    [Dependency] private readonly TransformSystem _transform = default!;
    [Dependency] private readonly EntityManager _entityManager = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly SharedHandsSystem _handsSystem = default!;
    [Dependency] private readonly IGameTiming _gameTiming = default!;
    [Dependency] private readonly SharedPopupSystem _popupSystem = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfterSystem = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly PhysicsSystem _physics = default!;

    private const string RitualDaggerPrototypeId = "RitualDagger";

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<CultistFactoryComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<CultistFactoryComponent, CultistFactoryItemSelectedMessage>(OnSelected);

        SubscribeLocalEvent<CultistFactoryComponent, InteractUsingEvent>(TryToggleAnchor);
        SubscribeLocalEvent<CultistFactoryComponent, BeforeActivatableUIOpenEvent>((u, c, args) => UpdateUserInterfaceState(u, args, c));
        SubscribeLocalEvent<CultistFactoryComponent, ActivatableUIOpenAttemptEvent>((u, c, args) => OnActivatableUI(u, args, c));
        SubscribeLocalEvent<CultistFactoryComponent, CultAnchorDoAfterEvent>(OnAnchorDoAfter);
        SubscribeLocalEvent<CultistFactoryComponent, ExaminedEvent>(OnExamine);
        SubscribeLocalEvent<CultistFactoryComponent, ConcealEvent>(OnConceal);
    }

    private void OnConceal(Entity<CultistFactoryComponent> ent, ref ConcealEvent args)
    {
        _physics.SetCanCollide(ent, !args.Conceal);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var structures = EntityQuery<CultistFactoryComponent>();
        foreach (var structure in structures)
        {
            if (structure.Active)
                continue;

            if (_gameTiming.CurTime > structure.NextTimeUse)
            {
                structure.Active = true;
                UpdateAppearance(structure.Owner, structure);
            }
        }
    }

    public void UpdateUserInterfaceState(EntityUid uid, BeforeActivatableUIOpenEvent args, CultistFactoryComponent? component = null)
    {
        if (!Resolve(uid, ref component))
            return;

        _ui.SetUiState(uid, CultistAltarUiKey.Key, new CultistFactoryBUIState(component.Products));

        Dirty(uid, component);
    }

    private void OnActivatableUI(EntityUid uid, ActivatableUIOpenAttemptEvent args, CultistFactoryComponent? component = null)
    {
        if (!Resolve(uid, ref component))
            return;

        var user = args.User;

        if (!HasComp<CultistComponent>(user))
            args.Cancel();

        Dirty(uid, component);
    }

    private void OnInit(EntityUid uid, CultistFactoryComponent component, ComponentInit args)
    {
        UpdateAppearance(uid, component);
    }

    private void OnSelected(EntityUid uid, CultistFactoryComponent component, CultistFactoryItemSelectedMessage args)
    {
        var user = args.Actor;

        if (!CanCraft(uid, component, user))
            return;

        if (!_prototypeManager.TryIndex<CultistFactoryProductionPrototype>(args.Item, out var prototype))
            return;

        foreach (var item in prototype.Item)
        {
            var entity = Spawn(item, Transform(user).Coordinates);
            _handsSystem.TryPickupAnyHand(user, entity);
        }

        component.NextTimeUse = _gameTiming.CurTime + TimeSpan.FromSeconds(component.Cooldown);
        component.Active = false;
        UpdateAppearance(uid, component);
    }

    private void TryToggleAnchor(EntityUid uid, CultistFactoryComponent component, InteractUsingEvent args)
    {
        var entityPrototype = _entityManager.GetComponent<MetaDataComponent>(args.Used).EntityPrototype;

        if (entityPrototype == null)
            return;

        var used = entityPrototype.ID;
        var user = args.User;
        const int time = 2;

        if (used != RitualDaggerPrototypeId)
            return;

        if (!HasComp<CultistComponent>(user))
            return;

        var xform = Transform(uid);
        var ev = new CultAnchorDoAfterEvent(xform.Anchored);
        var doArgs = new DoAfterArgs(_entityManager, user, time, ev, uid, uid);

        _doAfterSystem.TryStartDoAfter(doArgs);
    }

    private void OnAnchorDoAfter(EntityUid uid, CultistFactoryComponent component, CultAnchorDoAfterEvent args)
    {
        if (!args.Target.HasValue)
            return;

        var target = args.Target.Value;
        var xform = Transform(target);

        if (args.IsAnchored)
        {
            _transform.Unanchor(target, xform);
            _popup.PopupEntity(Loc.GetString("anchorable-unanchored"), uid, args.User);
        }
        else
        {
            _transform.AnchorEntity(target, xform);
            _popup.PopupEntity(Loc.GetString("anchorable-anchored"), uid, args.User);
        }

        _audio.PlayPvs("/Audio/Items/ratchet.ogg", uid);
    }

    private void OnExamine(EntityUid uid, CultistFactoryComponent component, ExaminedEvent args)
    {
        if (!HasComp<CultistComponent>(args.Examiner))
            return;

        var isAnchored = Comp<TransformComponent>(uid).Anchored;
        var messageId = isAnchored ? "examinable-anchored" : "examinable-unanchored";
        args.PushMarkup(Loc.GetString(messageId, ("target", uid)));
    }

    private bool CanCraft(EntityUid uid, CultistFactoryComponent component, EntityUid user)
    {
        if (component.NextTimeUse == null || _gameTiming.CurTime > component.NextTimeUse)
        {
            component.Active = true;
            UpdateAppearance(uid, component);
            return true;
        }

        var name = MetaData(uid).EntityName;
        var totalSeconds = (component.NextTimeUse - _gameTiming.CurTime).Value.TotalSeconds;
        var seconds = Convert.ToInt32(totalSeconds);

        _popupSystem.PopupEntity(Loc.GetString("cultist-factory-charging", ("name", name),
            ("seconds", seconds)), uid, user);

        UpdateAppearance(uid, component);
        return false;
    }

    private void UpdateAppearance(EntityUid uid, CultistFactoryComponent component)
    {
        AppearanceComponent? appearance = null;
        if (!Resolve(uid, ref appearance, false))
            return;

        _appearance.SetData(uid, CultCraftStructureVisuals.Activated, component.Active);
    }
}
