using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Content.Shared.Administration.Logs;
using Content.Shared.Alert;
using Content.Shared.Audio;
using Content.Shared.Damage.Components;
using Content.Shared.Database;
using Content.Shared.Gravity;
using Content.Shared.Damage;
using Content.Shared.Hands;
using Content.Shared.Inventory;
using Content.Shared.Inventory.Events;
using Content.Shared.Item.ItemToggle.Components;
using Content.Shared.Popups;
using Content.Shared.Projectiles;
using Content.Shared.Standing.Systems;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Network;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Shared.Weapons.Reflect;

/// <summary>
/// This handles reflecting projectiles and hitscan shots.
/// </summary>
public sealed class ReflectSystem : EntitySystem
{
    [Dependency] private readonly INetManager _netManager = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly ISharedAdminLogManager _adminLogger = default!;
    [Dependency] private readonly IGameTiming _gameTiming = default!;
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly InventorySystem _inventorySystem = default!;
    [Dependency] private readonly DamageableSystem _damageableSystem = default!;
    [Dependency] private readonly SharedGravitySystem _gravity = default!;
    [Dependency] private readonly SharedStandingStateSystem _standing = default!;
    [Dependency] private readonly AlertsSystem _alerts = default!;


    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ReflectComponent, ProjectileReflectAttemptEvent>(OnReflectCollide);
        SubscribeLocalEvent<ReflectComponent, HitScanReflectAttemptEvent>(OnReflectHitscan);
        SubscribeLocalEvent<ReflectComponent, GotEquippedEvent>(OnReflectEquipped);
        SubscribeLocalEvent<ReflectComponent, GotUnequippedEvent>(OnReflectUnequipped);
        SubscribeLocalEvent<ReflectComponent, GotEquippedHandEvent>(OnReflectHandEquipped);
        SubscribeLocalEvent<ReflectComponent, GotUnequippedHandEvent>(OnReflectHandUnequipped);
        SubscribeLocalEvent<ReflectComponent, ItemToggledEvent>(OnToggleReflect);

        SubscribeLocalEvent<ReflectUserComponent, ProjectileReflectAttemptEvent>(OnReflectUserCollide);
        SubscribeLocalEvent<ReflectUserComponent, HitScanReflectAttemptEvent>(OnReflectUserHitscan);
    }

    private void OnReflectUserHitscan(EntityUid uid, ReflectUserComponent component, ref HitScanReflectAttemptEvent args)
    {
        if (args.Reflected)
            return;

        foreach (var ent in _inventorySystem.GetHandOrInventoryEntities(uid, SlotFlags.WITHOUT_POCKET))
        {
            if (!TryReflectHitscan(uid, ent, args.Shooter, args.SourceItem, args.Direction, out var dir))
                continue;

            args.Direction = dir.Value;
            args.Reflected = true;
            break;
        }
    }

    private void OnReflectUserCollide(EntityUid uid, ReflectUserComponent component, ref ProjectileReflectAttemptEvent args)
    {
        foreach (var ent in _inventorySystem.GetHandOrInventoryEntities(uid, SlotFlags.WITHOUT_POCKET))
        {
            if (!TryReflectProjectile(uid, ent, args.ProjUid))
                continue;

            args.Cancelled = true;
            break;
        }
    }

    private void OnReflectCollide(EntityUid uid, ReflectComponent component, ref ProjectileReflectAttemptEvent args)
    {
        if (args.Cancelled)
            return;

        if (TryReflectProjectile(uid, uid, args.ProjUid, reflect: component))
            args.Cancelled = true;
    }

    private bool TryReflectProjectile(EntityUid user, EntityUid reflector, EntityUid projectile, ProjectileComponent? projectileComp = null, ReflectComponent? reflect = null)
    {
        // Do we have the components needed to try a reflect at all?
        if (
            !Resolve(reflector, ref reflect, false) ||
            !reflect.Enabled ||
            !reflect.InRightPlace || // WD
            !TryComp<ReflectiveComponent>(projectile, out var reflective) ||
            (reflect.Reflects & reflective.Reflective) == 0x0 ||
            !TryComp<PhysicsComponent>(projectile, out var physics) ||
            TryComp<StaminaComponent>(reflector, out var staminaComponent) && staminaComponent.Critical ||
            _standing.IsDown(reflector)
        )
            return false;

        if (!_random.Prob(CalcReflectChance(reflector, reflect)))
            return false;

        var rotation = _random.NextAngle(-reflect.Spread / 2, reflect.Spread / 2).Opposite();
        var existingVelocity = _physics.GetMapLinearVelocity(projectile, component: physics);
        var relativeVelocity = existingVelocity - _physics.GetMapLinearVelocity(user);
        var newVelocity = rotation.RotateVec(relativeVelocity);

        // Have the velocity in world terms above so need to convert it back to local.
        var difference = newVelocity - existingVelocity;

        _physics.SetLinearVelocity(projectile, physics.LinearVelocity + difference, body: physics);

        var locRot = Transform(projectile).LocalRotation;
        var newRot = rotation.RotateVec(locRot.ToVec());
        _transform.SetLocalRotation(projectile, newRot.ToAngle());

        if (_netManager.IsServer) // WD edit
        {
            _audio.PlayPvs(reflect.SoundOnReflect, user, AudioHelpers.WithVariation(0.05f, _random));
        }

        if (Resolve(projectile, ref projectileComp, false))
        {
            if (reflect.DamageOnReflect) // WD
            {
                _damageableSystem.TryChangeDamage(reflector, projectileComp.Damage, projectileComp.IgnoreResistances,
                    origin: projectileComp.Shooter);
            }

            _adminLogger.Add(LogType.BulletHit, LogImpact.Medium, $"{ToPrettyString(user)} reflected {ToPrettyString(projectile)} from {ToPrettyString(projectileComp.Weapon)} shot by {projectileComp.Shooter}");

            projectileComp.IgnoreTarget = true; // WD

            projectileComp.Shooter = user;
            projectileComp.Weapon = user;
            Dirty(projectile, projectileComp);
        }
        else
        {
            _adminLogger.Add(LogType.BulletHit, LogImpact.Medium, $"{ToPrettyString(user)} reflected {ToPrettyString(projectile)}");
        }

        return true;
    }

    private float CalcReflectChance(EntityUid reflector, ReflectComponent reflect)
    {
        /*
         *  The rules of deflection are as follows:
         *  If you innately reflect things via magic, biology etc., you always have a full chance.
         *  If you are standing up and standing still, you're prepared to deflect and have full chance.
         *  If you have velocity, your deflection chance depends on your velocity, clamped.
         *  If you are floating, your chance is the minimum value possible.
         *  You cannot deflect if you are knocked down or stunned.
         */

        if (reflect.Innate)
            return reflect.ReflectProb;

        if (_gravity.IsWeightless(reflector))
            return reflect.MinReflectProb;

        if (!TryComp<PhysicsComponent>(reflector, out var reflectorPhysics))
            return reflect.ReflectProb;

        return MathHelper.Lerp(
            reflect.MinReflectProb,
            reflect.ReflectProb,
            // Inverse progression between velocities fed in as progression between probabilities. We go high -> low so the output here needs to be _inverted_.
            1 - Math.Clamp((reflectorPhysics.LinearVelocity.Length() - reflect.VelocityBeforeNotMaxProb) / (reflect.VelocityBeforeMinProb - reflect.VelocityBeforeNotMaxProb), 0, 1)
        );
    }

    private void OnReflectHitscan(EntityUid uid, ReflectComponent component, ref HitScanReflectAttemptEvent args)
    {
        if (args.Reflected ||
            (component.Reflects & args.Reflective) == 0x0)
        {
            return;
        }

        if (TryReflectHitscan(uid, uid, args.Shooter, args.SourceItem, args.Direction, out var dir))
        {
            args.Direction = dir.Value;
            args.Reflected = true;
        }
    }

    private bool TryReflectHitscan(
        EntityUid user,
        EntityUid reflector,
        EntityUid? shooter,
        EntityUid shotSource,
        Vector2 direction,
        [NotNullWhen(true)] out Vector2? newDirection)
    {
        if (!TryComp<ReflectComponent>(reflector, out var reflect) ||
            !reflect.Enabled ||
            !reflect.InRightPlace || // WD
            TryComp<StaminaComponent>(reflector, out var staminaComponent) && staminaComponent.Critical ||
            _standing.IsDown(reflector))
        {
            newDirection = null;
            return false;
        }

        if (!_random.Prob(CalcReflectChance(reflector, reflect)))
        {
            newDirection = null;
            return false;
        }

        if (_netManager.IsServer) // WD edit
        {
            _audio.PlayPvs(reflect.SoundOnReflect, user, AudioHelpers.WithVariation(0.05f, _random));
        }

        var spread = _random.NextAngle(-reflect.Spread / 2, reflect.Spread / 2);
        newDirection = -spread.RotateVec(direction);

        if (shooter != null)
            _adminLogger.Add(LogType.HitScanHit, LogImpact.Medium, $"{ToPrettyString(user)} reflected hitscan from {ToPrettyString(shotSource)} shot by {ToPrettyString(shooter.Value)}");
        else
            _adminLogger.Add(LogType.HitScanHit, LogImpact.Medium, $"{ToPrettyString(user)} reflected hitscan from {ToPrettyString(shotSource)}");

        return true;
    }

    private void OnReflectEquipped(EntityUid uid, ReflectComponent component, GotEquippedEvent args)
    {
        if (_gameTiming.ApplyingState)
            return;

        EnsureComp<ReflectUserComponent>(args.Equipee);

        component.InRightPlace = IsInRightPlace(component, Placement.Body); // WD

        if (component.Enabled && component.InRightPlace) // WD added component.InRightPlace
            EnableAlert(args.Equipee);
    }

    private void OnReflectUnequipped(EntityUid uid, ReflectComponent comp, GotUnequippedEvent args)
    {
        RefreshReflectUser(args.Equipee);
    }

    private void OnReflectHandEquipped(EntityUid uid, ReflectComponent component, GotEquippedHandEvent args)
    {
        if (_gameTiming.ApplyingState)
            return;

        EnsureComp<ReflectUserComponent>(args.User);

        component.InRightPlace = IsInRightPlace(component, Placement.Hands); // WD

        if (component.Enabled && component.InRightPlace) // WD added component.InRightPlace
            EnableAlert(args.User);
    }

    private void OnReflectHandUnequipped(EntityUid uid, ReflectComponent component, GotUnequippedHandEvent args)
    {
        RefreshReflectUser(args.User);
    }

    private void OnToggleReflect(EntityUid uid, ReflectComponent comp, ref ItemToggledEvent args)
    {
        comp.Enabled = args.Activated;
        Dirty(uid, comp);

        // WD edit start
        // Reason for the edit: previously EnableAlert and DisableAlert were given an "EntityUid uid" which
        // belongs to an item, not to the item user. Now its logic corrected and moved to "RefreshReflectUser()".
        // if (comp.Enabled)
        //     EnableAlert(uid);
        // else
        //     DisableAlert(uid);
        if (args.User != null)
        {
            RefreshReflectUser((EntityUid) args.User);
        }
        // WD edit end
    }

    /// <summary>
    /// Refreshes whether someone has reflection potential so we can raise directed events on them.
    /// </summary>
    private void RefreshReflectUser(EntityUid user)
    {
        foreach (var ent in _inventorySystem.GetHandOrInventoryEntities(user, SlotFlags.WITHOUT_POCKET))
        {
            if (!HasComp<ReflectComponent>(ent))
                continue;

            EnsureComp<ReflectUserComponent>(user);

            // WD edit start
            // Reason for the edit: to ensure correct display of alert.
            if (!TryComp<ReflectComponent>(ent, out var component))
                continue;
            if (component.Enabled && component.InRightPlace)
                EnableAlert(user);
            else
            {
                DisableAlert(user);
                continue;
            }
            // WD edit end

            return;
        }

        RemCompDeferred<ReflectUserComponent>(user);
        DisableAlert(user);
    }

    private void EnableAlert(EntityUid alertee)
    {
        _alerts.ShowAlert(alertee, AlertType.Deflecting);
    }

    private void DisableAlert(EntityUid alertee)
    {
        _alerts.ClearAlert(alertee, AlertType.Deflecting);
    }

    /// <summary>
    /// Selfdescribing.
    /// </summary>
    private static bool IsInRightPlace(ReflectComponent component, Placement placement) // WD
    {
        if (component.Placement == (Placement.Hands | Placement.Body))
            return true;
        else
            return (component.Placement & placement) != 0x0;
    }
}
