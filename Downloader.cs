using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace Citadel
{
    public static class Downloader
    {
        public static readonly int CAPPED_CODE = 1;
        public static readonly int NOT_CAPPED_CODE = 0;
        public static readonly int EXCEPTION_CODE = -1;
        public static readonly int NO_PROFILE_CODE = -2;
        public static readonly int NOT_A_MEMBER_CODE = -3;
        public static readonly int PROFILE_PRIVATE_CODE = -4;

        public static readonly string CLAN_NAME = "Kingdom of Ashdale";

        private static TimeZoneInfo _timeZone;

        private static JObject[] GetAlogs(HttpClient client, string[] clanList)
        {
            var names = clanList.ToList();

            var tasks = new List<Task<string>>();

            while(names.Count > 0)
            {
                var count = Math.Min(20, names.Count);

                var range = names.GetRange(0, count);
                range.ForEach((name) => tasks.Add(client.GetStringAsync($"https://apps.runescape.com/runemetrics/profile/profile?user={name}&activities=20")));
                names.RemoveRange(0, count);
                Task.WhenAll(tasks).GetAwaiter().GetResult();
            }

            var raw = Task.WhenAll(tasks).GetAwaiter().GetResult();

            var result = new JObject[raw.Length];

            for(int index = 0; index < result.Length; index++)
            {
                result[index] = JObject.Parse(raw[index]);
            }

            return result;
        }

        public static string[] GetCappersList(HttpClient client)
        {
            List<string> result = new List<string>();
            var members = GetClanList(client);
            var alogs = GetAlogs(client, members);
            for(int index = 0; index < members.Length; index++)
            {
                var code = CheckFeed(alogs[index]);
                if (code == CAPPED_CODE)
                {
                    result.Add(members[index]);
                }
            }
            return result.ToArray();
        }

        public static string[] GetClanList(HttpClient client)
        {
            List<string> result = new List<string>();
            try
            {
                using (var reader = new StringReader(client.GetStringAsync($"http://services.runescape.com/m=clan-hiscores/members_lite.ws?clanName={CLAN_NAME}").GetAwaiter().GetResult()))
                {
                    for (string line = reader.ReadLine(); (line = reader.ReadLine()) != null;)
                    {
                        result.Add(Fix(line.Split(",")[0]));
                    }
                }
            }
            catch (Exception)
            {
                result.Clear();
            }
            return result.ToArray();
        }

        public static int CheckFeed(JObject json)
        {
            try
            {
                if (json.ContainsKey("error"))
                {
                    var error = json["error"].ToString();
                    if (error == "PROFILE_PRIVATE")
                        return PROFILE_PRIVATE_CODE;
                    if (error == "NO_PROFILE")
                        return NO_PROFILE_CODE;
                    if (error == "NOT_A_MEMBER")
                        return NOT_A_MEMBER_CODE;
                    return EXCEPTION_CODE;
                }
                else
                {
                    var activities = json["activities"] as JArray;
                    foreach(var activity in activities)
                    {
                        var date = TimeZoneInfo.ConvertTimeToUtc(DateTime.Parse(activity["date"].ToString()), _timeZone);
                        if (activity["text"].ToString() == "Capped at my Clan Citadel." && date >= Program.PreviousResetDate)
                        {
                            return CAPPED_CODE;
                        }
                    }
                    return NOT_CAPPED_CODE;
                }
            }
            catch (Exception)
            {
                return EXCEPTION_CODE;
            }
        }

        private static string Fix(string value)
        {
            var builder = new StringBuilder(value.Length);
            foreach(var character in value)
            {
                if (char.IsLetterOrDigit(character) || character == '_')
                    builder.Append(character);
                else
                    builder.Append(' ');
            }
            return builder.ToString();
        }

        static Downloader()
        {
            _timeZone = TimeZoneInfo.FindSystemTimeZoneById("GMT Standard Time");
        }
    }
}
