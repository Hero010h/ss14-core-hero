using Content.Shared.Chemistry.Reagent;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Content.Shared.Inventory;
using Content.Shared.Localizations;
using Content.Shared.Tag;
using JetBrains.Annotations;
using Robust.Shared.Prototypes;
using System.Linq;
using System.Text.Json.Serialization;

namespace Content.Server.Chemistry.ReagentEffects
{
    /// <summary>
    /// Default metabolism for medicine reagents.
    /// </summary>
    [UsedImplicitly]
    public sealed partial class HealthChange : ReagentEffect
    {
        /// <summary>
        /// Damage to apply every metabolism cycle. Damage Ignores resistances.
        /// </summary>
        [DataField(required: true)]
        [JsonPropertyName("damage")]
        public DamageSpecifier Damage = default!;

        /// <summary>
        ///     Should this effect scale the damage by the amount of chemical in the solution?
        ///     Useful for touch reactions, like styptic powder or acid.
        /// </summary>
        [DataField]
        [JsonPropertyName("scaleByQuantity")]
        public bool ScaleByQuantity;

        [DataField]
        [JsonPropertyName("ignoreResistances")]
        public bool IgnoreResistances = true;

        /// <summary>
        ///     WD.
        ///     Whether this will work through hardsuits or not.
        /// </summary>
        [DataField]
        [JsonPropertyName("pierceHardsuit")]
        public bool PierceHardsuit = true;

        protected override string ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys)
        {
            var damages = new List<string>();
            var heals = false;
            var deals = false;

            var damageSpec = new DamageSpecifier(Damage);

            foreach (var group in prototype.EnumeratePrototypes<DamageGroupPrototype>())
            {
                if (!damageSpec.TryGetDamageInGroup(group, out var amount))
                    continue;

                var relevantTypes = damageSpec.DamageDict
                    .Where(x => x.Value != FixedPoint2.Zero && group.DamageTypes.Contains(x.Key)).ToList();

                if (relevantTypes.Count != group.DamageTypes.Count)
                    continue;

                var sum = FixedPoint2.Zero;
                foreach (var type in group.DamageTypes)
                {
                    sum += damageSpec.DamageDict.GetValueOrDefault(type);
                }

                // if the total sum of all the types equal the damage amount,
                // assume that they're evenly distributed.
                if (sum != amount)
                    continue;

                var sign = FixedPoint2.Sign(amount);

                if (sign < 0)
                    heals = true;
                if (sign > 0)
                    deals = true;

                damages.Add(
                    Loc.GetString("health-change-display",
                        ("kind", group.LocalizedName),
                        ("amount", MathF.Abs(amount.Float())),
                        ("deltasign", sign)
                    ));

                foreach (var type in group.DamageTypes)
                {
                    damageSpec.DamageDict.Remove(type);
                }
            }

            foreach (var (kind, amount) in damageSpec.DamageDict)
            {
                var sign = FixedPoint2.Sign(amount);

                if (sign < 0)
                    heals = true;
                if (sign > 0)
                    deals = true;

                damages.Add(
                    Loc.GetString("health-change-display",
                        ("kind", prototype.Index<DamageTypePrototype>(kind).LocalizedName),
                        ("amount", MathF.Abs(amount.Float())),
                        ("deltasign", sign)
                    ));
            }

            var healsordeals = heals ? (deals ? "both" : "heals") : (deals ? "deals" : "none");

            return Loc.GetString("reagent-effect-guidebook-health-change",
                ("chance", Probability),
                ("changes", ContentLocalizationManager.FormatList(damages)),
                ("healsordeals", healsordeals));
        }

        public override void Effect(ReagentEffectArgs args)
        {   // TODO Make something out of this acid system
            if (!PierceHardsuit &&
                args.EntityManager.System<InventorySystem>().TryGetSlotEntity(args.SolutionEntity, "outerClothing", out var suit) &&
                args.EntityManager.System<TagSystem>().HasTag(suit.Value, "Hardsuit"))
                return; // WD

            var scale = ScaleByQuantity ? args.Quantity : FixedPoint2.New(1);
            scale *= args.Scale;

            args.EntityManager.System<DamageableSystem>().TryChangeDamage(
                args.SolutionEntity,
                Damage * scale,
                IgnoreResistances,
                interruptsDoAfters: false);
        }
    }
}
