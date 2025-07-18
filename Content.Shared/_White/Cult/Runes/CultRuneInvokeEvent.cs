namespace Content.Shared._White.Cult.Runes;

public sealed class CultRuneInvokeEvent : EntityEventArgs
{
    public EntityUid Rune { get; set; }
    public EntityUid User { get; set; }
    public HashSet<EntityUid> Cultists { get; }
    public bool Result { get; set; }

    public string? InvokePhraseOverride { get; set; } = null;

    public CultRuneInvokeEvent(EntityUid rune, EntityUid user, HashSet<EntityUid> cultists)
    {
        Rune = rune;
        User = user;
        Cultists = cultists;
    }
}
