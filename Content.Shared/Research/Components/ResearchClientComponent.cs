﻿using Robust.Shared.Serialization;

namespace Content.Shared.Research.Components
{
    /// <summary>
    /// This is an entity that is able to connect to a <see cref="ResearchServerComponent"/>
    /// </summary>
    [RegisterComponent]
    public sealed partial class ResearchClientComponent : Component
    {
        public bool ConnectedToServer => Server != null;

        /// <summary>
        /// The server the client is connected to
        /// </summary>
        [ViewVariables(VVAccess.ReadOnly)]
        public EntityUid? Server { get; set; }
    }

    /// <summary>
    /// Raised on the client whenever its server is changed
    /// </summary>
    /// <param name="Server">Its new server</param>
    [ByRefEvent]
    public readonly record struct ResearchRegistrationChangedEvent(EntityUid? Server);

    /// <summary>
    ///     Sent to the server when the client deselects a research server.
    /// </summary>
    [Serializable, NetSerializable]
    public sealed class ResearchClientServerDeselectedMessage : BoundUserInterfaceMessage
    {
    }

    /// <summary>
    ///     Sent to the server when the client chooses a research server.
    /// </summary>
    [Serializable, NetSerializable]
    public sealed class ResearchClientServerSelectedMessage : BoundUserInterfaceMessage
    {
        public int ServerId;

        public ResearchClientServerSelectedMessage(int serverId)
        {
            ServerId = serverId;
        }
    }

    /// <summary>
    ///     Request that the server updates the client.
    /// </summary>
    [Serializable, NetSerializable]
    public sealed class ResearchClientSyncMessage : BoundUserInterfaceMessage
    {
    }

    [NetSerializable, Serializable]
    public enum ResearchClientUiKey
    {
        Key,
    }

    [Serializable, NetSerializable]
    public sealed class ResearchClientBoundInterfaceState : BoundUserInterfaceState
    {
        public List<string> ServerNames;
        public List<int> ServerIds;
        public int SelectedServerId;

        public ResearchClientBoundInterfaceState(List<ResearchServerComponent> servers, int selectedServerId = -1)
        {
            ServerNames = new List<string>(servers.Count);
            ServerIds = new List<int>(servers.Count);
            foreach (var server in servers)
            {
                ServerNames.Add(server.ServerName);
                ServerIds.Add(server.Id);
            }

            SelectedServerId = selectedServerId;
        }
    }
}