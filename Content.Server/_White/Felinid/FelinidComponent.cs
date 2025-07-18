using Robust.Shared.Audio;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype;
using Robust.Shared.Prototypes;

namespace Content.Server.Abilities.Felinid
{
    [RegisterComponent]
    public sealed partial class FelinidComponent : Component
    {
        /// <summary>
        /// The hairball prototype to use.
        /// </summary>
        [DataField("hairballPrototype", customTypeSerializer: typeof(PrototypeIdSerializer<EntityPrototype>))]
        public string HairballPrototype = "Hairball";

        [DataField]
        public SoundSpecifier MouseEatingSound = new SoundCollectionSpecifier("eating");

        public EntityUid? HairballAction;

        public EntityUid? EatMouseAction;

        public EntityUid? PotentialTarget = null;
    }
}
