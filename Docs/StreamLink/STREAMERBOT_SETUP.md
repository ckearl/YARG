# StreamLink: Streamer.bot, TikFinity and OBS setup

How to let viewers trigger in-game effects (speed changes, All Taps, Shuffle, ...) from Twitch, YouTube, Kick and
TikTok. Everything platform-related happens in **Streamer.bot**; the game only talks to Streamer.bot over a local
WebSocket and never sees your platform logins.

```
Twitch / YouTube / Kick ──► Streamer.bot ◄── TikFinity ◄── TikTok LIVE
                              │  "Mod: ..." action → CPH.WebsocketBroadcastJson(command)
                              ▼  ws://127.0.0.1:8080 (password)
                            Game ── runs "StreamLink Ack" action with the result ──► Streamer.bot
                                                                     (refund / complete redeem, chat reply)
```

The two C# scripts referenced below are in [`streamerbot/`](streamerbot/).

---

## 1. Streamer.bot WebSocket server

1. **Servers/Clients → WebSocket Server**.
2. Address **`127.0.0.1`**, port **`8080`**. Don't use `0.0.0.0`: localhost keeps it off your network and avoids a
   Windows Firewall prompt.
3. Turn **Authentication** on and set a password.
4. Turn the server on (and **Auto Start** so it's on every time).

## 2. Game settings

In the game: **Settings → General → StreamLink**.

| Setting | Value |
|---|---|
| Stream Mode | On (default) |
| Streamer.bot Address | `127.0.0.1` |
| Streamer.bot Port | `8080` |
| Default Effect Duration | used when an action doesn't set one |

The **password** isn't in the menu yet. Close the game and edit `settings.json`:

- Editor / development builds: `%USERPROFILE%\AppData\LocalLow\YARC\YARG\dev\settings.json`
- Release builds: `...\release\settings.json` (the `YARC\YARG` part changes once the build is renamed)

```json
"StreamLinkPassword": "your-streamer-bot-password",
```

The dot in the bottom-left corner of the game shows the link: **green** connected, **amber** connecting,
**red** wrong password, **grey** off.

## 3. The "StreamLink Ack" action (create this first)

The game runs this action by name after every command. It refunds Twitch redeems that couldn't be applied,
completes the ones that were, and optionally replies in chat.

1. **Actions → Add**, name it exactly **`StreamLink Ack`**. No trigger.
2. **Sub-Actions → Core → C# → Execute C# Code**, paste [`StreamLinkAck.cs`](streamerbot/StreamLinkAck.cs),
   **Compile**, **Save and Compile**.
3. Optional: change `ANNOUNCE_APPLIED` / `ANNOUNCE_REJECTED` at the top.

Arguments the game passes: `cmdId`, `status` (`applied` / `rejected` / `expired`), `reason`, `mod`, `user`,
`platform`, `rewardId`, `redemptionId`, `endsInMs`.

## 4. One action per purchasable effect

For each effect you want to sell:

1. **Actions → Add**, e.g. **`Mod: Speed Up`**.
2. Add **Execute C# Code** with [`StreamLinkSend.cs`](streamerbot/StreamLinkSend.cs) and change the settings block:

   | Action | `CMD` | `MOD` | `PARAMS` |
   |---|---|---|---|
   | Mod: Speed Up | `apply` | `speed` | `{"value":1.25}` (0.60–1.50) |
   | Mod: Slow Down | `apply` | `speed` | `{"value":0.75}` |
   | Mod: All Taps | `apply` | `allTaps` | |
   | Mod: All HOPOs | `apply` | `allHopos` | |
   | Mod: All Strums | `apply` | `allStrums` | |
   | Mod: Shuffle | `apply` | `shuffle` | (optional `{"seed":123}`) |
   | Mod: Mirror | `apply` | `mirror` | |
   | StreamLink Clear | `clear` | | |
   | StreamLink Ping | `ping` | | |

   `DURATION_MS` sets how long it lasts (5000–120000).
3. Add triggers (step 5).

Rules the game enforces regardless of price: one effect per group (speed / note type / lanes; a new one replaces
the old), at most one speed change every 3 seconds, nothing in menus, practice, replays or the last 5 seconds of a
song, and only for a single guitar/bass player. Rejected commands are refunded by the Ack action.

**Prices and cooldowns live in Streamer.bot**, not in the game: set them on the reward or trigger.

## 5. Triggers

| Platform | Trigger | Notes |
|---|---|---|
| Twitch channel points | **Twitch → Channel Reward → Reward Redemption** | Create the reward **in Streamer.bot** (Platforms → Twitch → Channel Point Rewards → Add), not on the Twitch dashboard, and leave **Skip reward requests queue** off. Twitch only lets the app that created a reward refund or complete its redemptions. |
| Twitch bits | **Twitch → Chat → Cheer** | Use the Range setting for a minimum amount. |
| Twitch subs | The Twitch subscription, resub and gift sub triggers | |
| YouTube | **YouTube → Chat → Super Chat** | Range on the amount. |
| Kick | Kick event triggers | |
| TikTok | TikFinity (step 6) | |

## 6. TikFinity (TikTok)

1. TikFinity desktop app → **Setup → Streamer.bot Connection** → connect to the same WebSocket server → **Test**.
2. Map gifts to the `Mod: ...` actions.
3. In each action used by TikFinity, add a **Set Argument** sub-action *before* the C# one: name `platform`,
   value `tiktok` (so the game shows a TikTok badge).

TikTok support relies on an unofficial API and can break without notice; treat it as best effort.

## 7. Test it before going live

1. Start the game with Streamer.bot running. The corner dot should turn green.
2. Run **StreamLink Ping** from Streamer.bot (right-click → Test Trigger). The Ack action should run with
   `status = applied` (check **Action History**).
3. Start a song on guitar, run **Mod: Mirror** with Test Trigger, and check that the banner shows and the far end of
   the highway changes.
4. Run it again from the menu: it should be rejected with `not_in_song`.
5. On an unlisted/test stream, redeem a real reward once while in a song (completed) and once in the menu
   (refunded).

In editor and development builds you can also trigger effects without Streamer.bot: **Ctrl+F5** speed 1.25,
**Ctrl+F6** speed 0.75, **Ctrl+F7/F8/F9** taps/HOPOs/strums, **Ctrl+F10** shuffle, **Ctrl+F11** mirror,
**Ctrl+F12** clear.

## 8. OBS

- Capture the game with **Game Capture**; the StreamLink banner and effect list are part of the game image.
- Capture the game's sound with an **Application Audio Capture** source on its **own audio track**.
- To keep copyrighted music out of Twitch VODs, use the **Twitch VOD Track** option
  (Settings → Output → Streaming, in OBS versions that have it) and point it at a track **without** the game audio.
  Check the exact option name in your OBS version.

## Still to confirm on the real setup

These come from Streamer.bot's documentation but haven't been tried against a live Streamer.bot yet:

- Whether `CPH.WebsocketBroadcastJson` arrives raw or wrapped in a `General.Custom` event. The game accepts both.
- That the arguments of the game's `DoAction` request show up as action arguments in **StreamLink Ack**.
- The exact viewer-name argument on each trigger (the script tries `user`, `userName`, `username`).
- That `bits` is the Cheer trigger's amount argument.
