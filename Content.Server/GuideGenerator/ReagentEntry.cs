using System.Linq;
using System.Text.Json.Serialization;
using Content.Server._White.GuideGenerator;
using Content.Shared.Chemistry.Reaction;
using Content.Shared.Chemistry.Reagent;

namespace Content.Server.GuideGenerator;

public sealed class ReagentEntry
{
    [JsonPropertyName("id")]
    public string Id { get; }

    [JsonPropertyName("name")]
    public string Name { get; }

    [JsonPropertyName("group")]
    public string Group { get; }

    [JsonPropertyName("desc")]
    public string Description { get; }

    [JsonPropertyName("physicalDesc")]
    public string PhysicalDescription { get; }

    [JsonPropertyName("color")]
    public string SubstanceColor { get; }

    [JsonPropertyName("recipes")]
    public List<string> Recipes { get; } = new();

    [JsonPropertyName("textColor")]
    public string TextColor { get; }

    [JsonPropertyName("metabolisms")]
    public Dictionary<string, _White.GuideGenerator.ReagentEffectsEntry>? Metabolisms { get; }

    public ReagentEntry(ReagentPrototype proto)
    {
        Id = proto.ID;
        Name = proto.LocalizedName;
        Group = proto.Group;
        Description = proto.LocalizedDescription;
        PhysicalDescription = proto.LocalizedPhysicalDescription;
        SubstanceColor = proto.SubstanceColor.ToHex();
        var r = proto.SubstanceColor.R;
        var g = proto.SubstanceColor.G;
        var b = proto.SubstanceColor.B;
        TextColor = (0.2126f * r + 0.7152f * g + 0.0722f * b > 0.5
            ? Color.Black
            : Color.White).ToHex();
        Metabolisms = proto.Metabolisms?.ToDictionary(x => x.Key.Id, x => new _White.GuideGenerator.ReagentEffectsEntry(x.Value));
    }
}

public sealed class ReactionEntry
{
    [JsonPropertyName("id")]
    public string Id { get; }

    [JsonPropertyName("name")]
    public string Name { get; }

    [JsonPropertyName("reactants")]
    public Dictionary<string, ReactantEntry> Reactants { get; }

    [JsonPropertyName("products")]
    public Dictionary<string, float> Products { get; }

    [JsonPropertyName("mixingCategories")]
    public List<MixingCategoryEntry> MixingCategories { get; } = new();
    [JsonPropertyName("minTemp")]
    public float MinTemp { get; }
    [JsonPropertyName("maxTemp")]
    public float MaxTemp { get; }
    [JsonPropertyName("hasMax")]
    public bool HasMax { get; }

    [JsonPropertyName("effects")]
    public List<ReagentEffectEntry> ExportEffects { get; } = new();
    [JsonIgnore]
    public List<ReagentEffect> Effects { get; }

    public ReactionEntry(ReactionPrototype proto)
    {
        Id = proto.ID;
        Name = proto.Name;
        Reactants =
            proto.Reactants
                .Select(x => KeyValuePair.Create(x.Key, new ReactantEntry(x.Value.Amount.Float(), x.Value.Catalyst)))
                .ToDictionary(x => x.Key, x => x.Value);
        Products =
            proto.Products
                .Select(x => KeyValuePair.Create(x.Key, x.Value.Float()))
                .ToDictionary(x => x.Key, x => x.Value);
        Effects = proto.Effects;

        ExportEffects = proto.Effects.Select(x => new ReagentEffectEntry(x)).ToList();
        MinTemp = proto.MinimumTemperature;
        MaxTemp = proto.MaximumTemperature;
        HasMax = !float.IsPositiveInfinity(MaxTemp);
    }
}

public sealed class ReactantEntry
{
    [JsonPropertyName("amount")]
    public float Amount { get; }

    [JsonPropertyName("catalyst")]
    public bool Catalyst { get; }

    public ReactantEntry(float amnt, bool cata)
    {
        Amount = amnt;
        Catalyst = cata;
    }
}
