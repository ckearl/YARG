using System;
using UnityEngine;
using UnityEngine.InputSystem;
using YARG.Core.Logging;
using YARG.Core.StreamLink;
using YARG.Gameplay;
using YARG.Settings;

namespace YARG.StreamLink
{
    /// <summary>
    /// Owns the Streamer.bot connection and the <see cref="StreamDirector"/> for the whole session, so commands
    /// get an answer in menus too. Created automatically and kept across scene loads.
    /// </summary>
    public class StreamLinkService : MonoSingleton<StreamLinkService>
    {
        private const float BANNER_SECONDS = 3f;

        private readonly GameplayStreamHost _host = new();
        private StreamDirector   _director;
        private StreamLinkClient _client;

        private GameManager _game;
        private int    _lastTickFrame = -1;
        private double _lastTickTime;

        private string _banner;
        private float  _bannerUntil;
        private int    _debugCommandId;

        public StreamDirector Director => _director;
        public LinkStatus LinkStatus => _client?.Status ?? LinkStatus.Disconnected;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Create()
        {
            var obj = new GameObject("StreamLink");
            DontDestroyOnLoad(obj);
            obj.AddComponent<StreamLinkService>();
        }

        public static bool IsStreamModeActive(GameManager game)
        {
            return SettingsManager.SettingContainer.IsInitialized &&
                SettingsManager.Settings.StreamMode.Value &&
                !game.IsPractice &&
                game.ReplayInfo == null;
        }

        protected override void SingletonAwake()
        {
            _director = new StreamDirector(_host);
            _director.EffectApplied += OnEffectApplied;
            _lastTickTime = Time.realtimeSinceStartupAsDouble;
        }

        protected override void SingletonDestroy()
        {
            _client?.Dispose();
            _client = null;
        }

        public void RestartClient()
        {
            _client?.Dispose();
            _client = null;
            // Started again on the next Update
        }

        private void StartClient()
        {
            var settings = SettingsManager.Settings;
            _client = new StreamLinkClient(new StreamLinkSettings
            {
                Host = settings.StreamLinkHost.Value,
                Port = settings.StreamLinkPort.Value,
                Password = settings.StreamLinkPassword.Value,
            }, _director.Enqueue);
            _host.Client = _client;
            _client.Start();
        }

        // Song lifecycle, called by GameManager

        public void OnSongStart(GameManager game)
        {
            _game = game;
            _host.Attach(game);
            _director.OnSongStart();
            YargLogger.LogFormatInfo("[StreamLink] Song started, effects {0}",
                _host.IsSupportedInstrument && !_host.IsPracticeOrReplay ? "enabled" : "disabled");
        }

        public void OnSongEnd(GameManager game)
        {
            if (game != _game)
            {
                return;
            }

            // Detach first: the game is being torn down, so there's no speed to reset
            _host.Detach();
            _director.OnSongEnd();
            _game = null;
        }

        public void OnPauseChanged(GameManager game, bool paused)
        {
            if (game == _game)
            {
                _director.SetPaused(paused);
            }
        }

        /// <summary>
        /// Called by GameManager each unpaused frame, between the song runner and the player updates.
        /// </summary>
        public void GameplayTick(GameManager game)
        {
            if (game == _game)
            {
                Tick();
            }
        }

        private void Tick()
        {
            double now = Time.realtimeSinceStartupAsDouble;
            _director.Tick(now - _lastTickTime);
            _lastTickTime = now;
            _lastTickFrame = Time.frameCount;
        }

        private void Update()
        {
            if (!SettingsManager.SettingContainer.IsInitialized || SettingsManager.Settings == null)
            {
                return;
            }

            if (_client == null)
            {
                StartClient();
            }

            _director.DefaultDurationSeconds = SettingsManager.Settings.StreamEffectDuration.Value;

            if (_game != null)
            {
                // Covers pause paths that don't go through PauseCore (e.g. failing)
                _director.SetPaused(_game.Paused);
            }

            if (Application.isEditor || Debug.isDebugBuild)
            {
                HandleDebugHotkeys();
            }

            // GameManager ticks first while a song is playing (it runs earlier in the frame);
            // otherwise (menus, paused) tick here so commands still get answered
            if (_lastTickFrame != Time.frameCount)
            {
                Tick();
            }
        }

        private void OnEffectApplied(ActiveEffect effect)
        {
            string user = string.IsNullOrEmpty(effect.Source.User) ? "someone" : "@" + effect.Source.User;
            _banner = $"{user} → {effect.Describe()} ({effect.DurationSeconds:0}s)";
            _bannerUntil = Time.unscaledTime + BANNER_SECONDS;
        }

        // Debug hotkeys (editor and development builds): Ctrl + F5..F12.
        // They go through the same parser and director as real commands.
        private void HandleDebugHotkeys()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null || !keyboard.ctrlKey.isPressed)
            {
                return;
            }

            if (keyboard.f5Key.wasPressedThisFrame)  SendDebugCommand("speed", "\"params\":{\"value\":1.25},");
            if (keyboard.f6Key.wasPressedThisFrame)  SendDebugCommand("speed", "\"params\":{\"value\":0.75},");
            if (keyboard.f7Key.wasPressedThisFrame)  SendDebugCommand("allTaps");
            if (keyboard.f8Key.wasPressedThisFrame)  SendDebugCommand("allHopos");
            if (keyboard.f9Key.wasPressedThisFrame)  SendDebugCommand("allStrums");
            if (keyboard.f10Key.wasPressedThisFrame) SendDebugCommand("shuffle");
            if (keyboard.f11Key.wasPressedThisFrame) SendDebugCommand("mirror");
            if (keyboard.f12Key.wasPressedThisFrame) SendDebugCommand(null);
        }

        private void SendDebugCommand(string mod, string extra = "")
        {
            string id = $"debug-{Guid.NewGuid():N}";
            string json = mod == null
                ? $"{{\"v\":1,\"id\":\"{id}\",\"cmd\":\"clear\"}}"
                : $"{{\"v\":1,\"id\":\"{id}\",\"cmd\":\"apply\",\"mod\":\"{mod}\",{extra}" +
                  $"\"source\":{{\"platform\":\"test\",\"user\":\"Hotkey{++_debugCommandId}\",\"trigger\":\"debug\"}}}}";
            _director.Enqueue(StreamMessageParser.Parse(json));
        }

        // Temporary on-screen display until the proper HUD (M4): connection dot, banner and active effects
        private GUIStyle _style;

        private void OnGUI()
        {
            if (!SettingsManager.SettingContainer.IsInitialized || !SettingsManager.Settings.StreamMode.Value)
            {
                return;
            }

            _style ??= new GUIStyle(GUI.skin.label) { fontSize = 18, richText = true };

            string dot = LinkStatus switch
            {
                LinkStatus.Connected    => "<color=#3c3>●</color>",
                LinkStatus.AuthFailed   => "<color=#e33>●</color> auth failed",
                LinkStatus.Disconnected => "<color=#888>●</color>",
                _                       => "<color=#fb0>●</color> connecting",
            };

            var text = new System.Text.StringBuilder();
            text.Append(dot).Append(" StreamLink");

            if (_game != null)
            {
                foreach (var effect in _director.Effects.Active)
                {
                    text.Append($"\n  {effect.Describe()}  {effect.RemainingSeconds:0}s");
                }
            }

            GUI.Label(new Rect(12, 12, 600, 200), text.ToString(), _style);

            if (_banner != null && Time.unscaledTime < _bannerUntil)
            {
                var bannerStyle = new GUIStyle(_style) { fontSize = 28, alignment = TextAnchor.UpperCenter };
                GUI.Label(new Rect(0, 60, Screen.width, 60), $"<b>{_banner}</b>", bannerStyle);
            }
        }
    }
}
