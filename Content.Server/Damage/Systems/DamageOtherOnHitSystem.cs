using Content.Server.Administration.Logs;
using Content.Server.Damage.Components;
using Content.Server.Weapons.Ranged.Systems;
using Content.Shared.Camera;
using Content.Server._White.Crossbow;
using Content.Shared._White.Cult.Systems;
using Content.Shared.Damage;
using Content.Shared.Damage.Events;
using Content.Shared.Damage.Systems;
using Content.Shared.Database;
using Content.Shared.Effects;
using Content.Shared.Mobs.Components;
using Content.Shared.Projectiles;
using Content.Shared.Throwing;
using Content.Shared.Weapons.Melee;
using Robust.Shared.Physics.Components;
using Robust.Shared.Player;

namespace Content.Server.Damage.Systems
{
    public sealed class DamageOtherOnHitSystem : EntitySystem
    {
        [Dependency] private readonly IAdminLogManager _adminLogger = default!;
        [Dependency] private readonly GunSystem _guns = default!;
        [Dependency] private readonly DamageableSystem _damageable = default!;
        [Dependency] private readonly DamageExamineSystem _damageExamine = default!;
        [Dependency] private readonly SharedCameraRecoilSystem _sharedCameraRecoil = default!;
        [Dependency] private readonly SharedColorFlashEffectSystem _color = default!;
        [Dependency] private readonly ThrownItemSystem _thrownItem = default!;

        public override void Initialize()
        {
            SubscribeLocalEvent<DamageOtherOnHitComponent, ThrowDoHitEvent>(OnDoHit,
                before: new[] {typeof(MeleeThrowOnHitSystem)},
                after: new[] {typeof(BloodSpearSystem)}); // WD EDIT
            SubscribeLocalEvent<DamageOtherOnHitComponent, DamageExamineEvent>(OnDamageExamine);
        }

        private void OnDoHit(EntityUid uid, DamageOtherOnHitComponent component, ThrowDoHitEvent args)
        {
            // WD EDIT START
            if (args.Handled)
                return;

            var damage = component.Damage;

            if (TryComp(uid, out ThrowDamageModifierComponent? modifier))
                damage += modifier.Damage;

            var dmg = _damageable.TryChangeDamage(args.Target, damage, component.IgnoreResistances, origin: args.Component.Thrower);
            // WD EDIT END

            // Log damage only for mobs. Useful for when people throw spears at each other, but also avoids log-spam when explosions send glass shards flying.
            if (dmg != null && HasComp<MobStateComponent>(args.Target))
                _adminLogger.Add(LogType.ThrowHit, $"{ToPrettyString(args.Target):target} received {dmg.GetTotal():damage} damage from collision");

            if (dmg is { Empty: false })
            {
                _color.RaiseEffect(Color.Red, new List<EntityUid>() { args.Target }, Filter.Pvs(args.Target, entityManager: EntityManager));
            }

            _guns.PlayImpactSound(args.Target, dmg, component.Sound, component.Sound != null); // WD EDIT
            /* if (TryComp<PhysicsComponent>(uid, out var body) && body.LinearVelocity.LengthSquared() > 0f)
            {
                var direction = body.LinearVelocity.Normalized();
                _sharedCameraRecoil.KickCamera(args.Target, direction);
            }*/
            // WD EDIT

            // TODO: If more stuff touches this then handle it after.
            if (!HasComp<EmbeddableProjectileComponent>(uid) && TryComp<PhysicsComponent>(uid, out var physics)) // WD EDIT
            {
                _thrownItem.LandComponent(args.Thrown, args.Component, physics, false);
            }
        }

        private void OnDamageExamine(EntityUid uid, DamageOtherOnHitComponent component, ref DamageExamineEvent args)
        {
            _damageExamine.AddDamageExamine(args.Message, component.Damage, Loc.GetString("damage-throw"));
        }
    }
}
