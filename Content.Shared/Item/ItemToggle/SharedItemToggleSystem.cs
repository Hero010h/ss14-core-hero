using Content.Shared.Examine;
using Content.Shared.Interaction.Events;
using Content.Shared.Item.ItemToggle.Components;
using Content.Shared.Popups;
using Content.Shared.Temperature;
using Content.Shared.Toggleable;
using Content.Shared.Wieldable;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Network;

namespace Content.Shared.Item.ItemToggle;
/// <summary>
/// Handles generic item toggles, like a welder turning on and off, or an e-sword.
/// </summary>
/// <remarks>
/// If you need extended functionality (e.g. requiring power) then add a new component and use events.
/// </remarks>
public abstract class SharedItemToggleSystem : EntitySystem
{
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly SharedPointLightSystem _light = default!;
    [Dependency] private readonly INetManager _netManager = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;

    [Dependency] private readonly SharedItemSystem _item = default!; // WD

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ItemToggleComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<ItemToggleComponent, ItemUnwieldedEvent>(TurnOffonUnwielded);
        SubscribeLocalEvent<ItemToggleComponent, ItemWieldedEvent>(TurnOnonWielded);
        SubscribeLocalEvent<ItemToggleComponent, UseInHandEvent>(OnUseInHand);

        SubscribeLocalEvent<ItemToggleHotComponent, IsHotEvent>(OnIsHotEvent);

        SubscribeLocalEvent<ItemToggleActiveSoundComponent, ItemToggledEvent>(UpdateActiveSound);

        SubscribeLocalEvent<ItemToggleComponent, ExaminedEvent>(OnExamined); // WD
        SubscribeLocalEvent<ItemToggleComponent, ItemToggledEvent>(UpdatePrefix); // WD
    }

    private void OnStartup(Entity<ItemToggleComponent> ent, ref ComponentStartup args)
    {
        UpdateVisuals(ent);
    }

    private void OnUseInHand(EntityUid uid, ItemToggleComponent itemToggle, UseInHandEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;

        Toggle(uid, args.User, predicted: itemToggle.Predictable, itemToggle: itemToggle);
    }

    /// <summary>
    /// Used when an item is attempted to be toggled.
    /// </summary>
    public void Toggle(EntityUid uid, EntityUid? user = null, bool predicted = true, ItemToggleComponent? itemToggle = null)
    {
        if (!Resolve(uid, ref itemToggle))
            return;

        if (itemToggle.Activated)
        {
            TryDeactivate(uid, user, itemToggle: itemToggle, predicted: predicted);
        }
        else
        {
            TryActivate(uid, user, itemToggle: itemToggle, predicted: predicted);
        }
    }

    /// <summary>
    /// Used when an item is attempting to be activated. It returns false if the attempt fails any reason, interrupting the activation.
    /// </summary>
    public bool TryActivate(EntityUid uid, EntityUid? user = null, bool predicted = true, ItemToggleComponent? itemToggle = null)
    {
        if (!Resolve(uid, ref itemToggle))
            return false;

        if (itemToggle.Activated)
            return true;

        if (!itemToggle.Predictable && _netManager.IsClient)
            return true;

        var attempt = new ItemToggleActivateAttemptEvent(user);
        RaiseLocalEvent(uid, ref attempt);

        if (attempt.Cancelled)
        {
            if (predicted)
                _audio.PlayPredicted(itemToggle.SoundFailToActivate, uid, user);
            else
                _audio.PlayPvs(itemToggle.SoundFailToActivate, uid);

            if (attempt.Popup != null && user != null)
            {
                if (predicted)
                    _popup.PopupClient(attempt.Popup, uid, user.Value);
                else
                    _popup.PopupEntity(attempt.Popup, uid, user.Value);
            }

            return false;
        }

        Activate(uid, itemToggle, predicted, user);

        return true;
    }

    /// <summary>
    /// Used when an item is attempting to be deactivated. It returns false if the attempt fails any reason, interrupting the deactivation.
    /// </summary>
    public bool TryDeactivate(EntityUid uid, EntityUid? user = null, bool predicted = true, ItemToggleComponent? itemToggle = null)
    {
        if (!Resolve(uid, ref itemToggle))
            return false;

        if (!itemToggle.Predictable && _netManager.IsClient)
            return true;

        if (!itemToggle.Activated)
            return true;

        var attempt = new ItemToggleDeactivateAttemptEvent(user);
        RaiseLocalEvent(uid, ref attempt);

        if (attempt.Cancelled)
        {
            return false;
        }

        Deactivate(uid, itemToggle, predicted, user);
        return true;
    }

    private void Activate(EntityUid uid, ItemToggleComponent itemToggle, bool predicted, EntityUid? user = null)
    {
        // TODO: Fix this hardcoding
        TryComp(uid, out AppearanceComponent? appearance);
        _appearance.SetData(uid, ToggleableLightVisuals.Enabled, true, appearance);
        _appearance.SetData(uid, ToggleVisuals.Toggled, true, appearance);

        if (_light.TryGetLight(uid, out var light))
        {
            _light.SetEnabled(uid, true, light);
        }

        var soundToPlay = itemToggle.SoundActivate;
        if (predicted)
            _audio.PlayPredicted(soundToPlay, uid, user);
        else
            _audio.PlayPvs(soundToPlay, uid);

        // END FIX HARDCODING

        var toggleUsed = new ItemToggledEvent(predicted, Activated: true, user);
        RaiseLocalEvent(uid, ref toggleUsed);

        itemToggle.Activated = true;
        UpdateVisuals((uid, itemToggle));
        Dirty(uid, itemToggle);
    }

    /// <summary>
    /// Used to make the actual changes to the item's components on deactivation.
    /// </summary>
    private void Deactivate(EntityUid uid, ItemToggleComponent itemToggle, bool predicted, EntityUid? user = null)
    {
        var soundToPlay = itemToggle.SoundDeactivate;
        if (predicted)
            _audio.PlayPredicted(soundToPlay, uid, user);
        else
            _audio.PlayPvs(soundToPlay, uid);
        // END FIX HARDCODING

        var toggleUsed = new ItemToggledEvent(predicted, Activated: false, user);
        RaiseLocalEvent(uid, ref toggleUsed);

        itemToggle.Activated = false;
        UpdateVisuals((uid, itemToggle));
        Dirty(uid, itemToggle);
    }

    private void UpdateVisuals(Entity<ItemToggleComponent> ent)
    {
        if (TryComp(ent, out AppearanceComponent? appearance))
        {
            _appearance.SetData(ent, ToggleVisuals.Toggled, ent.Comp.Activated, appearance);

            if (ent.Comp.ToggleLight)
                _appearance.SetData(ent, ToggleableLightVisuals.Enabled, ent.Comp.Activated, appearance);
        }

        if (!ent.Comp.ToggleLight)
            return;

        if (_light.TryGetLight(ent, out var light))
            _light.SetEnabled(ent, ent.Comp.Activated, light);
    }

    /// <summary>
    /// Used for items that require to be wielded in both hands to activate. For instance the dual energy sword will turn off if not wielded.
    /// </summary>
    private void TurnOffonUnwielded(EntityUid uid, ItemToggleComponent itemToggle, ItemUnwieldedEvent args)
    {
        if (itemToggle.Activated)
            TryDeactivate(uid, args.User, itemToggle: itemToggle);
    }

    /// <summary>
    /// Wieldable items will automatically turn on when wielded.
    /// </summary>
    private void TurnOnonWielded(EntityUid uid, ItemToggleComponent itemToggle, ref ItemWieldedEvent args)
    {
        if (!itemToggle.Activated)
            TryActivate(uid, args.User, itemToggle: itemToggle); // WD added "args.User" parameter
    }

    public bool IsActivated(EntityUid uid, ItemToggleComponent? comp = null)
    {
        if (!Resolve(uid, ref comp, false))
            return true; // assume always activated if no component

        return comp.Activated;
    }

    /// <summary>
    /// Used to make the item hot when activated.
    /// </summary>
    private void OnIsHotEvent(EntityUid uid, ItemToggleHotComponent itemToggleHot, IsHotEvent args)
    {
        args.IsHot |= IsActivated(uid);
    }

    /// <summary>
    /// Used to update the looping active sound linked to the entity.
    /// </summary>
    private void UpdateActiveSound(EntityUid uid, ItemToggleActiveSoundComponent activeSound, ref ItemToggledEvent args)
    {
        if (_netManager.IsClient) // WD EDIT, FUCK THIS (desword sound broken)
            return;

        if (args.Activated)
        {
            if (activeSound.ActiveSound != null && activeSound.PlayingStream == null)
            {
                activeSound.PlayingStream = _audio.PlayPvs(activeSound.ActiveSound, uid, AudioParams.Default.WithLoop(true)) .Value.Entity;
            }
        }
        else
        {
            activeSound.PlayingStream = _audio.Stop(activeSound.PlayingStream);
        }
    }

    // WD added start
    private void OnExamined(Entity<ItemToggleComponent> ent, ref ExaminedEvent args)
    {
        var onMsg = IsActivated(ent.Owner)
            ? Loc.GetString(ent.Comp.ActivatedDescription)
            : Loc.GetString(ent.Comp.DeactivatedDescription);

        args.PushMarkup(onMsg);
    }

    private void UpdatePrefix(Entity<ItemToggleComponent> ent, ref ItemToggledEvent args)
    {
        _item.SetHeldPrefix(ent.Owner, args.Activated ? "on" : "off");
    }

    // WD added end
}
