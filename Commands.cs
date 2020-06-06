using Discord;
using Discord.Commands;
using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace Citadel
{
    public class Commands : ModuleBase<SocketCommandContext>
    {
        [Command("permission")]
        [RequireAdmin]
        public async Task AdminAsync(IGuildUser target, [Remainder]string text)
        {
            var id = Context.User.Id;
            if (id == target.Id)
            {
                await ReplyAsync("You cannot change your own permission level.");
            }
            else
            {
                var contextRank = Context.Guild.OwnerId == id ? 3 : (int)Program.Permissions[id];
                var targetRank = Context.Guild.OwnerId == target.Id ? 3 : Program.CheckPermission(target.Id, Permission.Mod) ? (int)Program.Permissions[target.Id] : 0;
                if (contextRank > targetRank)
                {
                    var success = Enum.TryParse(text, true, out Permission permission);
                    if (!success)
                    {
                        string options = contextRank == 3 ? "user, mod, or admin" : "user or mod";
                        await ReplyAsync($"Unknown permission level '{text}', options are {options}");
                    }
                    else
                    {
                        if((int)permission >= contextRank)
                        {
                            await ReplyAsync($"Cannot set {target.Username} to a rank equal to or above your own.");
                        }
                        else if (targetRank == (int)permission)
                        {
                            await ReplyAsync($"User {target.Username} already has a permission level of '{permission}'");
                        }
                        else
                        {
                            if (permission == Permission.User)
                            {
                                Program.Permissions.Remove(target.Id);
                            }
                            else
                            {
                                Program.Permissions[target.Id] = permission;
                            }
                            Program.WriteConfig();
                            await ReplyAsync($"Set {target.Username} permission level to {permission}");
                        }
                    }
                }
                else
                {
                    await ReplyAsync($"{target.Username} has a equal or higher permission level than you.");
                }
            }
        }

        [Command("help")]
        public async Task HelpAsync()
        {
            var commands = Program.Commands.GetExecutableCommandsAsync(Context, Program.Services);
            var result = new StringBuilder();
            foreach(var command in commands.Result)
            {
                if (command.Name == "help") continue;
                result.Append($"{command.Name}\n");
            }
            await Context.User.SendMessageAsync($"**__Available Commands__**\n{result}");
        }

        [Command("list")]
        [RequireMod]
        public async Task ListAsync([Remainder]string date = null)
        {
            var message = new StringBuilder();
            
            if(date == null)
            {
                string[] cappers = Program.CappedList.ToArray();

                message.Append($"**__Capped citizens for the week of {Program.CurrentResetDate.ToShortDateString()}__**\n");

                foreach (var capper in cappers)
                {
                    message.Append($"{capper}\n");
                }
                await ReplyAsync(message.ToString());
            }
            else
            {
                if(File.Exists(Program.COOKIE_DIRECTORY + $"/{date.Replace("/", "-")}.json"))
                {
                    JArray cookies = JArray.Parse(File.ReadAllText(Program.COOKIE_DIRECTORY + $"/{date.Replace("/", "-")}.json"));
                    message.Append($"**__Capped citizens for the week of {date.Replace("-", "/")}__**\n");
                    foreach (var cookie in cookies)
                    {
                        message.Append($"{cookie}\n");
                    }
                    await ReplyAsync(message.ToString());
                }
                else
                {
                    await ReplyAsync($"There is no data for {date}.");
                }
            }
        }

        [Command("add")]
        [RequireMod]
        public async Task AddAsync(params string[] names)
        {
            foreach(var name in names)
            {
                Program.CappedList.Add(name);
            }
            Program.WriteCookies();
            await ReplyAsync("Added all to capped list.");
        }

        [Command("remove")]
        [RequireMod]
        public async Task RemoveAsync(params string[] names)
        {
            foreach(var name in names)
            {
                Program.CappedList.Remove(name);
            }
            Program.WriteCookies();
            await ReplyAsync("Removed all from capped list.");
        }

        [Command("resetdate")]
        public async Task TimeToResetAsync()
            => await ReplyAsync($"Current citadel reset is **{Program.CurrentResetDate}**.");

        [Command("setrsn")]
        public async Task SetRSNAsync([Remainder]string text)
        {
            Program.RSNames[Context.User.Id] = text;
            await File.WriteAllTextAsync(Program.RSN_PATH + "/" + Context.User.Id, text);
            await ReplyAsync($"Set username to {text}");
        }

        [Command("haveicapped")]
        public async Task HaveICappedAsync([Remainder]string text = null)
        {
            if(text == null)
                await ReplyAsync(Program.RSNames.ContainsKey(Context.User.Id) ? Program.CappedListContains(Program.RSNames[Context.User.Id]) ? $"{Program.RSNames[Context.User.Id]} has capped this week!" : $"{Program.RSNames[Context.User.Id]} has not capped this week!" : $"Username not set, please set with {Program.PREFIX}setrsn");
            else
                await ReplyAsync(Program.CappedListContains(text) ? $"{text} has capped this week!" : $"{text} has not capped this week!");
        }

        [Command("setupdatechannel")]
        [RequireMod]
        public async Task SetUpdateChannelAsync(IGuildChannel raw = null)
        {
            if(raw == null)
            {
                Program.UpdateChannel = 0;
                Program.WriteConfig();
                await ReplyAsync("Removed the update channel.");
            }
            else if (raw is ITextChannel channel)
            {
                Program.UpdateChannel = channel.Id;
                Program.WriteConfig();
                await ReplyAsync($"Set the Update channel to {channel.Name}.");
            }
            else await ReplyAsync($"{raw.Name} must be a Text Channel.");
        }

        [Command("setresetchannel")]
        [RequireMod]
        public async Task SetResetChannelAsync(IGuildChannel raw = null)
        {
            if(raw == null)
            {
                Program.ResetChannel = 0;
                Program.WriteConfig();
                await ReplyAsync("Removed the reset channel.");
            }
            else if (raw is ITextChannel channel)
            {
                Program.ResetChannel = channel.Id;
                Program.WriteConfig();
                await ReplyAsync($"Set the Reset channel to {channel.Name}.");
            }
            else await ReplyAsync($"{raw.Name} must be a Text Channel.");
        }

        [Command("setlistchannel")]
        [RequireMod]
        public async Task SetListChannelAsync(IGuildChannel raw = null)
        {
            if(raw == null)
            {
                Program.ListChannel = 0;
                Program.WriteConfig();
                await ReplyAsync("Removed the list channel.");
            }
            else if(raw is ITextChannel channel)
            {
                Program.ListChannel = channel.Id;
                Program.WriteConfig();
                await ReplyAsync($"Set the List channel to {channel.Name}");
            }
            else await ReplyAsync($"{raw.Name} must be a Text Channel.");
        }

        [Command("setresetmessage")]
        [RequireMod]
        public async Task SetResetCommandAsync([Remainder]string message)
        {
            Program.ResetMessage = message;
            Program.WriteConfig();
            await ReplyAsync("Reset message has been set.");
        }

        [Command("getresetmessage")]
        [RequireMod]
        public async Task GetResetMessage()
        {
            await ReplyAsync(Program.ResetMessage);
        }
    }
}
