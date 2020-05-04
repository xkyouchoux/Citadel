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
        public Task AdminAsync()
        {
            return ReplyAsync("Admin");
        }
    }
}
