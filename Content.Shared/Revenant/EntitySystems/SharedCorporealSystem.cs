using Content.Shared.Physics;
using Robust.Shared.Physics;
using System.Linq;
using Content.Shared.Ghost;
using Content.Shared.Movement.Systems;
using Content.Shared.Revenant.Components;
using Robust.Shared.Physics.Systems;

namespace Content.Shared.Revenant.EntitySystems;

/// <summary>
/// Makes the revenant solid when the component is applied.
/// Additionally applies a few visual effects.
/// Used for status effect.
/// </summary>
public abstract class SharedCorporealSystem : EntitySystem
{
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly MovementSpeedModifierSystem _movement = default!;
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly SharedGhostSystem _ghost = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<CorporealComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<CorporealComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<CorporealComponent, RefreshMovementSpeedModifiersEvent>(OnRefresh);
    }

    private void OnRefresh(EntityUid uid, CorporealComponent component, RefreshMovementSpeedModifiersEvent args)
    {
        args.ModifySpeed(component.MovementSpeedDebuff, component.MovementSpeedDebuff);
    }

    public virtual void OnStartup(EntityUid uid, CorporealComponent component, ComponentStartup args)
    {
        _appearance.SetData(uid, RevenantVisuals.Corporeal, true);

        if (TryComp<FixturesComponent>(uid, out var fixtures) && fixtures.FixtureCount >= 1)
        {
            var fixture = fixtures.Fixtures.First();

            _physics.SetCollisionMask(uid, fixture.Key, fixture.Value, (int) (CollisionGroup.SmallMobMask | CollisionGroup.GhostImpassable), fixtures);
            _physics.SetCollisionLayer(uid, fixture.Key, fixture.Value, (int) CollisionGroup.SmallMobLayer, fixtures);
        }
        _movement.RefreshMovementSpeedModifiers(uid);

        if (!TryComp(uid, out GhostComponent? ghost) || ghost.Visible)
            return;

        component.MadeVisible = true;
        _ghost.SetVisible((uid, ghost), true);
    }

    public virtual void OnShutdown(EntityUid uid, CorporealComponent component, ComponentShutdown args)
    {
        _appearance.SetData(uid, RevenantVisuals.Corporeal, false);

        if (TryComp<FixturesComponent>(uid, out var fixtures) && fixtures.FixtureCount >= 1)
        {
            var fixture = fixtures.Fixtures.First();

            _physics.SetCollisionMask(uid, fixture.Key, fixture.Value, (int) CollisionGroup.GhostImpassable, fixtures);
            _physics.SetCollisionLayer(uid, fixture.Key, fixture.Value, 0, fixtures);
        }
        component.MovementSpeedDebuff = 1; //just so we can avoid annoying code elsewhere
        _movement.RefreshMovementSpeedModifiers(uid);

        if (component.MadeVisible && TryComp(uid, out GhostComponent? ghost))
            _ghost.SetVisible((uid, ghost), false);
    }
}
