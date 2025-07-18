using Content.Server.Atmos;
using Content.Shared.Atmos;

namespace Content.Server._White.Flamethrower;

[RegisterComponent]
public sealed partial class GasProjectileComponent : Component
{
    public GasMixture? GasMixture;

    public TileInfo? LastTile;

    public TileInfo? CurTile;

    [DataField("gasUsagePerTile", required: true)]
    [ViewVariables(VVAccess.ReadWrite)]
    public float GasUsagePerTile;
}

public record struct TileInfo(EntityUid? GridUid, EntityUid? MapUid, Vector2i Tile);
