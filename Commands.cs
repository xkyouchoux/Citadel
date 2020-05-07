﻿using Discord;
using Discord.Commands;
using System;
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
                        if (targetRank == (int)permission)
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
                            Program.Dirty = true;
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
        }

        [Command("list")]
        [RequireMod]
        public async Task ListAsync()
        {
            string[] cappers = Program.CappedList.ToArray();

            var message = new StringBuilder();

            message.Append($"**__Capped citizens for the week of {Program.PreviousResetDate.ToShortDateString()} to {Program.CurrentResetDate.ToShortDateString()}__**\n");

            foreach (var capper in cappers)
            {
                message.Append($"{capper}\n");
            }

            await ReplyAsync(message.ToString());
        }

        [Command("resetdate")]
        public async Task TimeToResetAsync()
            => await ReplyAsync(Program.CurrentResetDate.ToString());

        [Command("setrsn")]
        public async Task SetRSNAsync([Remainder]string text)
        {
            Program.RSNames[Context.User.Id] = text;
            Program.Dirty = true;
            await ReplyAsync($"Set username to {text}");
        }

        [Command("haveicapped")]
        public async Task HaveICappedAsync([Remainder]string text)
            => await ReplyAsync(Program.CappedList.Contains(text) ? $"{text} has capped this week!" : $"{text} has not capped this week!");

        [Command("haveicapped")]
        public async Task HaveICappedAsync()
            => await ReplyAsync(Program.RSNames.ContainsKey(Context.User.Id) ? Program.CappedList.Contains(Program.RSNames[Context.User.Id]) ? $"{Program.RSNames[Context.User.Id]} has capped this week!" : $"{Program.RSNames[Context.User.Id]} has not capped this week!" : $"Username not set, please set with {Program.PREFIX}setrsn");
    }
}
