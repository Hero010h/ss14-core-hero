using Content.Server.Chat.Systems;
using Content.Server.Interaction;
using Content.Server.Popups;
using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Server.Radio.Components;
using Content.Shared._White.DeepSpaceCom; // WD
using Content.Server.Speech;
using Content.Server.Speech.Components;
using Content.Shared.UserInterface;
using Content.Shared.Chat;
using Content.Shared.Examine;
using Content.Shared.Interaction;
using Content.Shared.Radio;
using Content.Shared.Radio.Components;
using Robust.Server.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.Server.Radio.EntitySystems;

/// <summary>
///     This system handles radio speakers and microphones (which together form a hand-held radio).
/// </summary>
public sealed class RadioDeviceSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _protoMan = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly ChatSystem _chat = default!;
    [Dependency] private readonly RadioSystem _radio = default!;
    [Dependency] private readonly InteractionSystem _interaction = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;

    // Used to prevent a shitter from using a bunch of radios to spam chat.
    private HashSet<(string, EntityUid)> _recentlySent = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<RadioMicrophoneComponent, ComponentInit>(OnMicrophoneInit);
        SubscribeLocalEvent<RadioMicrophoneComponent, ExaminedEvent>(OnExamine);
        SubscribeLocalEvent<RadioMicrophoneComponent, ActivateInWorldEvent>(OnActivateMicrophone);
        SubscribeLocalEvent<RadioMicrophoneComponent, ListenEvent>(OnListen);
        SubscribeLocalEvent<RadioMicrophoneComponent, ListenAttemptEvent>(OnAttemptListen);
        SubscribeLocalEvent<RadioMicrophoneComponent, PowerChangedEvent>(OnPowerChanged);

        SubscribeLocalEvent<RadioSpeakerComponent, ComponentInit>(OnSpeakerInit);
        SubscribeLocalEvent<RadioSpeakerComponent, ActivateInWorldEvent>(OnActivateSpeaker);
        SubscribeLocalEvent<RadioSpeakerComponent, RadioReceiveEvent>(OnReceiveRadio);

        SubscribeLocalEvent<IntercomComponent, BeforeActivatableUIOpenEvent>(OnBeforeIntercomUiOpen);
        SubscribeLocalEvent<IntercomComponent, ToggleIntercomMicMessage>(OnToggleIntercomMic);
        SubscribeLocalEvent<IntercomComponent, ToggleIntercomSpeakerMessage>(OnToggleIntercomSpeaker);
        SubscribeLocalEvent<IntercomComponent, SelectIntercomChannelMessage>(OnSelectIntercomChannel);

        SubscribeLocalEvent<DeepSpaceComComponent, BeforeActivatableUIOpenEvent>(OnBeforeDeepSpaceComUiOpen); // WD start
        SubscribeLocalEvent<DeepSpaceComComponent, ToggleDeepSpaceComMicrophoneMessage>(OnToggleDeepSpaceComMic);
        SubscribeLocalEvent<DeepSpaceComComponent, ToggleDeepSpaceComSpeakerMessage>(OnToggleDeepSpaceComSpeaker);
        SubscribeLocalEvent<DeepSpaceComComponent, SelectDeepSpaceComChannelMessage>(OnSelectDeepSpaceComChannel); // WD end
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        _recentlySent.Clear();
    }


    #region Component Init
    private void OnMicrophoneInit(EntityUid uid, RadioMicrophoneComponent component, ComponentInit args)
    {
        if (component.Enabled)
            EnsureComp<ActiveListenerComponent>(uid).Range = component.ListenRange;
        else
            RemCompDeferred<ActiveListenerComponent>(uid);
    }

    private void OnSpeakerInit(EntityUid uid, RadioSpeakerComponent component, ComponentInit args)
    {
        if (component.Enabled)
            EnsureComp<ActiveRadioComponent>(uid).Channels.UnionWith(component.Channels);
        else
            RemCompDeferred<ActiveRadioComponent>(uid);
    }
    #endregion

    #region Toggling
    private void OnActivateMicrophone(EntityUid uid, RadioMicrophoneComponent component, ActivateInWorldEvent args)
    {
        if (!component.ToggleOnInteract)
            return;

        ToggleRadioMicrophone(uid, args.User, args.Handled, component);
        args.Handled = true;
    }

    private void OnActivateSpeaker(EntityUid uid, RadioSpeakerComponent component, ActivateInWorldEvent args)
    {
        if (!component.ToggleOnInteract)
            return;

        ToggleRadioSpeaker(uid, args.User, args.Handled, component);
        args.Handled = true;
    }

    public void ToggleRadioMicrophone(EntityUid uid, EntityUid user, bool quiet = false, RadioMicrophoneComponent? component = null)
    {
        if (!Resolve(uid, ref component))
            return;

        SetMicrophoneEnabled(uid, user, !component.Enabled, quiet, component);
    }

    private void OnPowerChanged(EntityUid uid, RadioMicrophoneComponent component, ref PowerChangedEvent args)
    {
        if (args.Powered)
            return;
        SetMicrophoneEnabled(uid, null, false,  true, component);
    }

    public void SetMicrophoneEnabled(EntityUid uid, EntityUid? user, bool enabled, bool quiet = false, RadioMicrophoneComponent? component = null)
    {
        if (!Resolve(uid, ref component, false))
            return;

        if (component.PowerRequired && !this.IsPowered(uid, EntityManager))
            return;

        component.Enabled = enabled;

        if (!quiet && user != null)
        {
            var state = Loc.GetString(component.Enabled ? "handheld-radio-component-on-state" : "handheld-radio-component-off-state");
            var message = Loc.GetString("handheld-radio-component-on-use", ("radioState", state));
            _popup.PopupEntity(message, user.Value, user.Value);
        }

        _appearance.SetData(uid, RadioDeviceVisuals.Broadcasting, component.Enabled);
        if (component.Enabled)
            EnsureComp<ActiveListenerComponent>(uid).Range = component.ListenRange;
        else
            RemCompDeferred<ActiveListenerComponent>(uid);
    }

    public void ToggleRadioSpeaker(EntityUid uid, EntityUid user, bool quiet = false, RadioSpeakerComponent? component = null)
    {
        if (!Resolve(uid, ref component))
            return;

        SetSpeakerEnabled(uid, user, !component.Enabled, quiet, component);
    }

    public void SetSpeakerEnabled(EntityUid uid, EntityUid user, bool enabled, bool quiet = false, RadioSpeakerComponent? component = null)
    {
        if (!Resolve(uid, ref component))
            return;

        component.Enabled = enabled;

        if (!quiet)
        {
            var state = Loc.GetString(component.Enabled ? "handheld-radio-component-on-state" : "handheld-radio-component-off-state");
            var message = Loc.GetString("handheld-radio-component-on-use", ("radioState", state));
            _popup.PopupEntity(message, user, user);
        }

        _appearance.SetData(uid, RadioDeviceVisuals.Speaker, component.Enabled);
        if (component.Enabled)
            EnsureComp<ActiveRadioComponent>(uid).Channels.UnionWith(component.Channels);
        else
            RemCompDeferred<ActiveRadioComponent>(uid);
    }
    #endregion

    private void OnExamine(EntityUid uid, RadioMicrophoneComponent component, ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        var proto = _protoMan.Index<RadioChannelPrototype>(component.BroadcastChannel);

        using (args.PushGroup(nameof(RadioMicrophoneComponent)))
        {
            args.PushMarkup(Loc.GetString("handheld-radio-component-on-examine", ("frequency", proto.Frequency)));
            args.PushMarkup(Loc.GetString("handheld-radio-component-chennel-examine",
                ("channel", proto.LocalizedName)));
        }
    }

    private void OnListen(EntityUid uid, RadioMicrophoneComponent component, ListenEvent args)
    {
        if (HasComp<RadioSpeakerComponent>(args.Source))
            return; // no feedback loops please.

        if (_recentlySent.Add((args.Message, args.Source)))
            _radio.SendRadioMessage(args.Source, args.Message, _protoMan.Index<RadioChannelPrototype>(component.BroadcastChannel), uid, false); // WD EDIT
    }

    private void OnAttemptListen(EntityUid uid, RadioMicrophoneComponent component, ListenAttemptEvent args)
    {
        if (component.PowerRequired && !this.IsPowered(uid, EntityManager)
            || component.UnobstructedRequired && !_interaction.InRangeUnobstructed(args.Source, uid, component.ListenRange))
        {
            args.Cancel();
        }
    }

    private void OnReceiveRadio(EntityUid uid, RadioSpeakerComponent component, ref RadioReceiveEvent args)
    {
        if (uid == args.RadioSource)
            return;

        var nameEv = new TransformSpeakerNameEvent(args.MessageSource, Name(args.MessageSource));
        RaiseLocalEvent(args.MessageSource, nameEv);

        var name = Loc.GetString("speech-name-relay", ("speaker", Name(uid)),
            ("originalName", nameEv.Name));

        // log to chat so people can identity the speaker/source, but avoid clogging ghost chat if there are many radios
        _chat.TrySendInGameICMessage(uid, args.Message, InGameICChatType.Whisper, ChatTransmitRange.GhostRangeLimit, nameOverride: name, checkRadioPrefix: false);
    }

    private void OnBeforeIntercomUiOpen(EntityUid uid, IntercomComponent component, BeforeActivatableUIOpenEvent args)
    {
        UpdateIntercomUi(uid, component);
    }

    private void OnToggleIntercomMic(EntityUid uid, IntercomComponent component, ToggleIntercomMicMessage args)
    {
        if (component.RequiresPower && !this.IsPowered(uid, EntityManager))
            return;

        SetMicrophoneEnabled(uid, args.Actor, args.Enabled, true);
        UpdateIntercomUi(uid, component);
    }

    private void OnToggleIntercomSpeaker(EntityUid uid, IntercomComponent component, ToggleIntercomSpeakerMessage args)
    {
        if (component.RequiresPower && !this.IsPowered(uid, EntityManager))
            return;

        SetSpeakerEnabled(uid, args.Actor, args.Enabled, true);
        UpdateIntercomUi(uid, component);
    }

    private void OnSelectIntercomChannel(EntityUid uid, IntercomComponent component, SelectIntercomChannelMessage args)
    {
        if (component.RequiresPower && !this.IsPowered(uid, EntityManager))
            return;

        if (!_protoMan.TryIndex<RadioChannelPrototype>(args.Channel, out _) || !component.SupportedChannels.Contains(args.Channel))
            return;

        if (TryComp<RadioMicrophoneComponent>(uid, out var mic))
            mic.BroadcastChannel = args.Channel;
        if (TryComp<RadioSpeakerComponent>(uid, out var speaker))
            speaker.Channels = new(){ args.Channel };
        UpdateIntercomUi(uid, component);
    }

    private void UpdateIntercomUi(EntityUid uid, IntercomComponent component)
    {
        var micComp = CompOrNull<RadioMicrophoneComponent>(uid);
        var speakerComp = CompOrNull<RadioSpeakerComponent>(uid);

        var micEnabled = micComp?.Enabled ?? false;
        var speakerEnabled = speakerComp?.Enabled ?? false;
        var availableChannels = component.SupportedChannels;
        var selectedChannel = micComp?.BroadcastChannel ?? SharedChatSystem.CommonChannel;
        var state = new IntercomBoundUIState(micEnabled, speakerEnabled, availableChannels, selectedChannel);
        _ui.SetUiState(uid, IntercomUiKey.Key, state);
    }

    private void OnBeforeDeepSpaceComUiOpen(EntityUid uid, DeepSpaceComComponent component, BeforeActivatableUIOpenEvent args) // WD start
    {
        UpdateDeepSpaceComUi(uid, component);
    }

    private void OnToggleDeepSpaceComMic(EntityUid uid, DeepSpaceComComponent component, ToggleDeepSpaceComMicrophoneMessage args)
    {
        if (component.RequiresPower && !this.IsPowered(uid, EntityManager))
            return;

        SetMicrophoneEnabled(uid, args.Actor, args.Enabled, true);
        UpdateDeepSpaceComUi(uid, component);
    }

    private void OnToggleDeepSpaceComSpeaker(EntityUid uid, DeepSpaceComComponent component, ToggleDeepSpaceComSpeakerMessage args)
    {
        if (component.RequiresPower && !this.IsPowered(uid, EntityManager))
            return;

        SetSpeakerEnabled(uid, args.Actor, args.Enabled, true);
        UpdateDeepSpaceComUi(uid, component);
    }

    private void OnSelectDeepSpaceComChannel(EntityUid uid, DeepSpaceComComponent component, SelectDeepSpaceComChannelMessage args)
    {
        if (component.RequiresPower && !this.IsPowered(uid, EntityManager))
            return;

        if (!_protoMan.TryIndex<RadioChannelPrototype>(args.Channel, out _) || !component.SupportedChannels.Contains(args.Channel))
            return;

        if (TryComp<RadioMicrophoneComponent>(uid, out var mic))
            mic.BroadcastChannel = args.Channel;
        if (TryComp<RadioSpeakerComponent>(uid, out var speaker))
            speaker.Channels = new(){ args.Channel };
        UpdateDeepSpaceComUi(uid, component);
    }

    private void UpdateDeepSpaceComUi(EntityUid uid, DeepSpaceComComponent component)
    {
        var micComp = CompOrNull<RadioMicrophoneComponent>(uid);
        var speakerComp = CompOrNull<RadioSpeakerComponent>(uid);

        var micEnabled = micComp?.Enabled ?? false;
        var speakerEnabled = speakerComp?.Enabled ?? false;
        var availableChannels = component.SupportedChannels;
        var selectedChannel = micComp?.BroadcastChannel ?? SharedChatSystem.CommonChannel;
        var state = new DeepSpaceComBoundUIState(micEnabled, speakerEnabled, availableChannels, selectedChannel);
        _ui.SetUiState(uid, DeepSpaceComUiKey.Key, state);
    } // WD end
}
