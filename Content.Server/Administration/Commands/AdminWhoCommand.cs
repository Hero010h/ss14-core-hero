﻿using System.Text;
using Content.Server.Administration.Managers;
using Content.Server.Afk;
using Content.Shared.Administration;
using Robust.Shared.Console;
using Robust.Shared.Utility;

namespace Content.Server.Administration.Commands;

[AnyCommand]
public sealed class AdminWhoCommand : IConsoleCommand
{
    public string Command => "adminwho";
    public string Description => "Returns a list of all admins on the server";
    public string Help => "Usage: adminwho";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        var adminMgr = IoCManager.Resolve<IAdminManager>();
        var afk = IoCManager.Resolve<IAfkManager>();

        var sb = new StringBuilder();
        var first = true;

        // WD start
        var isAdmin = shell.Player is {} player && adminMgr.HasAdminFlag(player, AdminFlags.Admin);
        foreach (var admin in adminMgr.ActiveAdmins)
        {
            var adminData = adminMgr.GetAdminData(admin)!;
            DebugTools.AssertNotNull(adminData);

            if (!isAdmin && adminData.Stealth)
                continue;

            if (!first)
                sb.Append('\n');
            first = false;

            sb.Append(admin.Name);
            if (adminData.Title is { } title)
                sb.Append($": [{title}]");

            if (!isAdmin)
                continue;

            if (afk.IsAfk(admin))
                sb.Append(" [AFK]");

            if (adminData.Stealth)
                sb.Append(" [Stealth]");
            // WD end
        }

        shell.WriteLine(sb.ToString());
    }
}
