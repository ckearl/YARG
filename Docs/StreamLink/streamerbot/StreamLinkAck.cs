// Streamer.bot inline C# for the "StreamLink Ack" action.
// The game runs this action (by name, over the WebSocket server) after every command, with these arguments:
//   cmdId, status (applied | rejected | expired), reason, mod, user, platform, rewardId, redemptionId, endsInMs
//
// For Twitch channel point redeems it completes the redemption when the effect was applied and refunds it when
// the game rejected it. This only works for rewards created by Streamer.bot (Twitch only lets the app that made a
// reward change its redemptions), with "Skip reward requests queue" turned off.

using System;

public class CPHInline
{
    // ---- Settings ----
    const bool ANNOUNCE_APPLIED = false;   // chat message for every applied effect
    const bool ANNOUNCE_REJECTED = true;   // chat message when an effect couldn't be applied
    // -------------------

    public bool Execute()
    {
        string status = Arg("status");
        string reason = Arg("reason");
        string mod = Arg("mod");
        string user = Arg("user");
        string platform = Arg("platform");
        string rewardId = Arg("rewardId");
        string redemptionId = Arg("redemptionId");

        if (status == "expired")
        {
            return true;
        }

        if (platform == "twitch" && rewardId != "" && redemptionId != "")
        {
            bool ok = status == "applied"
                ? CPH.TwitchRedemptionFulfill(rewardId, redemptionId)
                : CPH.TwitchRedemptionCancel(rewardId, redemptionId);
            if (!ok)
            {
                CPH.LogWarn("StreamLink: couldn't update redemption " + redemptionId + " (" + status + ")");
            }
        }

        string mention = user == "" ? "" : "@" + user + " ";
        if (status == "applied" && ANNOUNCE_APPLIED)
        {
            Say(platform, mention + Pretty(mod) + " is live!");
        }
        else if (status == "rejected" && ANNOUNCE_REJECTED && reason != "duplicate")
        {
            string refund = platform == "twitch" && redemptionId != "" ? " Your points were refunded." : "";
            Say(platform, mention + Pretty(mod) + " didn't go through: " + Explain(reason) + "." + refund);
        }

        return true;
    }

    void Say(string platform, string message)
    {
        switch (platform)
        {
            case "twitch":  CPH.SendMessage(message, true, true); break;
            case "youtube": CPH.SendYouTubeMessage(message); break;
            case "kick":    CPH.SendKickMessage(message, true, true); break;
            // TikTok has no chat method in Streamer.bot; TikFinity can show alerts instead
        }
    }

    static string Explain(string reason)
    {
        switch (reason)
        {
            case "not_in_song":            return "no song is playing right now";
            case "unsupported_instrument": return "it only works on guitar or bass";
            case "rate_limited":           return "speed was changed too recently, try again in a few seconds";
            case "song_ending":            return "the song is about to end";
            case "invalid_params":         return "the effect is set up wrong (streamer: check the action)";
            default:                       return "something went wrong";
        }
    }

    static string Pretty(string mod)
    {
        switch (mod)
        {
            case "speed":     return "Speed change";
            case "allTaps":   return "All Taps";
            case "allHopos":  return "All HOPOs";
            case "allStrums": return "All Strums";
            case "shuffle":   return "Shuffle";
            case "mirror":    return "Mirror";
            default:          return "Your effect";
        }
    }

    string Arg(string name)
    {
        object value;
        return CPH.TryGetArg(name, out value) && value != null ? value.ToString() : "";
    }
}
