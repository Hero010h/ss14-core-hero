using Content.Server.Speech.EntitySystems;
using Content.Server._White.AspectsSystem.Aspects;
using Content.Shared.Chat.Prototypes;
using Content.Shared.Humanoid;
using Robust.Shared.Audio;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype.Dictionary;

namespace Content.Server.Speech.Components;

/// <summary>
///     Component required for entities to be able to do vocal emotions.
/// </summary>
[RegisterComponent]
[Access(typeof(VocalSystem), typeof(CatEarsAspect))]
public sealed partial class VocalComponent : Component
{
    /// <summary>
    ///     Emote sounds prototype id for each sex (not gender).
    ///     Entities without <see cref="HumanoidComponent"/> considered to be <see cref="Sex.Unsexed"/>.
    /// </summary>
    [DataField("sounds", customTypeSerializer: typeof(PrototypeIdValueDictionarySerializer<Sex, EmoteSoundsPrototype>))]
    public Dictionary<Sex, string>? Sounds;

    [DataField("screamId", customTypeSerializer: typeof(PrototypeIdSerializer<EmotePrototype>))]
    public string ScreamId = "Scream";

    [DataField("wilhelm")]
    public SoundSpecifier Wilhelm = new SoundPathSpecifier("/Audio/Voice/Human/wilhelm_scream.ogg");

    [DataField("wilhelmProbability")]
    public float WilhelmProbability = 0.0002f;

    /// <summary>
    ///     Currently loaded emote sounds prototype, based on entity sex.
    ///     Null if no valid prototype for entity sex was found.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    public EmoteSoundsPrototype? EmoteSounds = null;
}
