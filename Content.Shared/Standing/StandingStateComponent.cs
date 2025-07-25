using Content.Shared.Standing.Systems;
using Content.Shared._White.Standing;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared.Standing
{
    [RegisterComponent, NetworkedComponent, AutoGenerateComponentState, Access(typeof(SharedStandingStateSystem), typeof(StandingStateSystem))]
    public sealed partial class StandingStateComponent : Component
    {
        [ViewVariables(VVAccess.ReadWrite), DataField]
        public SoundSpecifier DownSound { get; private set; } = new SoundCollectionSpecifier("BodyFall");

        [DataField, AutoNetworkedField]
        public StandingState CurrentState { get; set; } = StandingState.Standing; // WD EDIT

        /// <summary>
        ///     Time required to get up.
        /// </summary>
        [DataField, AutoNetworkedField]
        public TimeSpan StandingUpTime { get; set; } = TimeSpan.FromSeconds(1); // WD EDIT

        // WD EDIT
        [DataField, AutoNetworkedField, ViewVariables(VVAccess.ReadWrite)]
        public bool CanLieDown;

        // WD EDIT
        [DataField, AutoNetworkedField, ViewVariables(VVAccess.ReadWrite)]
        public bool AutoGetUp = true;

        /// <summary>
        ///     List of fixtures that had their collision mask changed when the entity was downed.
        ///     Required for re-adding the collision mask.
        /// </summary>
        [DataField, AutoNetworkedField]
        public List<string> ChangedFixtures = new();

    }
}

[Serializable, NetSerializable]
public sealed class ChangeStandingStateEvent : CancellableEntityEventArgs
{
}

// WD EDIT
[Serializable, NetSerializable]
public sealed class CheckAutoGetUpEvent : CancellableEntityEventArgs
{
}

// WD EDIT
public enum StandingState
{
    Lying,
    GettingUp,
    Standing
}
