using System.Linq;
using Content.Shared.Actions;
using Content.Shared.Implants.Components;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Mindshield.Components;
using Content.Shared.Mobs;
using Content.Shared.Store;
using Content.Shared.Tag;
using JetBrains.Annotations;
using Robust.Shared.Containers;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Shared.Implants;

public abstract class SharedSubdermalImplantSystem : EntitySystem
{
    [Dependency] private readonly INetManager _net = default!;
    [Dependency] private readonly SharedActionsSystem _actionsSystem = default!;
    [Dependency] private readonly SharedContainerSystem _container = default!;
    [Dependency] private readonly TagSystem _tag = default!;
    [Dependency] private readonly SharedUserInterfaceSystem _ui = default!; // WD

    public const string BaseStorageId = "storagebase";

    public override void Initialize()
    {
        SubscribeLocalEvent<SubdermalImplantComponent, EntGotInsertedIntoContainerMessage>(OnInsert);
        SubscribeLocalEvent<SubdermalImplantComponent, ContainerGettingRemovedAttemptEvent>(OnRemoveAttempt);
        SubscribeLocalEvent<SubdermalImplantComponent, EntGotRemovedFromContainerMessage>(OnRemove);

        SubscribeLocalEvent<ImplantedComponent, MobStateChangedEvent>(RelayToImplantEvent);
        SubscribeLocalEvent<ImplantedComponent, AfterInteractUsingEvent>(RelayToImplantEvent);
        SubscribeLocalEvent<ImplantedComponent, SuicideEvent>(RelayToImplantEvent);
    }

    private void OnInsert(EntityUid uid, SubdermalImplantComponent component, EntGotInsertedIntoContainerMessage args)
    {
        if (component.ImplantedEntity == null || _net.IsClient)
            return;

        if (!string.IsNullOrWhiteSpace(component.ImplantAction))
        {
            _actionsSystem.AddAction(component.ImplantedEntity.Value, ref component.Action, component.ImplantAction, uid);
        }

        //replace micro bomb with macro bomb
        if (_container.TryGetContainer(component.ImplantedEntity.Value, ImplanterComponent.ImplantSlotId, out var implantContainer) && _tag.HasTag(uid, "MacroBomb"))
        {
            foreach (var implant in implantContainer.ContainedEntities)
            {
                if (_tag.HasTag(implant, "MicroBomb"))
                {
                    _container.Remove(implant, implantContainer);
                    QueueDel(implant);
                }
            }
        }

        var ev = new ImplantImplantedEvent(uid, component.ImplantedEntity.Value);
        RaiseLocalEvent(uid, ref ev);
    }

    private void OnRemoveAttempt(EntityUid uid, SubdermalImplantComponent component, ContainerGettingRemovedAttemptEvent args)
    {
        if (component.Permanent && component.ImplantedEntity != null)
            args.Cancel();
    }

    private void OnRemove(EntityUid uid, SubdermalImplantComponent component, EntGotRemovedFromContainerMessage args)
    {
        if (component.ImplantedEntity == null || Terminating(component.ImplantedEntity.Value))
            return;

        if (component.ImplantAction != null)
            _actionsSystem.RemoveProvidedActions(component.ImplantedEntity.Value, uid);

        // WD EDIT START
        if (_tag.HasTag(uid, "MindShield") &&
            _container.TryGetContainer(component.ImplantedEntity.Value, ImplanterComponent.ImplantSlotId,
                out var implantContainer) &&
            implantContainer.ContainedEntities.All(x => !_tag.HasTag(x, "MindShield")))
        {
            RemCompDeferred<MindShieldComponent>(component.ImplantedEntity.Value);
        }
        // WD EDIT END

        if (!_container.TryGetContainer(uid, BaseStorageId, out var storageImplant))
            return;

        var entCoords = Transform(component.ImplantedEntity.Value).Coordinates;

        var containedEntites = storageImplant.ContainedEntities.ToArray();

        foreach (var entity in containedEntites)
        {
            if (Terminating(entity))
                continue;

            _container.RemoveEntity(storageImplant.Owner, entity, force: true, destination: entCoords);
        }
    }

    /// <summary>
    /// Add a list of implants to a person.
    /// Logs any implant ids that don't have <see cref="SubdermalImplantComponent"/>.
    /// </summary>
    public void AddImplants(EntityUid uid, IEnumerable<String> implants)
    {
        foreach (var id in implants)
        {
            AddImplant(uid, id);
        }
    }

    /// <summary>
    /// Adds a single implant to a person, and returns the implant.
    /// Logs any implant ids that don't have <see cref="SubdermalImplantComponent"/>.
    /// </summary>
    /// <returns>
    /// The implant, if it was successfully created. Otherwise, null.
    /// </returns>>
    public EntityUid? AddImplant(EntityUid uid, String implantId) // WD EDIT AHEAD OF WIZDEN UPSTREAM
    {
        var coords = Transform(uid).Coordinates;
        var ent = Spawn(implantId, coords);

        if (TryComp<SubdermalImplantComponent>(ent, out var implant))
        {
            ForceImplant(uid, ent, implant);
        }
        else
        {
            Log.Warning($"Found invalid starting implant '{implantId}' on {uid} {ToPrettyString(uid):implanted}");
            Del(ent);
            return null;
        }

        return ent;
    }

    /// <summary>
    /// Forces an implant into a person
    /// Good for on spawn related code or admin additions
    /// </summary>
    /// <param name="target">The entity to be implanted</param>
    /// <param name="implant"> The implant</param>
    /// <param name="component">The implant component</param>
    /// <param name="containerForce">Should we force inserting in container</param>
    public void ForceImplant(EntityUid target, EntityUid implant, SubdermalImplantComponent component, bool containerForce = false)
    {
        //If the target doesn't have the implanted component, add it.
        var implantedComp = EnsureComp<ImplantedComponent>(target);
        var implantContainer = implantedComp.ImplantContainer;

        component.ImplantedEntity = target;
        RaiseLocalEvent(implant, new SubdermalImplantInserted(target, target, component));
        _container.Insert(implant, implantContainer);
    }

    /// <summary>
    /// Force remove a singular implant
    /// </summary>
    /// <param name="target">the implanted entity</param>
    /// <param name="implant">the implant</param>
    [PublicAPI]
    public void ForceRemove(EntityUid target, EntityUid implant)
    {
        if (!TryComp<ImplantedComponent>(target, out var implanted))
            return;

        var implantContainer = implanted.ImplantContainer;

        _container.Remove(implant, implantContainer);
        QueueDel(implant);
    }

    /// <summary>
    /// Removes and deletes implants by force
    /// </summary>
    /// <param name="target">The entity to have implants removed</param>
    [PublicAPI]
    public void WipeImplants(EntityUid target)
    {
        if (!TryComp<ImplantedComponent>(target, out var implanted))
            return;

        var implantContainer = implanted.ImplantContainer;

        _container.CleanContainer(implantContainer);
    }

    //Relays from the implanted to the implant
    private void RelayToImplantEvent<T>(EntityUid uid, ImplantedComponent component, T args) where T : notnull
    {
        if (!_container.TryGetContainer(uid, ImplanterComponent.ImplantSlotId, out var implantContainer))
            return;

        var relayEv = new ImplantRelayEvent<T>(args);
        foreach (var implant in implantContainer.ContainedEntities)
        {
            if (args is HandledEntityEventArgs { Handled: true })
                return;

            RaiseLocalEvent(implant, relayEv);
        }
    }

    //Miracle edit

    /// <summary>
    /// Transfers all implants from one entity to another.
    /// </summary>
    /// <remarks>
    /// This method transfers all implants from a donor entity to a recipient entity.
    /// Implants are moved from the donor's implant container to the recipient's implant container.
    /// </remarks>
    /// <param name="donor">The entity from which implants will be transferred.</param>
    /// <param name="recipient">The entity to which implants will be transferred.</param>
    public void TransferImplants(EntityUid donor, EntityUid recipient)
    {
        // Check if the donor has an ImplantedComponent, indicating the presence of implants
        if (!TryComp<ImplantedComponent>(donor, out var donorImplanted))
            return;

        // Get the implant containers for both the donor and recipient entities
        var donorImplantContainer = donorImplanted.ImplantContainer;

        // Get all implants from the donor's implant container
        var donorImplants = donorImplantContainer.ContainedEntities.ToArray();

        // Transfer each implant from the donor to the recipient
        foreach (var donorImplant in donorImplants)
        {
            // Check for any conditions or filters before transferring (if needed)
            // For instance, verifying if the recipient can receive specific implants, etc.

            // Remove the implant from the donor's implant container
            _container.Remove(donorImplant, donorImplantContainer, force: true);

            if (!TryComp<SubdermalImplantComponent>(donorImplant, out var subdermal))
                return;

            // Insert the implant into the recipient's implant container
            ForceImplant(recipient, donorImplant, subdermal, true);

            if (TryComp(recipient, out ActorComponent? actor))
                _ui.CloseUi(donorImplant, StoreUiKey.Key, actor.PlayerSession);
        }
    }

    //Miracle edit end
}

public sealed class ImplantRelayEvent<T> where T : notnull
{
    public readonly T Event;

    public ImplantRelayEvent(T ev)
    {
        Event = ev;
    }
}

/// <summary>
/// Event that is raised whenever someone is implanted with any given implant.
/// Raised on the the implant entity.
/// </summary>
/// <remarks>
/// implant implant implant implant
/// </remarks>
[ByRefEvent]
public readonly struct ImplantImplantedEvent
{
    public readonly EntityUid Implant;
    public readonly EntityUid? Implanted;

    public ImplantImplantedEvent(EntityUid implant, EntityUid? implanted)
    {
        Implant = implant;
        Implanted = implanted;
    }
}
