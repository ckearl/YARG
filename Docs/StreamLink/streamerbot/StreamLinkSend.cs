// Streamer.bot inline C# for one purchasable effect ("Mod: ..." action).
// Copy this into an Execute C# Code sub-action and change the four settings below.
// Every Mod action uses the same code; only the settings differ.
//
// Examples:
//   Mod: Speed Up     CMD = "apply"  MOD = "speed"     PARAMS = "{\"value\":1.25}"
//   Mod: Slow Down    CMD = "apply"  MOD = "speed"     PARAMS = "{\"value\":0.75}"
//   Mod: All Taps     CMD = "apply"  MOD = "allTaps"
//   Mod: All HOPOs    CMD = "apply"  MOD = "allHopos"
//   Mod: All Strums   CMD = "apply"  MOD = "allStrums"
//   Mod: Shuffle      CMD = "apply"  MOD = "shuffle"
//   Mod: Mirror       CMD = "apply"  MOD = "mirror"
//   StreamLink Clear  CMD = "clear"  (ends every effect)
//   StreamLink Ping   CMD = "ping"   (connection test; the game always answers)
//
// To force the platform shown in the game (e.g. for TikFinity gifts), add a "Set Argument" sub-action
// before this one: name "platform", value "tiktok".

using System;
using Newtonsoft.Json.Linq;

public class CPHInline
{
    // ---- Settings ----
    const string CMD = "apply";
    const string MOD = "speed";
    const string PARAMS = "{\"value\":1.25}";   // "" for effects without parameters
    const int DURATION_MS = 20000;              // 5000 - 120000
    // -------------------

    public bool Execute()
    {
        var command = new JObject
        {
            ["v"] = 1,
            ["id"] = Guid.NewGuid().ToString(),
            ["cmd"] = CMD,
        };

        if (CMD == "apply")
        {
            command["mod"] = MOD;
            command["durationMs"] = DURATION_MS;
            if (!string.IsNullOrEmpty(PARAMS))
            {
                command["params"] = JObject.Parse(PARAMS);
            }
        }

        command["source"] = new JObject
        {
            ["platform"] = Platform(),
            ["user"] = FirstArg("user", "userName", "username"),
            ["trigger"] = FirstArg("rewardName", "actionName"),
            ["amount"] = Amount(),
            ["rewardId"] = FirstArg("rewardId"),
            ["redemptionId"] = FirstArg("redemptionId"),
        };

        CPH.WebsocketBroadcastJson(command.ToString(Newtonsoft.Json.Formatting.None));
        return true;
    }

    // "eventSource" is the platform (Twitch, YouTube, Kick, ...). Note: "__source" is the event *type*
    // (e.g. TwitchRewardRedemption), not the platform.
    string Platform()
    {
        string forced = FirstArg("platform");
        if (forced != "")
        {
            return forced.ToLowerInvariant();
        }

        object source;
        string name = CPH.TryGetArg("eventSource", out source) && source != null
            ? source.ToString().ToLowerInvariant()
            : "";

        if (name.Contains("twitch")) return "twitch";
        if (name.Contains("youtube")) return "youtube";
        if (name.Contains("kick")) return "kick";
        if (name.Contains("tiktok")) return "tiktok";
        return name == "" ? "unknown" : name;
    }

    // Bits for cheers; Super Chat amounts are in micros of the currency
    double Amount()
    {
        string bits = FirstArg("bits");
        double value;
        if (bits != "" && double.TryParse(bits, out value)) return value;

        string micros = FirstArg("microAmount");
        if (micros != "" && double.TryParse(micros, out value)) return value / 1000000.0;

        return 0;
    }

    string FirstArg(params string[] names)
    {
        foreach (var name in names)
        {
            object value;
            if (CPH.TryGetArg(name, out value) && value != null && value.ToString() != "")
            {
                return value.ToString();
            }
        }

        return "";
    }
}
