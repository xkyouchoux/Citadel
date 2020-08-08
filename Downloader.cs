﻿using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace Citadel
{
    public static class Downloader
    {
        public struct MemberData
        {
            public string Name;
            public string Rank;
            public int ClanXP;
            public int ClanKills;

            public static List<MemberData> Diff(List<MemberData> list1, List<MemberData> list2)
            {
                var result = new List<MemberData>();

                foreach(var item1 in list1)
                {
                    bool valid = false;
                    foreach(var item2 in list2)
                    {
                        if (item1.Name == item2.Name)
                        {
                            valid = true;
                            break;
                        }
                    }
                    if (!valid)
                        result.Add(item1);
                }

                return result;
            }
        }

        public static readonly int CAPPED_CODE = 1;
        public static readonly int NO_ERROR = 0;
        public static readonly int EXCEPTION_CODE = -1;
        public static readonly int NO_PROFILE_CODE = -2;
        public static readonly int NOT_A_MEMBER_CODE = -3;
        public static readonly int PROFILE_PRIVATE_CODE = -4;

        public static readonly string CLAN_NAME = "Kingdom of Ashdale";

        private static readonly TimeZoneInfo _timeZone;

        private static int level120 = 104273167;

        private static string defaultAchievements;

        private static string[] skillNames =
        {
            "Attack",
            "Defense",
            "Strength",
            "Constitution",
            "Ranged",
            "Prayer",
            "Magic",
            "Cooking",
            "Woodcutting",
            "Fletching",
            "Fishing",
            "Firemaking",
            "Crafting",
            "Smithing",
            "Mining",
            "Herblore",
            "Agility",
            "Thieving",
            "Slayer",
            "Farming",
            "Runecrafting",
            "Hunter",
            "Construction",
            "Summoning",
            "Dungeoneering",
            "Divination",
            "Invention",
            "Archaeology"
        };

        private static string[] achievementStrings = new string[]
        {
            ":star: **{0}** has achieved **{1} Overall XP!**\n",
            ":star: **{0}** has found **{1}!**\n",
            ":star: **{0}** has achieved **99 {1}!**\n",
            ":star: **{0}** has achieved **120 {1}!**\n",
            ":star: **{0}** has achieved **{1}M {2} XP!**\n"
        };

        public static JObject[] GetProfiles(HttpClient client, string[] members)
        {
            var names = members.ToList();
            var tasks = new List<Task<string>>();

            while (names.Count > 0)
            {
                var count = Math.Min(20, names.Count);

                var range = names.GetRange(0, count);
                range.ForEach((name) => tasks.Add(client.GetStringAsync($"https://apps.runescape.com/runemetrics/profile/profile?user={name}&activities=20")));
                names.RemoveRange(0, count);
                Task.WhenAll(tasks).GetAwaiter().GetResult();
            }

            var raw = Task.WhenAll(tasks).GetAwaiter().GetResult();

            var result = new JObject[raw.Length];

            for (int index = 0; index < result.Length; index++)
            {
                result[index] = JObject.Parse(raw[index]);
            }

            return result;
        }

        public static string[] GetCappersList(JObject[] profiles)
        {
            List<string> result = new List<string>();
            for (int index = 0; index < profiles.Length; index++)
            {
                var code = CheckFeed(profiles[index]);
                if (code == CAPPED_CODE)
                {
                    result.Add(profiles[index].GetValue("name").ToString());
                }
            }
            return result.ToArray();
        }

        public static string[] GetAchievements(JObject[] profiles)
        {
            var result = new List<string>();
            Directory.CreateDirectory($"{Directory.GetCurrentDirectory()}/achievements");
            foreach(var profile in profiles)
            {
                if(NO_ERROR == CheckError(profile))
                {
                    var name = profile["name"].ToString();
                    JObject achievements;
                    if (File.Exists($"{Directory.GetCurrentDirectory()}/achievements/{name}.json"))
                    {
                        achievements = JObject.Parse(File.ReadAllText($"{Directory.GetCurrentDirectory()}/achievements/{name}.json"));
                    }
                    else
                    {
                        achievements = JObject.Parse(defaultAchievements);
                    }
                    bool dirty = false;
                    int total = (int)(profile["totalxp"].ToObject<uint>() / 25000000 * 25000000 / 1000000);
                    if(total > achievements["total"].ToObject<int>())
                    {
                        achievements["total"] = total;
                        var str = total.ToString();
                        if(str.Length == 4)
                        {
                            result.Add(string.Format(achievementStrings[0], name, str.Insert(1, ".") + "B"));
                        }
                        else
                            result.Add(string.Format(achievementStrings[0], name, str+"M"));
                        dirty = true;
                    }
                    var activitiesArrayToken = profile["activities"] as JArray;
                    foreach(var activityToken in activitiesArrayToken)
                    {
                        var activity = activityToken as JObject;
                        var text = activity["text"].ToString();
                        if (text.StartsWith("I found ") && text.EndsWith("pet."))
                        {
                            var petText = text.Replace("I found ", "");
                            petText = petText[0..^1];
                            if (!achievements["pets"].ToObject<string[]>().Contains(petText))
                            {
                                result.Add(string.Format(achievementStrings[1], name, petText));
                                (achievements["pets"] as JArray).Add(petText);
                                dirty = true;
                            }
                        }
                    }
                    foreach(var skillToken in profile["skillvalues"])
                    {
                        var skill = skillToken as JObject;
                        var id = skill["id"].ToObject<int>();
                        var level = skill["level"].ToObject<int>();
                        var xp = skill["xp"].ToObject<int>() / 10;
                        var achArray = achievements["skills"] as JArray;
                        if(!achArray[id]["99"].ToObject<bool>() && level >= 99)
                        {
                            achArray[id]["99"] = true;
                            result.Add(string.Format(achievementStrings[2], name, skillNames[id]));
                            dirty = true;
                        }
                        if(!achArray[id]["120"].ToObject<bool>() && (level >= 120 || xp >= level120))
                        {
                            achArray[id]["120"] = true;
                            result.Add(string.Format(achievementStrings[3], name, skillNames[id]));
                            dirty = true;
                        }
                        var last = achArray[id]["last"].ToObject<int>();
                        var current = xp / 10000000 * 10;
                        if(current > last)
                        {
                            achArray[id]["last"] = current;
                            result.Add(string.Format(achievementStrings[4], name, current, skillNames[id]));
                            dirty = true;
                        }

                    }
                    if (dirty)
                        File.WriteAllText($"{Directory.GetCurrentDirectory()}/achievements/{name}.json", achievements.ToString());
                }
            }
            return result.ToArray();
        }

        public static MemberData[] ParseMemberData(string data)
        {
            var result = new List<MemberData>();
            using var reader = new StringReader(data);
            for (string line = reader.ReadLine(); (line = reader.ReadLine()) != null;)
            {
                var split = line.Split(",");
                if (split.Length != 4)
                    continue;
                result.Add(new MemberData
                {
                    Name = Fix(split[0]),
                    Rank = split[1],
                    ClanXP = int.Parse(split[2]),
                    ClanKills = int.Parse(split[3])
                });
            }
            return result.ToArray();
        }

        public static string[] GetClanList(HttpClient client)
        {
            List<string> result = new List<string>();
            try
            {
                using var reader = new StringReader(client.GetStringAsync($"http://services.runescape.com/m=clan-hiscores/members_lite.ws?clanName={CLAN_NAME}").GetAwaiter().GetResult());
                for (string line = reader.ReadLine(); (line = reader.ReadLine()) != null;)
                {
                    result.Add(Fix(line.Split(",")[0]));
                }
            }
            catch (Exception)
            {
                result.Clear();
            }
            return result.ToArray();
        }

        public static int CheckError(JObject json)
        {
            try
            {
                if (json.ContainsKey("error"))
                {
                    var error = json["error"].ToString();
                    if (error == "PROFILE_PRIVATE")
                    {
                        return PROFILE_PRIVATE_CODE;
                    }

                    if (error == "NO_PROFILE")
                    {
                        return NO_PROFILE_CODE;
                    }

                    if (error == "NOT_A_MEMBER")
                    {
                        return NOT_A_MEMBER_CODE;
                    }

                    return EXCEPTION_CODE;
                }
                return NO_ERROR;
            }
            catch (Exception)
            {
                return EXCEPTION_CODE;
            }
        }

        public static int CheckFeed(JObject json)
        {
            var code = CheckError(json);
            if(code == NO_ERROR)
            {

                var activities = json["activities"] as JArray;
                foreach (var activity in activities)
                {
                    var date = TimeZoneInfo.ConvertTimeToUtc(DateTime.Parse(activity["date"].ToString()), _timeZone);
                    if (activity["text"].ToString() == "Capped at my Clan Citadel." && date >= Program.PreviousResetDate)
                    {
                        return CAPPED_CODE;
                    }
                }
            }
            return code;
        }

        private static string Fix(string value)
        {
            var builder = new StringBuilder(value.Length);
            foreach (var character in value)
            {
                if (char.IsLetterOrDigit(character) || character == '_')
                {
                    builder.Append(character);
                }
                else
                {
                    builder.Append(' ');
                }
            }
            return builder.ToString();
        }

        static Downloader()
        {
            _timeZone = TimeZoneInfo.FindSystemTimeZoneById("GMT Standard Time");

            var json = new JObject
            {
                ["total"] = 0,
                ["pets"] = new JArray(),
                ["skills"] = new JArray()
            };

            for(int index = 0; index < skillNames.Length; index++)
            {
                ((JArray)json["skills"]).Add(new JObject
                {
                    ["99"] = false,
                    ["120"] = false,
                    ["last"] = 0
                });
            }
            defaultAchievements = json.ToString();
        }
    }
}
