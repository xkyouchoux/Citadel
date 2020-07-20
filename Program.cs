using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Citadel
{
    public enum Permission
    {
        User = 0,
        Mod = 1,
        Admin = 2
    }

    public class Program
    {
        public static char Prefix = '?';

        public static readonly uint DEFAULT_RESET_DAY = 6;
        public static readonly uint DEFAULT_RESET_HOUR = 0;
        public static readonly uint DEFAULT_RESET_MINUTE = 0;

        public static readonly string DEFAULT_RESET_MESSAGE = "<@&206234654091509760> :star: **Citadel has reset!** :european_castle:";
        public static readonly string[] DEFAULT_CAPPED_MESSAGES = new string[] { "**{0}** has capped!" };

        public static readonly string CONFIG_PATH;
        public static readonly string RSN_PATH;
        public static readonly string COOKIE_DIRECTORY;

        public static uint CurrentResetDay = DEFAULT_RESET_DAY;
        public static uint CurrentResetHour = DEFAULT_RESET_HOUR;
        public static uint CurrentResetMinute = DEFAULT_RESET_MINUTE;

        public static DateTime PreviousResetDate;
        public static DateTime CurrentResetDate;

        public static Dictionary<ulong, Permission> Permissions;
        public static Dictionary<ulong, string> RSNames;

        public static DiscordSocketClient Bot;
        public static CommandService Commands;
        public static ServiceProvider Services;

        public static ulong ResetChannel = 0L;
        public static ulong UpdateChannel = 0L;
        public static ulong ListChannel = 0L;

        public static volatile bool Paused = false;

        public static volatile List<string> CappedList;
        public static List<string> CappedMessages = DEFAULT_CAPPED_MESSAGES.ToList();

        public static Random Random = new Random();

        public static HttpClient Client;

        public static string ResetMessage = DEFAULT_RESET_MESSAGE;

        private static bool Updating = false;

        private static DateTime _next = DateTime.MinValue;

        public static void Main()
        {
            MainAsync().GetAwaiter().GetResult();
        }

        public static bool CappedListContains(string name)
        {
            if (name == null)
                return false;
            foreach (var rsn in CappedList)
            {
                if (rsn.ToLower() == name.ToLower())
                {
                    return true;
                }
            }
            return false;
        }

        public static bool CheckPermission(ulong id, Permission value)
        {
            if (!Permissions.ContainsKey(id)) return false;
            return (int)Permissions[id] >= (int)value;
        }

        public static async Task MainAsync()
        {
            Services = GetServices();
            string token = Environment.GetEnvironmentVariable("citadel-bot-token");
            if (token == null || token.Length == 0)
            {
                Console.WriteLine("Invalid bot token, please set the token environtment variable 'citadel-bot-token' to a valid token.");
                return;
            }

            Bot = Services.GetRequiredService<DiscordSocketClient>();
            Commands = Services.GetRequiredService<CommandService>();
            Client = Services.GetRequiredService<HttpClient>();
            Bot.Log += LogAsync;
            Commands.Log += LogAsync;


            await Bot.LoginAsync(TokenType.Bot, token);
            await Bot.StartAsync();

            await Bot.SetGameAsync($"{Prefix}help");

            Bot.MessageReceived += async (rawMessage) =>
            {

                if (!(rawMessage is SocketUserMessage message)) return;
                if (message.Source != MessageSource.User) return;

                var argPos = 0;

                if (!message.HasCharPrefix(Prefix, ref argPos)) return;

                var context = new SocketCommandContext(Bot, message);

                await Commands.ExecuteAsync(context, argPos, Services);
            };

            Commands.CommandExecuted += async (command, context, result) =>
            {
                if (!command.IsSpecified)
                    return;
                Console.WriteLine(new LogMessage(LogSeverity.Info, "Command", $"User:[{context.User.Username}:{context.User.Id}] Message:[{context.Message}]"));

                await Task.CompletedTask;
            };

            await Commands.AddModuleAsync<Commands>(Services);

            Thread t = new Thread(() =>
            {
                while (true)
                {
                    try
                    {
                        var currentTime = Trim(DateTime.UtcNow);
                        if (currentTime >= _next)
                        {
                            TimerElapsed(currentTime);
                            _next = currentTime.AddMinutes(1);
                        }
                        Thread.Sleep(1);
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e);
                    }
                }
            })
            {
                IsBackground = true
            };
            t.Start();

            _next = Trim(DateTime.UtcNow);

            await Task.Delay(-1);
        }

        private static DateTime Trim(DateTime time)
        {
            return new DateTime(time.Year, time.Month, time.Day, time.Hour, time.Minute, 0, DateTimeKind.Utc);
        }

        private static Task LogAsync(LogMessage message)
        {
            Console.WriteLine(message);
            return Task.CompletedTask;
        }

        private static void ReadConfig()
        {
            if (!File.Exists(CONFIG_PATH))
                WriteConfig();
            else
            {
                var json = JObject.Parse(File.ReadAllText(CONFIG_PATH));
                Permissions.Clear();
                var permissions = json["permissions"];
                foreach (var permission in permissions)
                {
                    Permissions[permission["id"].ToObject<ulong>()] = (Permission)permission["value"].ToObject<int>();
                }
                CurrentResetDay = json["reset_day"].ToObject<uint>();
                CurrentResetHour = json["reset_hour"].ToObject<uint>();
                CurrentResetMinute = json["reset_minute"].ToObject<uint>();
                ResetChannel = json["reset_channel"].ToObject<ulong>();
                UpdateChannel = json["update_channel"].ToObject<ulong>();
                ListChannel = json["list_channel"].ToObject<ulong>();
                ResetMessage = json["reset_message"].ToString();
                Prefix = json["prefix"].ToObject<char>();
                CappedMessages = json["capped_messages"].ToObject<List<string>>();
            }
        }

        public static void WriteConfig()
        {

            JArray permissions = new JArray();
            foreach (var Permission in Permissions)
            {
                JObject permission = new JObject
                {
                    ["id"] = Permission.Key,
                    ["value"] = (int)Permission.Value
                };
                permissions.Add(permission);
            }
            JObject json = new JObject
            {
                ["permissions"] = permissions,
                ["reset_day"] = CurrentResetDay,
                ["reset_hour"] = CurrentResetHour,
                ["reset_minute"] = CurrentResetMinute,
                ["reset_channel"] = ResetChannel,
                ["update_channel"] = UpdateChannel,
                ["list_channel"] = ListChannel,
                ["reset_message"] = ResetMessage,
                ["prefix"] = Prefix,
                ["capped_messages"] = JToken.FromObject(CappedMessages)
            };
            File.WriteAllText(CONFIG_PATH, json.ToString());
        }

        public static void WriteCookies()
        {
            File.WriteAllText(CookiesPath, new JArray(CappedList.ToArray()).ToString());
        }

        private static void TimerElapsed(DateTime eventTime)
        {
            if (!Paused && !Updating && eventTime.Minute % 10 == 0)
            {
                Updating = true;
                Bot.SetStatusAsync(UserStatus.Idle);

                string[] cappers = Downloader.GetCappersList(Client);

                var message = new StringBuilder();

                foreach (var capper in cappers)
                {
                    if (!CappedList.Contains(capper))
                    {
                        CappedList.Add(capper);
                        if (CappedMessages.Count > 0)
                            message.Append(string.Format(CappedMessages[Random.Next(0, CappedMessages.Count)], capper));
                    }
                }

                if (message.Length > 0 && UpdateChannel != 0)
                {
                    PostMessageAsync(UpdateChannel, message.ToString()).GetAwaiter().GetResult();
                }
                CappedList.Sort();
                WriteCookies();

                Bot.SetStatusAsync(UserStatus.Online);
                Updating = false;
                Console.WriteLine(new LogMessage(LogSeverity.Info, "Timer", $"Update finished in {(DateTime.UtcNow - eventTime).TotalMilliseconds}ms."));
            }
            if (eventTime == CurrentResetDate)
            {
                if (ResetChannel != 0)
                {
                    PostMessageAsync(ResetChannel, ResetMessage).GetAwaiter().GetResult();
                }
                if (ListChannel != 0)
                {
                    var message = new StringBuilder();
                    string[] cappers = CappedList.ToArray();

                    message.Append($"**__Capped citizens for the week of {CurrentResetDate.ToShortDateString()}__**\n");

                    foreach (var capper in cappers)
                    {
                        message.Append($"{capper}\n");
                    }
                    PostMessageAsync(ListChannel, message.ToString()).GetAwaiter().GetResult();
                }
                WriteCookies();
                ProgressDate();
                CappedList.Clear();
            }
        }

        private static async Task PostMessageAsync(ulong channel, string message)
        {
            if (Bot.ConnectionState != ConnectionState.Connected)
                await Task.Delay(1);
            foreach (var guild in Bot.Guilds)
            {
                foreach (var textChannel in guild.TextChannels)
                {
                    if (textChannel.Id == channel)
                    {
                        await textChannel.SendMessageAsync(message);
                    }
                }
            }
        }

        private static ServiceProvider GetServices()
        {
            return new ServiceCollection()
                .AddSingleton<DiscordSocketClient>()
                .AddSingleton<HttpClient>()
                .AddSingleton<CommandService>()
                .BuildServiceProvider();
        }

        public static void ProgressDate()
        {
            PreviousResetDate = CurrentResetDate;
            CurrentResetDate = CurrentResetDate.AddDays(7);
        }

        public static string CookiesPath
        {
            get
            {
                return $"{COOKIE_DIRECTORY}/{CurrentResetDate.ToShortDateString().Replace("/", "-")}.json";
            }
        }

        static Program()
        {
            CappedList = new List<string>();
            Permissions = new Dictionary<ulong, Permission>();
            RSNames = new Dictionary<ulong, string>();
            CONFIG_PATH = Directory.GetCurrentDirectory() + "/config.json";
            RSN_PATH = Directory.GetCurrentDirectory() + "/rsn";
            COOKIE_DIRECTORY = $"{Directory.GetCurrentDirectory()}/Log";
            Directory.CreateDirectory(RSN_PATH);
            ReadConfig();
            var now = DateTime.UtcNow;
            var date = now;
            while (date.DayOfWeek != (DayOfWeek)CurrentResetDay)
            {
                date = date.AddDays(1);
            }
            date = new DateTime(date.Year, date.Month, date.Day, (int)CurrentResetHour, (int)CurrentResetMinute, 0, DateTimeKind.Utc);
            if (now > date)
            {
                PreviousResetDate = date;
                CurrentResetDate = date.AddDays(7);
            }
            else
            {
                PreviousResetDate = date.AddDays(-7);
                CurrentResetDate = date;
            }
            var path = CookiesPath;
            if (File.Exists(path))
            {
                var array = JArray.Parse(File.ReadAllText(path));
                foreach (var item in array)
                    CappedList.Add(item.ToString());
            }
            var ids = Directory.GetFiles(RSN_PATH);
            foreach (var id in ids)
            {

                RSNames.Add(ulong.Parse(new FileInfo(id).Name), File.ReadAllText(id));
            }
        }
    }
}
