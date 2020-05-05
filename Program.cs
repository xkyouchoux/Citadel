using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
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

        public static uint CurrentResetDay = DEFAULT_RESET_DAY;
        public static uint CurrentResetHour = DEFAULT_RESET_HOUR;
        public static uint CurrentResetMinute = DEFAULT_RESET_MINUTE;

        public static DateTime PreviousResetDate;
        public static DateTime CurrentResetDate;

        public static ulong Admin = 0L;

        public static Dictionary<ulong, Permission> Permissions;

        public static DiscordSocketClient Bot;

        public static Timer Timer;

        public static ulong ResetChannel = 0L;
        public static ulong UpdateChannel = 0L;

        public static volatile bool Paused = false;

        public static readonly List<string> CappedList;

        public static string ResetMessage = DEFAULT_RESET_MESSAGE;
        public static string CappedMessage = DEFAULT_CAPPED_MESSAGE;

        public static void Main()
        {
            MainAsync().GetAwaiter().GetResult();
        }

        public static bool CheckPermission(ulong id, Permission value)
        {
            if (!Permissions.ContainsKey(id)) return false;
            return value >= Permissions[id];
        }

        public static async Task MainAsync()
        {
            using var services = GetServices();
            string token = Environment.GetEnvironmentVariable("citadel-bot-token");
            if (token == null || token.Length == 0)
            {
                Console.WriteLine("Invalid bot token, please set the token environtment variable 'citadel-bot-token' to a valid token.");
                return;
            }

            Bot = services.GetRequiredService<DiscordSocketClient>();
            var commands = services.GetRequiredService<CommandService>();
            Bot.Log += LogAsync;
            commands.Log += LogAsync;


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

                await commands.ExecuteAsync(context, argPos, services);
            };

            await commands.AddModuleAsync<Commands>(services);

            await Task.Delay(-1);
        }

        private static Task LogAsync(LogMessage message)
        {
            Console.WriteLine(message);
            return Task.CompletedTask;
        }

        public static void ReadConfig()
        {
            if (!File.Exists(CONFIG_PATH))
                WriteConfig();
            else
            {
                var json = JObject.Parse(File.ReadAllText(CONFIG_PATH));
                Admin = json["admin"].ToObject<ulong>();
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
                ["admin"] = Admin,
                ["permissions"] = permissions,
                ["reset_day"] = CurrentResetDay,
                ["reset_hour"] = CurrentResetHour,
                ["reset_minute"] = CurrentResetMinute,
                ["reset_channel"] = ResetChannel,
                ["update_channel"] = UpdateChannel,
                ["reset_message"] = ResetMessage,
                ["capped_message"] = CappedMessage
            };
            File.WriteAllText(CONFIG_PATH, json.ToString());
        }

        private static void TimerElapsed(object sender, ElapsedEventArgs e)
        {
            var time = e.SignalTime;
            if(time >= CurrentResetDate)
            {
                if(ResetChannel != 0)
                {
                    PostMessageAsync(ResetChannel, ResetMessage).GetAwaiter().GetResult();
                }
                PreviousResetDate = CurrentResetDate;
                CurrentResetDate = CurrentResetDate.AddDays(7);
                CappedList.Clear();
            }
            else if(!Paused && time.Hour % 10 == 0)
            {
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

                Bot.SetStatusAsync(UserStatus.Online);
            }
        }

        private static async Task PostMessageAsync(ulong channel, string message)
        {
            if (Bot.ConnectionState != ConnectionState.Connected)
                await Task.Delay(1);
            foreach(var guild in Bot.Guilds)
            {
                Console.WriteLine(guild);
                foreach(var textChannel in guild.TextChannels)
                {
                    Console.WriteLine(textChannel);
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

        static Program()
        {
            CappedList = new List<string>();
            Permissions = new Dictionary<ulong, Permission>();
            CONFIG_PATH = Directory.GetCurrentDirectory() + "/config.json";
            ReadConfig();
            Timer = new Timer(1000);
            Timer.Elapsed += TimerElapsed;
            var now = DateTime.UtcNow;
            var date = now;
            Console.WriteLine(now.DayOfWeek);
            Console.WriteLine((DayOfWeek)CurrentResetDay);
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
        }
    }
}
