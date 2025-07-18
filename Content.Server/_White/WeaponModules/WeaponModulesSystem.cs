using System.Diagnostics.CodeAnalysis;
using Content.Shared._White.Telescope;
using Content.Shared._White.WeaponModules;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Content.Shared.Verbs;
using Content.Shared.Tag;
using Robust.Server.GameObjects;
using Robust.Shared.Containers;
using Robust.Shared.Utility;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;

namespace Content.Server._White.WeaponModules;

public sealed class WeaponModulesSystem : EntitySystem
{
    protected static readonly Dictionary<string, Enum> Slots = new()
    {
        { "handguard_module", ModuleVisualState.HandGuardModule }, { "barrel_module", ModuleVisualState.BarrelModule }, { "aim_module", ModuleVisualState.AimModule }, { "shutter_module", ModuleVisualState.ShutterModule }
    };

    [Dependency] private readonly PointLightSystem _lightSystem = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearanceSystem = default!;
    [Dependency] private readonly SharedGunSystem _gunSystem = default!;
    [Dependency] private readonly TagSystem _tagSystem = default!;
    [Dependency] private readonly SharedAudioSystem _audioSystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<LightModuleComponent, EntGotInsertedIntoContainerMessage>(LightModuleOnInsert);
        SubscribeLocalEvent<LightModuleComponent, EntGotRemovedFromContainerMessage>(LightModuleOnEject);

        SubscribeLocalEvent<LaserModuleComponent, EntGotInsertedIntoContainerMessage>(LaserModuleOnInsert);
        SubscribeLocalEvent<LaserModuleComponent, EntGotRemovedFromContainerMessage>(LaserModuleOnEject);

        SubscribeLocalEvent<FlameHiderModuleComponent, EntGotInsertedIntoContainerMessage>(FlameHiderModuleOnInsert);
        SubscribeLocalEvent<FlameHiderModuleComponent, EntGotRemovedFromContainerMessage>(FlameHiderModuleOnEject);

        SubscribeLocalEvent<SilencerModuleComponent, EntGotInsertedIntoContainerMessage>(SilencerModuleOnInsert);
        SubscribeLocalEvent<SilencerModuleComponent, EntGotRemovedFromContainerMessage>(SilencerModuleOnEject);

        SubscribeLocalEvent<AcceleratorModuleComponent, EntGotInsertedIntoContainerMessage>(AcceleratorModuleOnInsert);
        SubscribeLocalEvent<AcceleratorModuleComponent, EntGotRemovedFromContainerMessage>(AcceleratorModuleOnEject);

        SubscribeLocalEvent<AimModuleComponent, EntGotInsertedIntoContainerMessage>(EightAimModuleOnInsert);
        SubscribeLocalEvent<AimModuleComponent, EntGotRemovedFromContainerMessage>(EightAimModuleOnEject);

        SubscribeLocalEvent<ShutterModuleComponent, EntGotInsertedIntoContainerMessage>(ShutterModuleOnInsert);
        SubscribeLocalEvent<ShutterModuleComponent, EntGotRemovedFromContainerMessage>(ShutterModuleOnEject);

        SubscribeLocalEvent<WeaponModulesComponent, GetVerbsEvent<AlternativeVerb>>(AddSwitchLightVerd);
    }

    private bool TryInsertModule(EntityUid module, EntityUid weapon, BaseModuleComponent component,
        string containerId, [NotNullWhen(true)] out WeaponModulesComponent? weaponModulesComponent)
    {
        if (!TryComp(weapon, out weaponModulesComponent) || !TryComp<AppearanceComponent>(weapon, out var appearanceComponent) ||
            !Slots.ContainsKey(containerId))
        {
            weaponModulesComponent = null;
            appearanceComponent = null;
            return false;
        }

        if (!weaponModulesComponent.Modules.Contains(module))
            weaponModulesComponent.Modules.Add(module);

        if (!Slots.TryGetValue(containerId, out var value))
            return false;

        _appearanceSystem.SetData(weapon, value, component.AppearanceValue, appearanceComponent);

        return true;
    }

    private bool TryEjectModule(EntityUid module, EntityUid weapon, string containerId, [NotNullWhen(true)] out WeaponModulesComponent? weaponModulesComponent)
    {
        if (!TryComp(weapon, out weaponModulesComponent) || !TryComp<AppearanceComponent>(weapon, out var appearanceComponent) || !Slots.ContainsKey(containerId))
        {
            weaponModulesComponent = null;
            appearanceComponent = null;
            return false;
        }


        if (weaponModulesComponent.Modules.Contains(module))
            weaponModulesComponent.Modules.Remove(module);

        if (!Slots.TryGetValue(containerId, out var value))
            return false;

        _appearanceSystem.SetData(weapon, value, "none", appearanceComponent);

        return true;
    }

    private void AddSwitchLightVerd(EntityUid uid, WeaponModulesComponent component, GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanInteract || !args.CanAccess)
            return;

        if (!_tagSystem.HasTag(args.Target, "HasLightModule"))
            return;

        AlternativeVerb verb = new()
        {
            Act = () =>
            {
                SetLight(args.Target);
            },
            Text = Loc.GetString("toggle-flashlight-verb-get-data-text"),
            Icon = new SpriteSpecifier.Texture(new("/Textures/Interface/VerbIcons/light.svg.192dpi.png")),
            Priority = 0
        };
        args.Verbs.Add(verb);
    }

    private void SetLight(EntityUid weapon)
    {
        _lightSystem.TryGetLight(weapon, out var light);
        if (light == null)
            return;

        _lightSystem.SetEnabled(weapon, !light.Enabled, light);
        _audioSystem.PlayPredicted(new SoundPathSpecifier("/Audio/Items/flashlight_pda.ogg"), weapon, weapon);
    }

    #region InsertModules
    private void LightModuleOnInsert(EntityUid module, LightModuleComponent component, EntGotInsertedIntoContainerMessage args)
    {
        EntityUid weapon = args.Container.Owner;

        if (!TryInsertModule(module, weapon, component, args.Container.ID, out var weaponModulesComponent))
            return;

        TryComp<AppearanceComponent>(weapon, out var appearanceComponent);

        SharedPointLightComponent light = _lightSystem.EnsureLight(weapon);

        _appearanceSystem.SetData(weapon, Modules.Light, "none", appearanceComponent);

        _lightSystem.SetRadius(weapon, component.Radius, light);
        _lightSystem.SetEnabled(weapon, true, light);

        _tagSystem.AddTag(weapon, "HasLightModule");
    }

    private void LaserModuleOnInsert(EntityUid module, LaserModuleComponent component, EntGotInsertedIntoContainerMessage args)
    {
        EntityUid weapon = args.Container.Owner;

        if (!TryComp<GunComponent>(weapon, out var gunComp)) return;

        if (!TryInsertModule(module, weapon, component, args.Container.ID, out var weaponModulesComponent))
            return;

        component.OldProjectileSpeed = gunComp.ProjectileSpeed;

        _gunSystem.SetProjectileSpeed(weapon, component.OldProjectileSpeed + component.ProjectileSpeedAdd);
    }

    private void FlameHiderModuleOnInsert(EntityUid module, FlameHiderModuleComponent component, EntGotInsertedIntoContainerMessage args)
    {
        EntityUid weapon = args.Container.Owner;

        if (!TryInsertModule(module, weapon, component, args.Container.ID, out var weaponModulesComponent))
            return;

        weaponModulesComponent.WeaponFireEffect = true;
        Dirty(weapon, weaponModulesComponent);
    }

    private void SilencerModuleOnInsert(EntityUid module, SilencerModuleComponent component, EntGotInsertedIntoContainerMessage args)
    {
        EntityUid weapon = args.Container.Owner;

        if (!TryComp<GunComponent>(weapon, out var gunComp)) return;

        if (!TryInsertModule(module, weapon, component, args.Container.ID, out var weaponModulesComponent))
            return;

        component.OldSoundGunshot = gunComp.SoundGunshot;

        weaponModulesComponent.WeaponFireEffect = true;
        _gunSystem.SetSound(weapon, component.NewSoundGunshot);

        Dirty(weapon, weaponModulesComponent);
    }

    private void AcceleratorModuleOnInsert(EntityUid module, AcceleratorModuleComponent component, EntGotInsertedIntoContainerMessage args)
    {
        EntityUid weapon = args.Container.Owner;

        if (!TryComp<GunComponent>(weapon, out var gunComp)) return;

        if (!TryInsertModule(module, weapon, component, args.Container.ID, out var weaponModulesComponent))
            return;

        component.OldFireRate = gunComp.FireRate;

        _gunSystem.SetFireRate(weapon, component.OldFireRate + component.FireRateAdd);
    }

    private void EightAimModuleOnInsert(EntityUid module, AimModuleComponent component, EntGotInsertedIntoContainerMessage args)
    {
        EntityUid weapon = args.Container.Owner;

        if (!TryComp<GunComponent>(weapon, out var gunComp)) return;

        if (!TryInsertModule(module, weapon, component, args.Container.ID, out var weaponModulesComponent))
            return;

        EnsureComp<TelescopeComponent>(weapon).Divisor = component.Divisor;
    }

    private void ShutterModuleOnInsert(EntityUid module, ShutterModuleComponent component, EntGotInsertedIntoContainerMessage args)
    {
        EntityUid weapon = args.Container.Owner;

        if (!TryComp<GunComponent>(weapon, out var gunComp)) return;

        if (!TryInsertModule(module, weapon, component, args.Container.ID, out var weaponModulesComponent))
            return;

        if (!TryComp<BallisticAmmoProviderComponent>(weapon, out var ballisticAmmo))
            return;

        ballisticAmmo.Whitelist?.Tags?.Add(component.Tag);
        Dirty(weapon, ballisticAmmo);
    }
    #endregion

    #region EjectModules
    private void LightModuleOnEject(EntityUid module, LightModuleComponent component, EntGotRemovedFromContainerMessage args)
    {
        EntityUid weapon = args.Container.Owner;

        if (!TryEjectModule(module, weapon, args.Container.ID, out var weaponModulesComponent))
            return;

        if (!_lightSystem.TryGetLight(weapon, out var light))
            return;

        _lightSystem.SetRadius(weapon, 0F, light);
        _lightSystem.SetEnabled(weapon, false, light);

        _tagSystem.RemoveTag(weapon, "HasLightModule");
    }

    private void LaserModuleOnEject(EntityUid module, LaserModuleComponent component, EntGotRemovedFromContainerMessage args)
    {
        EntityUid weapon = args.Container.Owner;

        if (!TryEjectModule(module, weapon, args.Container.ID, out var weaponModulesComponent))
            return;

        _gunSystem.SetProjectileSpeed(weapon, component.OldProjectileSpeed);
    }

    private void FlameHiderModuleOnEject(EntityUid module, FlameHiderModuleComponent component, EntGotRemovedFromContainerMessage args)
    {
        EntityUid weapon = args.Container.Owner;

        if (!TryEjectModule(module, weapon, args.Container.ID, out var weaponModulesComponent))
            return;

        weaponModulesComponent.WeaponFireEffect = false;
        Dirty(weapon, weaponModulesComponent);
    }

    private void SilencerModuleOnEject(EntityUid module, SilencerModuleComponent component, EntGotRemovedFromContainerMessage args)
    {
        EntityUid weapon = args.Container.Owner;

        if (!TryEjectModule(module, weapon, args.Container.ID, out var weaponModulesComponent))
            return;

        weaponModulesComponent.WeaponFireEffect = false;
        _gunSystem.SetSound(weapon, component.OldSoundGunshot!);
        Dirty(weapon, weaponModulesComponent);
    }

    private void AcceleratorModuleOnEject(EntityUid module, AcceleratorModuleComponent component, EntGotRemovedFromContainerMessage args)
    {
        EntityUid weapon = args.Container.Owner;

        if (!TryEjectModule(module, weapon, args.Container.ID, out var weaponModulesComponent))
            return;

        _gunSystem.SetFireRate(weapon, component.OldFireRate);
    }

    private void EightAimModuleOnEject(EntityUid module, AimModuleComponent component, EntGotRemovedFromContainerMessage args)
    {
        EntityUid weapon = args.Container.Owner;

        if (!TryEjectModule(module, weapon, args.Container.ID, out var weaponModulesComponent))
            return;

        RemComp<TelescopeComponent>(weapon);
    }

    private void ShutterModuleOnEject(EntityUid module, ShutterModuleComponent component, EntGotRemovedFromContainerMessage args)
    {
        EntityUid weapon = args.Container.Owner;

        if (!TryEjectModule(module, weapon, args.Container.ID, out var weaponModulesComponent))
            return;

        if (!TryComp<BallisticAmmoProviderComponent>(weapon, out var ballisticAmmo))
            return;

        ballisticAmmo.Whitelist?.Tags?.Remove(component.Tag);
        Dirty(weapon, ballisticAmmo);
    }
    #endregion
}
