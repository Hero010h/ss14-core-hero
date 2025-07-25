using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype;

namespace Content.Shared._White.GhostRecruitment;

[RegisterComponent]
public sealed partial class GhostRecruitmentSpawnPointComponent : Component
{
    [DataField("prototype", customTypeSerializer: typeof(PrototypeIdSerializer<EntityPrototype>), required: true)]
    public string EntityPrototype = default!;

    [DataField("recruitmentName")]
    public string RecruitmentName = "default";

    [DataField("priority")] public int Priority = 5;

    [DataField]
    public string JobId = "Passenger";
}
