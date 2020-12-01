using Discord;
using Discord.Commands;
using Discord.Webhook;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.WebSockets;
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

        public static readonly DateTime START_TIME = DateTime.UtcNow;

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
        public static ulong ItemChannel = 0L;

        public static bool UPDATE_READY = false;

        public static ulong Host = 0L;

        public static volatile bool Paused = false;

        public static bool Force = false;
        public static bool Cache = false;

        public static volatile List<string> CappedList;
        public static List<string> CappedMessages = DEFAULT_CAPPED_MESSAGES.ToList();

        public static Random Random = new Random();

        public static HttpClient Client;

        public static string ResetMessage = DEFAULT_RESET_MESSAGE;

        public static string AchievementWebhookUrl;

        private static bool Updating = false;

        private static DateTime _next = DateTime.MinValue;

        public static DiscordWebhookClient AchievementWebhook;

        public static event Action<string, string> OnNamechange;
        public static event Action<string> OnLeave;
        public static event Action<string> OnJoin;

        public static void Main()
        {
            MainAsync().GetAwaiter().GetResult();
        }

        public static bool CappedListContains(string name)
        {
            if (name == null)
            {
                return false;
            }

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
            if (!Permissions.ContainsKey(id))
            {
                return false;
            }

            return (int)Permissions[id] >= (int)value;
        }

        public static async Task MainAsync()
        {
            if (File.Exists($"{Directory.GetCurrentDirectory()}/Citadel-old.exe"))
            {
                File.Delete($"{Directory.GetCurrentDirectory()}/Citadel-old.exe");
            }

            Services = GetServices();
            string token = Environment.GetEnvironmentVariable("citadel-bot-token");
            Host = ulong.Parse(Environment.GetEnvironmentVariable("citadel-bot-host"));
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

                if (!(rawMessage is SocketUserMessage message))
                {
                    return;
                }

                if (message.Source != MessageSource.User)
                {
                    return;
                }

                var argPos = 0;

                if (!message.HasCharPrefix(Prefix, ref argPos))
                {
                    return;
                }

                var context = new SocketCommandContext(Bot, message);

                await Commands.ExecuteAsync(context, argPos, Services);
            };

            Commands.CommandExecuted += async (command, context, result) =>
            {
                if (!command.IsSpecified)
                {
                    return;
                }

                Console.WriteLine(new LogMessage(LogSeverity.Info, "Command", $"User:[{context.User.Username}:{context.User.Id}] Message:[{context.Message}]"));

                await Task.CompletedTask;
            };

            await Commands.AddModuleAsync<Commands>(Services);

            OnJoin += (name) =>
            {
                Console.WriteLine($"Caching achievements for [{name}]");
                Downloader.GetAchievements(Client, Downloader.GetProfiles(Client, new string[] { name }));
            };

            OnLeave += (name) =>
            {
                if (File.Exists($"{Directory.GetCurrentDirectory()}/achievements/{name}.json"))
                {
                    Console.WriteLine($"Removing achievement cache for [{name}]");
                    File.Delete($"{Directory.GetCurrentDirectory()}/achievements/{name}.json");
                }
            };

            OnNamechange += (prevName, newName) =>
            {
                Console.WriteLine($"Renaming achievement cache from [{prevName}] to [{newName}]");
                File.Move($"{Directory.GetCurrentDirectory()}/achievements/{prevName}.json", $"{Directory.GetCurrentDirectory()}/achievements/{newName}.json");
            };

            _next = Trim(DateTime.UtcNow);

            await Task.Run(() =>
            {
                while (!UPDATE_READY)
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
            });

            await Bot.StopAsync();

            var bytes = await Client.GetByteArrayAsync(Environment.GetEnvironmentVariable("citadel-bot-url"));

            File.Move($"{Directory.GetCurrentDirectory()}/Citadel.exe", $"{Directory.GetCurrentDirectory()}/Citadel-old.exe");
            File.WriteAllBytes($"{Directory.GetCurrentDirectory()}/Citadel.exe", bytes);

            Process.Start($"{Directory.GetCurrentDirectory()}/run.bat");

            Process.GetCurrentProcess().Kill();
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
            {
                WriteConfig();
            }
            else
            {
                var json = JObject.Parse(File.ReadAllText(CONFIG_PATH));
                Permissions.Clear();
                if(json.ContainsKey("permissions"))
                {
                    var permissions = json["permissions"];
                    foreach (var permission in permissions)
                    {
                        Permissions[permission["id"].ToObject<ulong>()] = (Permission)permission["value"].ToObject<int>();
                    }
                }
                if(json.ContainsKey("reset_day"))
                {
                    CurrentResetDay = json["reset_day"].ToObject<uint>();
                }
                if(json.ContainsKey("reset_hour"))
                {
                    CurrentResetHour = json["reset_hour"].ToObject<uint>();
                }
                if(json.ContainsKey("reset_minute"))
                {
                    CurrentResetMinute = json["reset_minute"].ToObject<uint>();
                }
                if(json.ContainsKey("reset_channel"))
                {
                    ResetChannel = json["reset_channel"].ToObject<ulong>();
                }
                if(json.ContainsKey("update_channel"))
                {
                    UpdateChannel = json["update_channel"].ToObject<ulong>();
                }
                if(json.ContainsKey("list_channel"))
                {
                    ListChannel = json["list_channel"].ToObject<ulong>();
                }
                if(json.ContainsKey("reset_message"))
                {
                    ResetMessage = json["reset_message"].ToString();
                }
                if(json.ContainsKey("prefix"))
                {
                    Prefix = json["prefix"].ToObject<char>();
                }
                if(json.ContainsKey("capped_messages"))
                {
                    CappedMessages = json["capped_messages"].ToObject<List<string>>();
                }
                if(json.ContainsKey("item_channel"))
                {
                    ItemChannel = json["item_channel"].ToObject<ulong>();
                }
                if (json.ContainsKey("achievement_webhook_url"))
                {
                    AchievementWebhookUrl = json["achievement_webhook_url"].ToString();
                    if(AchievementWebhookUrl != "")
                        AchievementWebhook = new DiscordWebhookClient(AchievementWebhookUrl);
                }
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
                ["item_channel"] = ItemChannel,
                ["reset_message"] = ResetMessage,
                ["prefix"] = Prefix,
                ["capped_messages"] = JToken.FromObject(CappedMessages),
                ["achievement_webhook_url"] = AchievementWebhookUrl
            };
            File.WriteAllText(CONFIG_PATH, json.ToString());
        }

        public static void WriteCookies()
        {
            File.WriteAllText(CookiesPath, new JArray(CappedList.ToArray()).ToString());
        }

        private static void TimerElapsed(DateTime eventTime)
        {
            try
            {
                List<Downloader.MemberData> membercache;
                if (File.Exists($"{Directory.GetCurrentDirectory()}/membercache.csv"))
                    membercache = Downloader.ParseMemberData(File.ReadAllText($"{Directory.GetCurrentDirectory()}/membercache.csv")).ToList();
                else
                    membercache = new List<Downloader.MemberData>();
                string result = Client.GetStringAsync($"http://services.runescape.com/m=clan-hiscores/members_lite.ws?clanName={Downloader.CLAN_NAME}").GetAwaiter().GetResult();
                if (result != null && result.Length > 0)
                    File.WriteAllText($"{Directory.GetCurrentDirectory()}/membercache.csv", result);
                var current = Downloader.ParseMemberData(result).ToList();
                if(membercache.Count > 0 && current.Count > 0)
                {
                    var leave = Downloader.MemberData.Diff(membercache, current);
                    var join = Downloader.MemberData.Diff(current, membercache);
                    if (leave.Count > 0 && join.Count == 0)
                    {
                        foreach (var leaver in leave)
                        {
                            OnLeave?.Invoke(leaver.Name);
                        }
                    }
                    else if (leave.Count == 0 && join.Count > 0)
                    {
                        foreach(var joiner in join)
                        {
                            OnJoin?.Invoke(joiner.Name);
                        }
                    }
                    else
                    {
                        for(int i = join.Count - 1; i >= 0; i--)
                        {
                            for(int j = leave.Count - 1; j >= 0; j--)
                            {
                                var joiner = join[i];
                                var leaver = leave[j];

                                if(joiner.Rank == leaver.Rank && joiner.ClanXP == leaver.ClanXP && joiner.ClanKills == leaver.ClanKills)
                                {
                                    join.RemoveAt(i);
                                    leave.RemoveAt(j);
                                    OnNamechange?.Invoke(leaver.Name, joiner.Name);
                                    break;
                                }
                            }
                        }

                        if (join.Count > 0)
                            join.ForEach((item) => OnJoin?.Invoke(item.Name));
                        if (leave.Count > 0)
                            leave.ForEach((item) => OnLeave?.Invoke(item.Name));
                    }

                }
            }
            catch(Exception e) 
            {
                Console.WriteLine(e);
            }
            if (!Paused && !Updating && (Force || eventTime.Minute % 10 == 0))
            {
                Force = false;
                Updating = true;
                try
                {
                    var profiles = Downloader.GetProfiles(Client, Downloader.GetClanList(Client));

                    string[] cappers = Downloader.GetCappersList(profiles);

                    var message = new StringBuilder();

                    foreach (var capper in cappers)
                    {
                        if (!CappedList.Contains(capper))
                        {
                            CappedList.Add(capper);
                            if (CappedMessages.Count > 0)
                            {
                                message.Append(string.Format(CappedMessages[Random.Next(0, CappedMessages.Count)], capper) + "\n");
                            }
                        }
                    }

                    if (message.Length > 0 && UpdateChannel != 0)
                    {
                        PostMessageAsync(UpdateChannel, message.ToString()).GetAwaiter().GetResult();
                    }
                    new Thread(() =>
                    {

                        Downloader.GetItems(Client, profiles);
                        return;
                        string[] achievements = Downloader.GetAchievements(Client, profiles);
                        if (AchievementWebhook != null && !Cache)
                        {
                            try
                            {
                                var builder = new StringBuilder();
                                foreach (var achievement in achievements)
                                {
                                    if (builder.Length + achievement.Length + 1 >= 2000)
                                    {
                                        AchievementWebhook.SendMessageAsync(builder.ToString());
                                        builder.Clear();
                                    }
                                    builder.Append(achievement);
                                }
                                if (builder.Length > 0)
                                {
                                    AchievementWebhook.SendMessageAsync(builder.ToString());
                                }
                            }
                            catch { }
                        }
                        Cache = false;
                    }).Start();
                }
                catch { }
                CappedList.Sort();
                WriteCookies();
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
            {
                await Task.Delay(1);
            }

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
            Directory.CreateDirectory(COOKIE_DIRECTORY);
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
                {
                    CappedList.Add(item.ToString());
                }
            }
            var ids = Directory.GetFiles(RSN_PATH);
            foreach (var id in ids)
            {

                RSNames.Add(ulong.Parse(new FileInfo(id).Name), File.ReadAllText(id));
            }
        }
    }
}
