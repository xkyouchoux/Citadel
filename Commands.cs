using Discord;
using Discord.Commands;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Citadel
{
    public class Commands : ModuleBase<SocketCommandContext>
    {
        [Command("ip")]
        [RequireHost]
        public async Task Ip()
        {
            await Context.User.SendMessageAsync(await Program.Client.GetStringAsync("http://ipinfo.io/ip"));
        }
        
        [Command("shutdown")]
        [RequireHost]
        public async Task UpdateAsync()
        {
            Program.SHUTDOWN = true;
            await Task.CompletedTask;
        }

        [Command("forcecache")]
        [RequireHost]
        public async Task ForceCache()
        {
            Program.Force = true;
            Program.Cache = true;
            await Task.CompletedTask;
        }

        [Command("force")]
        [RequireHost]
        public async Task ForceAsync()
        {
            Program.Force = true;
            new Thread(Program.TimerElapsed).Start();
            await Task.CompletedTask;
        }

        [Command("prefix")]
        [RequireMod]
        public async Task PrefixAsync(char prefix)
        {
            Program.Prefix = prefix;
            Program.WriteConfig();
            await Program.Bot.SetGameAsync($"{prefix}help");
            await ReplyAsync($"Set prefix to [{prefix}].");
        }

        [Command("webhook")]
        [RequireMod]
        public async Task AchievementWebhookAsync([Remainder]string url = "")
        {
            Program.AchievementWebhookUrl = url;
            Program.WriteConfig();
            if(url == "")
            {
                Program.AchievementWebhook = null;
                await ReplyAsync("Removed the achievement webhook url.");
            }
            else
            {
                Program.AchievementWebhook = new Discord.Webhook.DiscordWebhookClient(url);
                await ReplyAsync("Set the achievement webhook url.");
            }
        }

        [Command("uptime")]
        public async Task UptimeAsync()
        {
            var uptime = DateTime.UtcNow - Program.START_TIME;
            var result = new StringBuilder();
            if (uptime.Days == 1)
                result.Append("1 day, ");
            else
                result.Append($"{uptime.Days} days, ");
            if (uptime.Hours == 1)
                result.Append("1 hour, ");
            else
                result.Append($"{uptime.Hours} hours, ");
            if (uptime.Minutes == 1)
                result.Append("1 minute, ");
            else
                result.Append($"{uptime.Minutes} minutes, ");
            if (uptime.Seconds == 1)
                result.Append("1 second");
            else
                result.Append($"{uptime.Seconds} seconds");
            await ReplyAsync($"Current uptime: {result}");
        }

        [Command("permission")]
        [RequireAdmin]
        public async Task PermissionAsync(string text)
        {
            var success = Enum.TryParse(text, true, out Permission permission);
            if (success)
            {
                if (permission == Permission.User)
                {
                    await ReplyAsync("To many.");
                }
                else
                {
                    var list = new List<ulong>();
                    foreach (var perm in Program.Permissions)
                    {
                        if (perm.Value == permission)
                        {
                            list.Add(perm.Key);
                        }
                    }

                    var result = new StringBuilder();
                    result.Append($"__Users with permission {permission}__\n");

                    foreach (var item in list)
                    {
                        var member = Context.Guild.Users.Where((user) => user.Id == item);
                        if (member.Count() == 1)
                        {
                            result.Append($"{member.ElementAt(0).Username}\n");
                        }
                        else
                        {
                            result.Append($"Unknown ID: {item}\n");
                        }
                    }

                    await ReplyAsync(result.ToString());
                }
            }
            else
            {
                await ReplyAsync($"Unknown permission level '{text}', options are mod or admin.");
            }
        }

        [Command("permission")]
        [RequireAdmin]
        public async Task AdminAsync(IGuildUser target, [Remainder] string text)
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
                        if ((int)permission >= contextRank)
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
            foreach (var command in commands.Result)
            {
                if (command.Name == "help")
                {
                    continue;
                }

                result.Append($"{command.Name}\n");
            }
            await Context.User.SendMessageAsync($"**__Available Commands__**\n{result}");
        }

        [Command("additemblacklist")]
        [RequireMod]
        public async Task AddItemBlacklistAsync([Remainder] string text)
        {
            Program.ItemBlacklist.Add(text);
            Program.WriteConfig();
            await ReplyAsync($"Added {text} to item blacklist.");
        }

        [Command("removeitemblacklist")]
        [RequireMod]
        public async Task RemoveItemBlacklistAsync([Remainder] string text)
        {
            Program.ItemBlacklist.Remove(text);
            Program.WriteConfig();
            await ReplyAsync($"Removed {text} to item blacklist.");
        }

        [Command("clearitemblacklist")]
        [RequireMod]
        public async Task ClearItemBlacklistAsync()
        {
            Program.ItemBlacklist.Clear();
            Program.WriteConfig();
            await ReplyAsync($"Cleared item blacklist.");
        }

        [Command("listitemblacklist")]
        [RequireMod]
        public async Task ListItemBlacklist()
        {
            try
            {
                await ReplyAsync("__**Item keywords blacklisted**__");
                var builder = new StringBuilder();
                foreach (var item in Program.ItemBlacklist)
                {
                    if (builder.Length + item.Length + 1 >= 2000)
                    {
                        await ReplyAsync(builder.ToString());
                        builder.Clear();
                    }
                    builder.Append(item);
                }
                if (builder.Length > 0)
                {
                    await ReplyAsync(builder.ToString());
                }
            }
            catch { }
            await Task.CompletedTask;
        }

        [Command("list")]
        [RequireMod]
        public async Task ListAsync([Remainder] string date = null)
        {
            var message = new StringBuilder();

            if (date == null)
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
                if (File.Exists(Program.COOKIE_DIRECTORY + $"/{date.Replace("/", "-")}.json"))
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

        [Command("addcappers")]
        [RequireMod]
        public async Task AddCappersAsync(params string[] names)
        {
            var tmp = new List<string>();
            foreach (var name in names)
            {
                if (!Program.CappedList.Contains(name))
                {
                    Program.CappedList.Add(name);
                    tmp.Add(name);
                }
            }
            Program.WriteCookies();
            await ReplyAsync($"Added [{string.Join(", ", tmp)}] to capped list.");
        }

        [Command("removecappers")]
        [RequireMod]
        public async Task RemoveCappersAsync(params string[] names)
        {
            var tmp = new List<string>();
            foreach (var name in names)
            {
                if (Program.CappedList.Contains(name))
                {
                    Program.CappedList.Remove(name);
                    tmp.Add(name);
                }
            }
            Program.WriteCookies();
            await ReplyAsync($"Removed [{string.Join(", ", tmp)}] from capped list.");
        }

        [Command("resetdate")]
        public async Task TimeToResetAsync()
            => await ReplyAsync($"Current citadel reset is **{Program.CurrentResetDate}**.");

        [Command("setrsn")]
        public async Task SetRSNAsync([Remainder] string text)
        {
            Program.RSNames[Context.User.Id] = text;
            await File.WriteAllTextAsync(Program.RSN_PATH + "/" + Context.User.Id, text);
            await ReplyAsync($"Set username to {text}");
        }

        [Command("haveicapped")]
        public async Task HaveICappedAsync([Remainder] string text = null)
        {
            if (text == null)
            {
                await ReplyAsync(Program.RSNames.ContainsKey(Context.User.Id) ? Program.CappedListContains(Program.RSNames[Context.User.Id]) ? $"{Program.RSNames[Context.User.Id]} has capped this week!" : $"{Program.RSNames[Context.User.Id]} has not capped this week!" : $"Username not set, please set with {Program.Prefix}setrsn");
            }
            else
            {
                await ReplyAsync(Program.CappedListContains(text) ? $"{text} has capped this week!" : $"{text} has not capped this week!");
            }
        }

        [Command("setupdatechannel")]
        [RequireMod]
        public async Task SetUpdateChannelAsync(IGuildChannel raw = null)
        {
            if (raw == null)
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
            else
            {
                await ReplyAsync($"{raw.Name} must be a Text Channel.");
            }
        }

        [Command("setresetchannel")]
        [RequireMod]
        public async Task SetResetChannelAsync(IGuildChannel raw = null)
        {
            if (raw == null)
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
            else
            {
                await ReplyAsync($"{raw.Name} must be a Text Channel.");
            }
        }

        [Command("setlistchannel")]
        [RequireMod]
        public async Task SetListChannelAsync(IGuildChannel raw = null)
        {
            if (raw == null)
            {
                Program.ListChannel = 0;
                Program.WriteConfig();
                await ReplyAsync("Removed the list channel.");
            }
            else if (raw is ITextChannel channel)
            {
                Program.ListChannel = channel.Id;
                Program.WriteConfig();
                await ReplyAsync($"Set the List channel to {channel.Name}");
            }
            else
            {
                await ReplyAsync($"{raw.Name} must be a Text Channel.");
            }
        }

        [Command("setitemchannel")]
        [RequireMod]
        public async Task SetItemChannelAsync(IGuildChannel raw = null)
        {
            if (raw == null)
            {
                Program.ItemChannel = 0;
                Program.WriteConfig();
                await ReplyAsync("Removed the item channel.");
            }
            else if (raw is ITextChannel channel)
            {
                Program.ItemChannel = channel.Id;
                Program.WriteConfig();
                await ReplyAsync($"Set the Item channel to {channel.Name}");
            }
            else
            {
                await ReplyAsync($"{raw.Name} must be a Text Channel.");
            }
        }

        [Command("setresetmessage")]
        [RequireMod]
        public async Task SetResetCommandAsync([Remainder] string message)
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

        [Command("getmessages")]
        [RequireMod]
        public async Task GetMessages()
        {
            var result = new StringBuilder();
            result.Append("__Capped Messages__\n");

            foreach (var message in Program.CappedMessages)
            {
                result.Append($"{message}\n");
            }
            await ReplyAsync(result.ToString());
        }

        [Command("addmessage")]
        [RequireMod]
        public async Task AddMessageAsync([Remainder] string text)
        {
            if (!text.Contains("{0}"))
            {
                await ReplyAsync("Capped message must contain '{0}'!");
            }
            else if (Program.CappedMessages.Contains(text))
            {
                await ReplyAsync($"Capped messages already contains '{text}'.");
            }
            else
            {
                Program.CappedMessages.Add(text);
                Program.WriteConfig();
                await ReplyAsync($"Added '{text}' to the capped messages.");
            }
        }

        private string RemoveFormatting(string text)
        {
            return Regex.Replace(text, "[*_~`]", "");
        }

        [Command("removemessage")]
        [RequireMod]
        public async Task RemoveMessageAsync([Remainder] string text)
        {
            string result = null;
            Program.CappedMessages.ForEach((message) =>
            {
                if (RemoveFormatting(text) == RemoveFormatting(message))
                {
                    result = message;
                }
            });

            if (result == null)
            {
                await ReplyAsync($"Capped messages does not contain '{text}'.");
            }
            else
            {
                Program.CappedMessages.Remove(result);
                Program.WriteConfig();
                await ReplyAsync($"Removed '{result}' from the capped messages.");
            }
        }
    }
}
