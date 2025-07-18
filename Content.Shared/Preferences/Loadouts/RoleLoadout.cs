using System.Diagnostics.CodeAnalysis;
using Content.Shared.Random;
using Robust.Shared.Collections;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Utility;

namespace Content.Shared.Preferences.Loadouts;

/// <summary>
/// Contains all of the selected data for a role's loadout.
/// </summary>
[Serializable, NetSerializable]
public sealed class RoleLoadout
{
    public readonly ProtoId<RoleLoadoutPrototype> Role;

    public Dictionary<ProtoId<LoadoutGroupPrototype>, List<Loadout>> SelectedLoadouts = new();

    /*
     * Loadout-specific data used for validation.
     */

    public int? Points;

    public RoleLoadout(ProtoId<RoleLoadoutPrototype> role)
    {
        Role = role;
    }

    public RoleLoadout Clone()
    {
        var weh = new RoleLoadout(Role);

        foreach (var selected in SelectedLoadouts)
        {
            weh.SelectedLoadouts.Add(selected.Key, new List<Loadout>(selected.Value));
        }

        return weh;
    }

    /// <summary>
    /// Ensures all prototypes exist and effects can be applied.
    /// </summary>
    public void EnsureValid(HumanoidCharacterProfile profile, ICommonSession session, IDependencyCollection collection)
    {
        var groupRemove = new ValueList<string>();
        var protoManager = collection.Resolve<IPrototypeManager>();

        if (!protoManager.TryIndex(Role, out var roleProto))
        {
            SelectedLoadouts.Clear();
            return;
        }

        // Reset points to recalculate.
        Points = roleProto.Points;

        foreach (var (group, groupLoadouts) in SelectedLoadouts)
        {
            // Dump if Group doesn't exist
            if (!protoManager.TryIndex(group, out var groupProto))
            {
                groupRemove.Add(group);
                continue;
            }

            var loadouts = groupLoadouts[..Math.Min(groupLoadouts.Count, groupProto.MaxLimit)];

            // Validate first
            for (var i = loadouts.Count - 1; i >= 0; i--)
            {
                var loadout = loadouts[i];

                if (!protoManager.TryIndex(loadout.Prototype, out var loadoutProto))
                {
                    loadouts.RemoveAt(i);
                    continue;
                }

                // Похуй FIXME
                //if (!IsValid(profile, session, loadout.Prototype, collection, out _))
                // {
                //     loadouts.RemoveAt(i);
                //     continue;
                // }

                Apply(loadoutProto);
            }

            // Apply defaults if required
            // Technically it's possible for someone to game themselves into loadouts they shouldn't have
            // If you put invalid ones first but that's your fault for not using sensible defaults
            if (loadouts.Count < groupProto.MinLimit)
            {
                for (var i = 0; i < Math.Min(groupProto.MinLimit, groupProto.Loadouts.Count); i++)
                {
                    if (!protoManager.TryIndex(groupProto.Loadouts[i], out var loadoutProto))
                        continue;

                    var defaultLoadout = new Loadout()
                    {
                        Prototype = loadoutProto.ID,
                    };

                    if (loadouts.Contains(defaultLoadout))
                        continue;

                    // Still need to apply the effects even if validation is ignored.
                    loadouts.Add(defaultLoadout);
                    Apply(loadoutProto);
                }
            }

            SelectedLoadouts[group] = loadouts;
        }

        foreach (var value in groupRemove)
        {
            SelectedLoadouts.Remove(value);
        }
    }

    private void Apply(ItemLoadoutPrototype loadoutProto)
    {
        foreach (var effect in loadoutProto.Effects)
        {
            effect.Apply(this);
        }
    }

    /// <summary>
    /// Resets the selected loadouts to default if no data is present.
    /// </summary>
    public void SetDefault(IPrototypeManager protoManager, bool force = false)
    {
        if (force)
            SelectedLoadouts.Clear();

        var roleProto = protoManager.Index(Role);

        for (var i = roleProto.Groups.Count - 1; i >= 0; i--)
        {
            var group = roleProto.Groups[i];

            if (!protoManager.TryIndex(group, out var groupProto))
                continue;

            if (SelectedLoadouts.ContainsKey(group))
                continue;

            SelectedLoadouts[group] = new List<Loadout>();

            if (groupProto.MinLimit > 0)
            {
                // Apply any loadouts we can.
                for (var j = 0; j < Math.Min(groupProto.MinLimit, groupProto.Loadouts.Count); j++)
                {
                    AddLoadout(group, groupProto.Loadouts[j], protoManager);
                }
            }
        }
    }

    /// <summary>
    /// Returns whether a loadout is valid or not.
    /// </summary>
    public bool IsValid(HumanoidCharacterProfile profile, ICommonSession session, ProtoId<ItemLoadoutPrototype> loadout, IDependencyCollection collection, [NotNullWhen(false)] out FormattedMessage? reason)
    {
        reason = null;

        var protoManager = collection.Resolve<IPrototypeManager>();

        if (!protoManager.TryIndex(loadout, out var loadoutProto))
        {
            // Uhh
            reason = FormattedMessage.FromMarkup("");
            return false;
        }

        if (!protoManager.TryIndex(Role, out var roleProto))
        {
            reason = FormattedMessage.FromUnformatted("loadouts-prototype-missing");
            return false;
        }

        var valid = true;

        foreach (var effect in loadoutProto.Effects)
        {
            valid = valid && effect.Validate(profile, this, session, collection, out reason);
        }

        return valid;
    }

    /// <summary>
    /// Applies the specified loadout to this group.
    /// </summary>
    public bool AddLoadout(ProtoId<LoadoutGroupPrototype> selectedGroup, ProtoId<ItemLoadoutPrototype> selectedLoadout, IPrototypeManager protoManager)
    {
        var groupLoadouts = SelectedLoadouts[selectedGroup];

        // Need to unselect existing ones if we're at or above limit
        var limit = Math.Max(0, groupLoadouts.Count + 1 - protoManager.Index(selectedGroup).MaxLimit);

        for (var i = 0; i < groupLoadouts.Count; i++)
        {
            var loadout = groupLoadouts[i];

            if (loadout.Prototype != selectedLoadout)
            {
                // Remove any other loadouts that might push it above the limit.
                if (limit > 0)
                {
                    limit--;
                    groupLoadouts.RemoveAt(i);
                    i--;
                }

                continue;
            }

            DebugTools.Assert(false);
            return false;
        }

        groupLoadouts.Add(new Loadout()
        {
            Prototype = selectedLoadout,
        });

        return true;
    }

    /// <summary>
    /// Removed the specified loadout from this group.
    /// </summary>
    public bool RemoveLoadout(ProtoId<LoadoutGroupPrototype> selectedGroup, ProtoId<ItemLoadoutPrototype> selectedLoadout, IPrototypeManager protoManager)
    {
        // Although this may bring us below minimum we'll let EnsureValid handle it.

        var groupLoadouts = SelectedLoadouts[selectedGroup];

        for (var i = 0; i < groupLoadouts.Count; i++)
        {
            var loadout = groupLoadouts[i];

            if (loadout.Prototype != selectedLoadout)
                continue;

            groupLoadouts.RemoveAt(i);
            return true;
        }

        return false;
    }
}
