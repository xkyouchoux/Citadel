using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Threading.Tasks;
using NodaTime;

namespace Citadel
{
    public static class Downloader
    {
        public struct MemberData
        {
            public string Name;
            public string Rank;
            public long ClanXP;
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

        private static readonly DateTimeZone _timeZone;

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
            ":large_orange_diamond: **{0}** just joined the **{1} XP club!**\n", //total xp
            ":dove: **{0}** found a wild **{1}!**\n", //pet
            ":medal: **{0}** can now wear the **{1} cape!**\n", //99
            ":crown: **{0}** Nulyf'd 120 in **{1}!**\n", //120
            ":large_blue_diamond: **{0}** is overcompensating with **{1}!**\n", //20m xp increments
            ":purple_circle: **{0}** can now join the **{1} server!**\n" //total level
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
                if (!result[index].ContainsKey("name"))
                    result[index]["name"] = members[index];
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

        public static string[] GetItems(HttpClient client, JObject[] profiles)
        {
            var result = new List<string>();
            Directory.CreateDirectory($"{Program.BASE_DIR}/items");
            foreach(var profile in profiles)
            {
                var name = profile["name"].ToString();
                var code = CheckError(profile);
                if(code == NO_ERROR)
                {
                    JObject items;
                    bool exists = false;
                    if (File.Exists($"{Program.BASE_DIR}/items/{name}.json"))
                    {
                        items = JObject.Parse(File.ReadAllText($"{Program.BASE_DIR}/items/{name}.json"));
                        exists = true;
                    }
                    else
                    {
                        items = new JObject();
                    }
                    bool dirty = false;
                    var activitiesArrayToken = profile["activities"] as JArray;
                    foreach (var activityToken in activitiesArrayToken)
                    {
                        var activity = activityToken as JObject;
                        var text = activity["text"].ToString();
                        if (text.StartsWith("I found ") && !text.EndsWith("pet."))
                        {
                            var itemText = text.Replace("I found ", "");
                            if(itemText.EndsWith('.'))
                            {
                                itemText = itemText[0..^1];
                            }
                            var date = ConvertDateStringToTime(activity["date"].ToString());
                            if(items.ContainsKey(itemText))
                            {
                                var array = items[itemText] as JArray;
                                var list = array.ToObject<List<string>>();
                                if (!list.Contains(date.ToString()))
                                {
                                    array.Add(date.ToString());
                                    list.Add(date.ToString());
                                    if(exists && Program.CheckItemBlacklist(itemText))
                                        result.Add(string.Format(Program.DEFAULT_ITEM_FOUND_MESSAGE, name, itemText));
                                    dirty = true;
                                }
                            }
                            else
                            {
                                var array = new JArray();
                                array.Add(date.ToString());
                                if(exists && Program.CheckItemBlacklist(itemText))
                                    result.Add(string.Format(Program.DEFAULT_ITEM_FOUND_MESSAGE, name, itemText));
                                dirty = true;
                                items[itemText] = array;
                            }
                        }
                    }
                    if(dirty)
                    {
                        File.WriteAllText($"{Program.BASE_DIR}/items/{name}.json", items.ToString());
                    }
                }
            }
            return result.ToArray();
        }

        public static string[] GetAchievements(HttpClient client, JObject[] profiles)
        {
            var result = new List<string>();
            Directory.CreateDirectory($"{Program.BASE_DIR}/achievements");
            foreach(var profile in profiles)
            {
                var name = profile["name"].ToString();
                var code = CheckError(profile);
                if(code == PROFILE_PRIVATE_CODE)
                {
                    try
                    {
                        using var reader = new StringReader(client.GetStringAsync($"https://secure.runescape.com/m=hiscore/index_lite.ws?player={name}").GetAwaiter().GetResult());
                        profile["activities"] = new JArray();
                        var totalLine = reader.ReadLine().Split(",");
                        profile["totalskill"] = int.Parse(totalLine[1]);
                        profile["totalxp"] = uint.Parse(totalLine[2]);
                        var skillvalues = (profile["skillvalues"] = new JArray()) as JArray;
                        var index = 0;
                        for (string line; (line = reader.ReadLine()) != null;)
                        {
                            var split = line.Split(",");
                            if (split.Length == 3)
                            {
                                skillvalues.Add(new JObject
                                {
                                    ["level"] = int.Parse(split[1]),
                                    ["xp"] = int.Parse(split[2]) * 10,
                                    ["id"] = index
                                });
                                index++;
                            }
                        }
                        profile.Remove("error");
                        code = NO_ERROR;
                    }
                    catch { }
                }
                if (code == NO_ERROR)
                {
                    JObject achievements;
                    bool exists = false;
                    if (File.Exists($"{Program.BASE_DIR}/achievements/{name}.json"))
                    {
                        achievements = JObject.Parse(File.ReadAllText($"{Program.BASE_DIR}/achievements/{name}.json"));
                        exists = true;
                    }
                    else
                    {
                        achievements = JObject.Parse(defaultAchievements);
                    }
                    bool dirty = false;

                    int totalskill = profile["totalskill"].ToObject<int>() / 500 * 500;

                    if (achievements.ContainsKey("totalskill") && totalskill >= 1000 && totalskill > achievements["totalskill"].ToObject<int>())
                    {
                        achievements["totalskill"] = totalskill;
                        if (exists)
                        {
                            result.Add(string.Format(achievementStrings[5], name, totalskill));
                        }
                        dirty = true;
                    }
                    else
                    {
                        achievements["totalskill"] = totalskill;
                        dirty = true;
                    }

                    int total = (int)(profile["totalxp"].ToObject<uint>() / 25000000 * 25000000 / 1000000);
                    if (total % 100 == 0 && total > achievements["total"].ToObject<int>())
                    {
                        achievements["total"] = total;
                        var str = total.ToString();
                        if (exists)
                        {
                            if (str.Length == 4)
                            {
                                result.Add(string.Format(achievementStrings[0], name, str.Insert(1, ".") + "B"));
                            }
                            else
                                result.Add(string.Format(achievementStrings[0], name, str + "M"));
                        }
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
                                if(exists)
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
                            if(exists)
                                result.Add(string.Format(achievementStrings[2], name, skillNames[id]));
                            dirty = true;
                        }
                        if(!achArray[id]["120"].ToObject<bool>() && (level >= 120 || xp >= level120))
                        {
                            achArray[id]["120"] = true;
                            if(exists)
                                result.Add(string.Format(achievementStrings[3], name, skillNames[id]));
                            dirty = true;
                        }
                        var last = achArray[id]["last"].ToObject<int>();
                        var current = xp / 10000000 * 10;
                        if(current % 50 == 0 && current > last)
                        {
                            achArray[id]["last"] = current;
                            if(exists)
                                result.Add(string.Format(achievementStrings[4], name, $"{current}M XP in {skillNames[id]}"));
                            dirty = true;
                        }

                    }
                    if (dirty)
                        File.WriteAllText($"{Program.BASE_DIR}/achievements/{name}.json", achievements.ToString());
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
                try
                {

                    result.Add(new MemberData
                    {
                        Name = Fix(split[0]),
                        Rank = split[1],
                        ClanXP = long.Parse(split[2]),
                        ClanKills = int.Parse(split[3])
                    });
                }
                catch
                {
                    Console.WriteLine(string.Join(", ", split));
                }
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

        private static DateTime ConvertDateStringToTime(string str)
        {
            return _timeZone.AtStrictly(LocalDateTime.FromDateTime(DateTime.Parse(str))).ToDateTimeUtc();
        }

        public static int CheckFeed(JObject json)
        {
            var code = CheckError(json);
            if(code == NO_ERROR)
            {

                var activities = json["activities"] as JArray;
                foreach (var activity in activities)
                {
                    var dateTime = ConvertDateStringToTime(activity["date"].ToString());
                    if (activity["text"].ToString() == "Capped at my Clan Citadel." && dateTime >= Program.PreviousResetDate)
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
            _timeZone = DateTimeZoneProviders.Tzdb.GetZoneOrNull("Etc/GMT");

            var json = new JObject
            {
                ["total"] = 0,
                ["totalskill"] = 0,
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
