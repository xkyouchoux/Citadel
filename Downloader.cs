using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;

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
        public static readonly string ACTIVITY_LINK = "https://apps.runescape.com/runemetrics/profile/profile?user={0}&activities=20";

        private static WebClient _client;
        private static TimeZoneInfo _timeZone;

        public static string[] GetCappersList()
        {
            List<string> result = new List<string>();
            string[] members = GetClanList();
            foreach(var member in members)
            {
                var code = CheckFeed(member);
                if(code == CAPPED_CODE)
                {
                    result.Add(member);
                }
            }
            return result.ToArray();
        }

        public static string[] GetClanList()
        {
            List<string> result = new List<string>();
            try
            {
                using (var reader = new StringReader(_client.DownloadString($"http://services.runescape.com/m=clan-hiscores/members_lite.ws?clanName={CLAN_NAME}")))
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

        public static int CheckFeed(string username)
        {
            try
            {
                var json = JObject.Parse(_client.DownloadString(string.Format(ACTIVITY_LINK, username)));
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
                if (char.IsLetterOrDigit(character))
                    builder.Append(character);
                else
                    builder.Append(' ');
            }
            return builder.ToString();
        }

        static Downloader()
        {
            _client = new WebClient();
            _timeZone = TimeZoneInfo.FindSystemTimeZoneById("GMT Standard Time");
        }
    }
}
