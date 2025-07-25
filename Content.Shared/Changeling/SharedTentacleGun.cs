﻿using System.Numerics;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Humanoid;
using Content.Shared.Mobs.Components;
using Content.Shared.Physics;
using Content.Shared.Projectiles;
using Content.Shared.Standing;
using Content.Shared.Standing.Systems;
using Content.Shared.Stunnable;
using Content.Shared.Throwing;
using Content.Shared.Weapons.Misc;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Input;
using Robust.Shared.Network;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Events;
using Robust.Shared.Random;
using Robust.Shared.Serialization;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Shared.Changeling;

public abstract class SharedTentacleGun : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly INetManager _netManager = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly ThrowingSystem _throwingSystem = default!;
    [Dependency] private readonly ITimerManager _timerManager = default!;
    [Dependency] private readonly SharedStandingStateSystem _standing = default!;
    [Dependency] private readonly SharedHandsSystem _handsSystem = default!;
    [Dependency] private readonly SharedStunSystem _stun = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<TentacleGunComponent, GunShotEvent>(OnTentacleShot);
        SubscribeLocalEvent<TentacleProjectileComponent, MapInitEvent>(OnMapInit); // WD
        SubscribeLocalEvent<TentacleProjectileComponent, ProjectileEmbedEvent>(OnTentacleCollide);
        SubscribeLocalEvent<TentacleProjectileComponent, PreventCollideEvent>(PreventCollision);
    }

    private void PreventCollision(Entity<TentacleProjectileComponent> ent, ref PreventCollideEvent args)
    {
        if (HasComp<MobStateComponent>(args.OtherEntity) && !HasComp<HumanoidAppearanceComponent>(args.OtherEntity))
        {
            args.Cancelled = true;
            return;
        }

        if (!TryComp(ent, out ProjectileComponent? projectile))
            return;

        if (args.OtherEntity == projectile.Shooter || args.OtherEntity == projectile.Weapon)
            args.Cancelled = true;
    }

    // WD EDIT START
    private void OnMapInit(Entity<TentacleProjectileComponent> ent, ref MapInitEvent args)
    {
        if (ent.Comp.DespawnTime <= 0)
        {
            return;
        }

        _timerManager.AddTimer(new Timer(ent.Comp.DespawnTime, false, () =>
        {
            if (!ent.Comp.GrabbedUid.HasValue)
            {
                Del(ent.Owner);
            }
        }));
    }
    // WD EDIT END

    private void OnTentacleShot(EntityUid uid, TentacleGunComponent component, ref GunShotEvent args)
    {
        foreach (var (shotUid, _) in args.Ammo)
        {
            if (!HasComp<TentacleProjectileComponent>(shotUid))
                continue;

            Dirty(uid, component);
            var visuals = EnsureComp<JointVisualsComponent>(shotUid.Value);
            visuals.Sprite =
                new SpriteSpecifier.Rsi(new ResPath("Objects/Weapons/Guns/Launchers/tentacle_gun.rsi"), "frope");
            visuals.OffsetA = new Vector2(0f, 0.5f);
            visuals.Target = uid;
            Dirty(shotUid.Value, visuals);
        }

        TryComp<AppearanceComponent>(uid, out var appearance);
        _appearance.SetData(uid, SharedTetherGunSystem.TetherVisualsStatus.Key, false, appearance);
    }

    private void OnTentacleCollide(EntityUid uid, TentacleProjectileComponent component, ref ProjectileEmbedEvent args)
    {
        if (!_timing.IsFirstTimePredicted)
            return;

        if (!HasComp<TentacleGunComponent>(args.Weapon))
        {
            QueueDel(uid);
            return;
        }

        if (!TryComp<GunComponent>(args.Weapon, out var gun))
        {
            QueueDel(uid);
            return;
        }

        if (!TryComp<StandingStateComponent>(args.Embedded, out var standing) || !standing.CanLieDown)
        {
            DeleteProjectile(uid);
            return;
        }

        switch (gun.SelectedMode)
        {
            case SelectiveFire.PullMob when !PullMob(args):
                DeleteProjectile(uid);
                return;
            case SelectiveFire.PullMob:
                _timerManager.AddTimer(new Timer(1500, false, () =>
                {
                    DeleteProjectile(uid);
                }));
                component.GrabbedUid = args.Embedded;
                break;
            case SelectiveFire.PullItem:
                PullItem(args);
                DeleteProjectile(uid);
                break;
        }
    }

    private void PullItem(ProjectileEmbedEvent args)
    {
        foreach (var activeItem in _handsSystem.EnumerateHeld(args.Embedded))
        {
            if(!TryComp<PhysicsComponent>(activeItem, out var physicsComponent))
                continue;

            if (!_handsSystem.TryDrop(args.Embedded, activeItem))
                continue;

            var force = physicsComponent.Mass * 2.5f / 2;

            _throwingSystem.TryThrow(activeItem, Transform(args.Shooter!.Value).Coordinates, force);
            break;
        }
    }

    private bool PullMob(ProjectileEmbedEvent args)
    {
        _standing.TryLieDown(args.Embedded);
        _stun.TryKnockdown(args.Embedded, TimeSpan.FromSeconds(4), true);

        _throwingSystem.TryThrow(args.Embedded, Transform(args.Shooter!.Value).Coordinates, 5f);

        return true;
    }

    private void DeleteProjectile(EntityUid projUid)
    {
        TryComp<AppearanceComponent>(projUid, out var appearance);

        if (!Deleted(projUid))
        {
            if (_netManager.IsServer)
            {
                QueueDel(projUid);
            }
        }

        _appearance.SetData(projUid, SharedTetherGunSystem.TetherVisualsStatus.Key, true, appearance);
    }

    [Serializable, NetSerializable]
    protected sealed class RequestTentacleMessage : EntityEventArgs
    {
        public BoundKeyFunction Key;

        public RequestTentacleMessage(BoundKeyFunction key)
        {
            Key = key;
        }
    }
}
