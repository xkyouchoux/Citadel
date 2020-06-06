using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Timers;

namespace Citadel
{
    public enum Permission
    {
        User  = 0,
        Mod = 1,
        Admin = 2
    }

    public class Program
    {
        public static readonly char PREFIX = '>';

        public static readonly uint DEFAULT_RESET_DAY = 6;
        public static readonly uint DEFAULT_RESET_HOUR = 0;
        public static readonly uint DEFAULT_RESET_MINUTE = 0;

        public static readonly string DEFAULT_RESET_MESSAGE = "<@&206234654091509760> :star: **Citadel has reset!** :european_castle:";
        public static readonly string DEFAULT_CAPPED_MESSAGE = "**{0}** has capped!\n";

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

        public static Timer Timer;

        public static ulong ResetChannel = 0L;
        public static ulong UpdateChannel = 0L;
        public static ulong ListChannel = 0L;

        public static volatile bool Paused = false;

        public static volatile List<string> CappedList;

        public static string ResetMessage = DEFAULT_RESET_MESSAGE;
        public static string CappedMessage = DEFAULT_CAPPED_MESSAGE;

        private static bool Updating = false;

        public static void Main()
        {
            MainAsync().GetAwaiter().GetResult();
            Services.Dispose();
        }

        public static bool CappedListContains(string name)
        {
            if (name == null)
                return false;
            foreach(var rsn in CappedList)
            {
                if(rsn.ToLower() == name.ToLower())
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
            Bot.Log += LogAsync;
            Commands.Log += LogAsync;


            await Bot.LoginAsync(TokenType.Bot, token);
            await Bot.StartAsync();

            await Bot.SetGameAsync($"{PREFIX}help");

            Bot.MessageReceived += async (rawMessage) =>
            {

                if (!(rawMessage is SocketUserMessage message)) return;
                if (message.Source != MessageSource.User) return;

                var argPos = 0;

                if (!message.HasCharPrefix(PREFIX, ref argPos)) return;

                var context = new SocketCommandContext(Bot, message);

                await Commands.ExecuteAsync(context, argPos, Services);
            };

            await Commands.AddModuleAsync<Commands>(Services);

            Timer.Start();

            await Task.Delay(-1);
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
                foreach(var permission in permissions)
                {
                    Permissions[permission["id"].ToObject<ulong>()] = (Permission)permission["value"].ToObject<int>();
                }
                CurrentResetDay = json["reset_day"].ToObject<uint>();
                CurrentResetHour = json["reset_hour"].ToObject<uint>();
                CurrentResetMinute = json["reset_minute"].ToObject<uint>();
                ResetChannel = json["reset_channel"].ToObject<ulong>();
                UpdateChannel = json["update_channel"].ToObject<ulong>();
                ListChannel = json["list)channel"].ToObject<ulong>();
                ResetMessage = json["reset_message"].ToString();
                CappedMessage = json["capped_message"].ToString();
            }
        }

        public static void WriteConfig()
        {

            JArray permissions = new JArray();
            foreach(var Permission in Permissions)
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
                ["capped_message"] = CappedMessage
            };
            File.WriteAllText(CONFIG_PATH, json.ToString());
        }

        public static void WriteCookies()
        {
            File.WriteAllText(CookiesPath, new JArray(CappedList.ToArray()).ToString());
        }

        private static void TimerElapsed(object sender, ElapsedEventArgs e)
        {
            var time = DateTime.UtcNow;
            if(time >= CurrentResetDate)
            {
                if(ResetChannel != 0)
                {
                    PostMessageAsync(ResetChannel, ResetMessage).GetAwaiter().GetResult();
                }
                if(ListChannel != 0)
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
                PreviousResetDate = CurrentResetDate;
                CurrentResetDate = CurrentResetDate.AddDays(7);
                CappedList.Clear();
            }
            else if(!Paused && time.Minute % 10 == 0 && !Updating)
            {
                Updating = true;
                Console.WriteLine($"Started Update at {DateTime.UtcNow}");
                Bot.SetStatusAsync(UserStatus.Idle);

                string[] cappers = Downloader.GetCappersList();

                var message = new StringBuilder();

                foreach(var capper in cappers)
                {
                    if (!CappedList.Contains(capper))
                    {
                        CappedList.Add(capper);
                        message.Append(string.Format(CappedMessage, capper));
                    }
                }

                if(message.Length > 0 && UpdateChannel != 0)
                {
                    PostMessageAsync(UpdateChannel, message.ToString()).GetAwaiter().GetResult();
                }
                CappedList.Sort();
                WriteCookies();

                Bot.SetStatusAsync(UserStatus.Online);
                Updating = false;
                Console.WriteLine($"Finished Update at {DateTime.UtcNow}");
            }
        }

        private static async Task PostMessageAsync(ulong channel, string message)
        {
            if (Bot.ConnectionState != ConnectionState.Connected)
                await Task.Delay(1);
            foreach(var guild in Bot.Guilds)
            {
                foreach(var textChannel in guild.TextChannels)
                {
                    if(textChannel.Id == channel)
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
            Timer = new Timer(1000);
            Timer.Elapsed += TimerElapsed;
            var now = DateTime.UtcNow;
            var date = now;
            while (date.DayOfWeek != (DayOfWeek)CurrentResetDay)
            {
                date = date.AddDays(1);
            }
            date = new DateTime(date.Year, date.Month, date.Day, (int)CurrentResetHour, (int)CurrentResetMinute, 0);
            if(now > date)
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
            foreach(var id in ids)
            {

                RSNames.Add(ulong.Parse(new FileInfo(id).Name), File.ReadAllText(id));
            }
        }
    }
}
