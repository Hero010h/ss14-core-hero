using System.Numerics;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;

namespace Content.Shared.Projectiles;

/// <summary>
/// Embeds this entity inside of the hit target.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class EmbeddableProjectileComponent : Component
{
    /// <summary>
    /// Minimum speed of the projectile to embed.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite), DataField, AutoNetworkedField]
    public float MinimumSpeed = 5f;

    /// <summary>
    /// Delete the entity on embedded removal?
    /// Does nothing if there's no RemovalTime.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite), DataField, AutoNetworkedField]
    public bool DeleteOnRemove;

    /// <summary>
    /// How long it takes to remove the embedded object.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite), DataField, AutoNetworkedField]
    public float? RemovalTime = 1f;

    /// <summary>
    ///     Whether this entity will embed when thrown, or only when shot as a projectile.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite), DataField, AutoNetworkedField]
    public bool EmbedOnThrow = true;

    /// <summary>
    /// How far into the entity should we offset (0 is wherever we collided).
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite), DataField, AutoNetworkedField]
    public Vector2 Offset = Vector2.Zero;

    /// <summary>
    /// Sound to play after embedding into a hit target.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite), DataField, AutoNetworkedField]
    public SoundSpecifier? Sound;

    // WD START
    [ViewVariables, AutoNetworkedField]
    public bool PreventEmbedding;

    [ViewVariables, AutoNetworkedField]
    public bool Penetrate;

    [ViewVariables, AutoNetworkedField]
    public EntityUid? PenetratedUid;
    // WD END
}
