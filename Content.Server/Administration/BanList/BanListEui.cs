﻿using System.Threading.Tasks;
using Content.Server.Administration.Managers;
using Content.Server.Database;
using Content.Server.EUI;
using Content.Shared.Administration;
using Content.Shared.Administration.BanList;
using Content.Shared.Eui;
using Robust.Shared.Network;

namespace Content.Server.Administration.BanList;

public sealed class BanListEui : BaseEui
{
    [Dependency] private readonly IAdminManager _admins = default!;
    [Dependency] private readonly IPlayerLocator _playerLocator = default!;
    [Dependency] private readonly IServerDbManager _db = default!;

    public BanListEui()
    {
        IoCManager.InjectDependencies(this);
    }

    private Guid BanListPlayer { get; set; }
    private string BanListPlayerName { get; set; } = string.Empty;
    private List<SharedServerBan> Bans { get; } = new();
    private List<SharedServerRoleBan> RoleBans { get; } = new();

    public override void Opened()
    {
        base.Opened();

        _admins.OnPermsChanged += OnPermsChanged;
    }

    public override void Closed()
    {
        base.Closed();

        _admins.OnPermsChanged -= OnPermsChanged;
    }

    public override EuiStateBase GetNewState()
    {
        return new BanListEuiState(BanListPlayerName, Bans, RoleBans);
    }

    private void OnPermsChanged(AdminPermsChangedEventArgs args)
    {
        if (args.Player == Player && !_admins.HasAdminFlag(Player, AdminFlags.Ban))
        {
            Close();
        }
    }

    private async Task LoadBans(NetUserId userId)
    {
        foreach (var ban in await _db.GetServerBansAsync(null, userId, null))
        {
            SharedServerUnban? unban = null;
            if (ban.Unban is { } unbanDef)
            {
                var unbanningAdmin = unbanDef.UnbanningAdmin == null
                    ? null
                    : (await _playerLocator.LookupIdAsync(unbanDef.UnbanningAdmin.Value))?.Username;
                unban = new SharedServerUnban(unbanningAdmin, ban.Unban.UnbanTime.UtcDateTime);
            }

            Bans.Add(new SharedServerBan(
                ban.Id,
                ban.UserId,
                ban.Address is { } address
                    ? (address.address.ToString(), address.cidrMask)
                    : null,
                ban.HWId == null ? null : Convert.ToBase64String(ban.HWId.Value.AsSpan()),
                ban.BanTime.UtcDateTime,
                ban.ExpirationTime?.UtcDateTime,
                ban.Reason,
                ban.BanningAdmin == null
                    ? null
                    : (await _playerLocator.LookupIdAsync(ban.BanningAdmin.Value))?.Username,
                unban,
                ban.ServerName
            ));
        }
    }

    private async Task LoadRoleBans(NetUserId userId)
    {
        foreach (var ban in await _db.GetServerRoleBansAsync(null, userId, null))
        {
            SharedServerUnban? unban = null;
            if (ban.Unban is { } unbanDef)
            {
                var unbanningAdmin = unbanDef.UnbanningAdmin == null
                    ? null
                    : (await _playerLocator.LookupIdAsync(unbanDef.UnbanningAdmin.Value))?.Username;
                unban = new SharedServerUnban(unbanningAdmin, ban.Unban.UnbanTime.UtcDateTime);
            }

            RoleBans.Add(new SharedServerRoleBan(
                ban.Id,
                ban.UserId,
                ban.Address is { } address
                    ? (address.address.ToString(), address.cidrMask)
                    : null,
                ban.HWId == null ? null : Convert.ToBase64String(ban.HWId.Value.AsSpan()),
                ban.BanTime.UtcDateTime,
                ban.ExpirationTime?.UtcDateTime,
                ban.Reason,
                ban.BanningAdmin == null
                    ? null
                    : (await _playerLocator.LookupIdAsync(ban.BanningAdmin.Value))?.Username,
                unban,
                ban.Role,
                ban.ServerName ?? "unknown"
            ));
        }
    }

    private async Task LoadFromDb()
    {
        Bans.Clear();
        RoleBans.Clear();

        var userId = new NetUserId(BanListPlayer);
        BanListPlayerName = (await _playerLocator.LookupIdAsync(userId))?.Username ??
                            string.Empty;

        await LoadBans(userId);
        await LoadRoleBans(userId);

        StateDirty();
    }

    public async Task ChangeBanListPlayer(Guid banListPlayer)
    {
        BanListPlayer = banListPlayer;
        await LoadFromDb();
    }
}
