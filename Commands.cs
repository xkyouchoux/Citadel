using Discord;
using Discord.Commands;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Citadel
{
    public class Commands : ModuleBase<SocketCommandContext>
    {
        [RequireOwner]
        [Command("admin")]
        public async Task AdminAsync(IGuildUser target)
        {
            if(Program.CheckPermission(target.Id, Permission.Admin))
            {
                Program.Permissions[target.Id] = Permission.User;
                await ReplyAsync($"Set {target.Username} as User.");
            }
            else
            {
                Program.Permissions[target.Id] = Permission.Admin;
                await ReplyAsync($"Set {target.Username} role as Admin.");
            }
            Program.WriteConfig();
        }
    }
}
