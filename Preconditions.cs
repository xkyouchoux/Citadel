using Discord.Commands;
using Discord.WebSocket;
using System;
using System.Threading.Tasks;

namespace Citadel
{
    public class RequireHostAttribute : PreconditionAttribute
    {
        public override Task<PreconditionResult> CheckPermissionsAsync(ICommandContext context, CommandInfo command, IServiceProvider services)
        {
            if (context.User.Id == Program.Host)
                return Task.FromResult(PreconditionResult.FromSuccess());
            else
                return Task.FromResult(PreconditionResult.FromError("Must be Host."));
        }
    }

    public class RequireAdminAttribute : PreconditionAttribute
    {
        public override Task<PreconditionResult> CheckPermissionsAsync(ICommandContext context, CommandInfo command, IServiceProvider services)
        {
            if (context.User is SocketGuildUser)
            {
                if (context.Guild.OwnerId == context.User.Id || Program.CheckPermission(context.User.Id, Permission.Admin))
                {
                    return Task.FromResult(PreconditionResult.FromSuccess());
                }
                else return Task.FromResult(PreconditionResult.FromError("Must be Admin+."));
            }
            else return Task.FromResult(PreconditionResult.FromError("Must be used in a guild."));
        }
    }

    public class RequireModAttribute : PreconditionAttribute
    {
        public override Task<PreconditionResult> CheckPermissionsAsync(ICommandContext context, CommandInfo command, IServiceProvider services)
        {
            if (context.User is SocketGuildUser)
            {
                if (context.Guild.OwnerId == context.User.Id || Program.CheckPermission(context.User.Id, Permission.Mod))
                {
                    return Task.FromResult(PreconditionResult.FromSuccess());
                }
                else return Task.FromResult(PreconditionResult.FromError("Must be Mod+."));
            }
            else return Task.FromResult(PreconditionResult.FromError("Must be used in a guild."));
        }
    }
}
