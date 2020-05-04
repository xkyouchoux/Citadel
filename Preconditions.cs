using Discord.Commands;
using Discord.WebSocket;
using System;
using System.Threading.Tasks;

namespace Citadel
{
    public class RequireAdminAttribute : PreconditionAttribute
    {
        public override Task<PreconditionResult> CheckPermissionsAsync(ICommandContext context, CommandInfo command, IServiceProvider services)
        {
            if (context.User is SocketGuildUser user)
            {
                if (context.Guild.OwnerId == context.User.Id || (Program.Permissions.ContainsKey(context.User.Id) && Program.Permissions[context.User.Id] == Permission.Admin))
                {
                    return Task.FromResult(PreconditionResult.FromSuccess());
                }
                else return Task.FromResult(PreconditionResult.FromError("Must be admin or owner."));
            }
            else return Task.FromResult(PreconditionResult.FromError("Must be used in a guild."));
        }
    }
}
