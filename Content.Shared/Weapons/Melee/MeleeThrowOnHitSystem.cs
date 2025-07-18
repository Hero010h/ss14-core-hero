using System.Numerics;
using Content.Shared.Construction.Components;
using Content.Shared.Standing.Systems;
using Content.Shared.Stunnable;
using Content.Shared.Throwing;
using Content.Shared.Weapons.Melee.Components;
using Content.Shared.Weapons.Melee.Events;
using Content.Shared.Wieldable.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Events;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Timing;

namespace Content.Shared.Weapons.Melee;

/// <summary>
/// This handles <see cref="MeleeThrowOnHitComponent"/>
/// </summary>
public sealed class MeleeThrowOnHitSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly SharedStunSystem _stun = default!; // WD added
    [Dependency] private readonly ThrownItemSystem _thrownItem = default!; // WD added
    [Dependency] private readonly SharedStandingStateSystem _standingState = default!; // WD added

    /// <inheritdoc/>
    public override void Initialize()
    {
        SubscribeLocalEvent<MeleeThrowOnHitComponent, ThrowDoHitEvent>(OnDoHit); // WD

        SubscribeLocalEvent<MeleeThrowOnHitComponent, MeleeHitEvent>(OnMeleeHit);
        SubscribeLocalEvent<MeleeThrownComponent, ComponentStartup>(OnThrownStartup);
        SubscribeLocalEvent<MeleeThrownComponent, ComponentShutdown>(OnThrownShutdown);
        SubscribeLocalEvent<MeleeThrownComponent, StartCollideEvent>(OnStartCollide);
    }

    private void OnDoHit(Entity<MeleeThrowOnHitComponent> ent, ref ThrowDoHitEvent args) // WD
    {
        // WD edit start
        if (!ent.Comp.ThrowOnThrowHit)
            return;
        // WD edit end

        if (!CanThrowOnHit(ent, args.Target))
            return;

        if (!TryComp(ent, out PhysicsComponent? physics))
            return;

        var velocity = physics.LinearVelocity.Normalized() * ent.Comp.Speed;

        RemComp<MeleeThrownComponent>(args.Target);
        var thrownComp = new MeleeThrownComponent
        {
            Velocity = velocity,
            Lifetime = ent.Comp.Lifetime,
            MinLifetime = ent.Comp.MinLifetime
        };
        AddComp(args.Target, thrownComp);

        // WD added start
        if (ent.Comp.StunTime != 0f)
            _stun.TryParalyze(args.Target, TimeSpan.FromSeconds(ent.Comp.StunTime), true);

        else if (ent.Comp.FallAfterHit)
            _standingState.TryLieDown(ent, null, SharedStandingStateSystem.DropHeldItemsBehavior.DropIfStanding);
        // WD added end

        _thrownItem.LandComponent(ent, args.Component, physics, false);
        _physics.SetLinearVelocity(ent, Vector2.Zero);
    }

    private void OnMeleeHit(Entity<MeleeThrowOnHitComponent> ent, ref MeleeHitEvent args)
    {
        if (args.Handled) // WD
            return;

        if (!args.IsHit)
            return;

        // WD added start
        if (ent.Comp.RequireWield)
        {
            if (!TryComp<WieldableComponent>(args.Weapon, out var weapon))
                return;

            if (!weapon.Wielded)
                return;
        }
        // WD added end

        // WD edit start
        var stunTime = ent.Comp.StunTime;
        var speed = ent.Comp.Speed;
        var lifetime = ent.Comp.Lifetime;

        if (args.Direction != null) // Heavy attack
        {
            stunTime = 0f;
            speed *= 0.5f;
            lifetime *= 0.5f;
        }
        // WD edit end

        var mapPos = _transform.GetMapCoordinates(args.User).Position;
        foreach (var hit in args.HitEntities)
        {
            var hitPos = _transform.GetMapCoordinates(hit).Position;
            var angle = args.Direction ?? hitPos - mapPos;
            if (angle == Vector2.Zero)
                continue;

            if (!CanThrowOnHit(ent, hit))
                continue;

            if (ent.Comp.UnanchorOnHit && HasComp<AnchorableComponent>(hit))
            {
                _transform.Unanchor(hit, Transform(hit));
            }

            RemComp<MeleeThrownComponent>(hit);
            var ev = new MeleeThrowOnHitStartEvent(args.User, ent);
            RaiseLocalEvent(hit, ref ev);
            var thrownComp = new MeleeThrownComponent
            {
                Velocity = angle.Normalized() * speed, // WD edit
                Lifetime = lifetime, // WD edit
                MinLifetime = ent.Comp.MinLifetime
            };
            AddComp(hit, thrownComp);

            // WD added end
            if (stunTime != 0f)
                _stun.TryParalyze(hit, TimeSpan.FromSeconds(stunTime), true);

            else if (ent.Comp.FallAfterHit)
                _standingState.TryLieDown(hit);
            // WD added start
        }
    }

    private void OnThrownStartup(Entity<MeleeThrownComponent> ent, ref ComponentStartup args)
    {
        var (_, comp) = ent;

        if (!TryComp<PhysicsComponent>(ent, out var body) ||
            (body.BodyType & (BodyType.Dynamic | BodyType.KinematicController)) == 0x0)
            return;

        comp.PreviousStatus = body.BodyStatus;
        comp.ThrownEndTime = _timing.CurTime + TimeSpan.FromSeconds(comp.Lifetime);
        comp.MinLifetimeTime = _timing.CurTime + TimeSpan.FromSeconds(comp.MinLifetime);

        _physics.SetBodyStatus(ent, body, BodyStatus.InAir);
        _physics.SetLinearVelocity(ent, Vector2.Zero, body: body);
        _physics.ApplyLinearImpulse(ent, comp.Velocity * body.Mass, body: body);

        Dirty(ent, ent.Comp);
    }

    private void OnThrownShutdown(Entity<MeleeThrownComponent> ent, ref ComponentShutdown args)
    {
        if (TryComp<PhysicsComponent>(ent, out var body))
            _physics.SetBodyStatus(ent, body, ent.Comp.PreviousStatus);
        var ev = new MeleeThrowOnHitEndEvent();
        RaiseLocalEvent(ent, ref ev);
    }

    private void OnStartCollide(Entity<MeleeThrownComponent> ent, ref StartCollideEvent args)
    {
        var (_, comp) = ent;
        if (!args.OtherFixture.Hard || !args.OtherBody.CanCollide || !args.OurFixture.Hard || !args.OurBody.CanCollide)
            return;

        if (_timing.CurTime < comp.MinLifetimeTime)
            return;

        RemCompDeferred(ent, ent.Comp);
    }

    public bool CanThrowOnHit(Entity<MeleeThrowOnHitComponent> ent, EntityUid target)
    {
        var (uid, comp) = ent;

        var ev = new AttemptMeleeThrowOnHitEvent(target);
        RaiseLocalEvent(uid, ref ev);

        if (ev.Handled)
            return !ev.Cancelled;

        return comp.Enabled;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<MeleeThrownComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (_timing.CurTime > comp.ThrownEndTime)
                RemCompDeferred(uid, comp);
        }
    }
}
