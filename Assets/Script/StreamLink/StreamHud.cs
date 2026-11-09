using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using YARG.Core.StreamLink;
using YARG.Settings;

namespace YARG.StreamLink
{
    /// <summary>
    /// The viewer-facing overlay: a banner when an effect is triggered, a list of active effects with countdown
    /// bars, and a connection indicator. Built in code on its own canvas so it doesn't depend on themes or prefabs.
    /// </summary>
    public class StreamHud : MonoBehaviour
    {
        private const float BANNER_IN       = 0.2f;
        private const float BANNER_HOLD     = 2.5f;
        private const float BANNER_OUT      = 0.3f;
        private const int   MAX_BANNER_QUEUE = 3;

        private const float ROW_WIDTH  = 340f;
        private const float ROW_HEIGHT = 46f;
        private const float ROW_GAP    = 8f;

        private static readonly Color PanelColor = new(0.06f, 0.06f, 0.09f, 0.82f);
        private static readonly Color BarTrack   = new(1f, 1f, 1f, 0.12f);

        private StreamLinkService _service;

        private TMP_FontAsset _font;
        private bool _fontIsFallback = true;
        private float _nextFontSearch;

        private RectTransform _root;

        // Banner
        private RectTransform   _banner;
        private CanvasGroup     _bannerGroup;
        private Image           _bannerAccent;
        private Image           _bannerBadge;
        private TextMeshProUGUI _bannerBadgeText;
        private TextMeshProUGUI _bannerText;
        private readonly Queue<ActiveEffect> _bannerQueue = new();
        private float _bannerTime = -1f;

        // Active effects
        private readonly List<EffectRow> _rows = new();

        // Connection
        private RectTransform   _connection;
        private Image           _connectionDot;
        private TextMeshProUGUI _connectionText;

        private sealed class EffectRow
        {
            public RectTransform   Root;
            public Image           Accent;
            public TextMeshProUGUI Name;
            public TextMeshProUGUI Time;
            public RectTransform   Bar;
            public Image           BarFill;
        }

        public void Initialize(StreamLinkService service)
        {
            _service = service;
            _service.Director.EffectApplied += OnEffectApplied;
            Build();
        }

        private void OnDestroy()
        {
            if (_service != null && _service.Director != null)
            {
                _service.Director.EffectApplied -= OnEffectApplied;
            }
        }

        private void OnEffectApplied(ActiveEffect effect)
        {
            _bannerQueue.Enqueue(effect);
            // A burst of redeems shouldn't back the banner up for half a minute; skip the oldest ones
            while (_bannerQueue.Count > MAX_BANNER_QUEUE)
            {
                _bannerQueue.Dequeue();
            }
        }

        private void Update()
        {
            bool visible = SettingsManager.SettingContainer.IsInitialized && SettingsManager.Settings.StreamMode.Value;
            _root.gameObject.SetActive(visible);
            if (!visible)
            {
                return;
            }

            // The UI's Barlow font is only in memory once a menu has used it, so keep looking (once a second)
            if (_fontIsFallback && Time.unscaledTime >= _nextFontSearch)
            {
                _nextFontSearch = Time.unscaledTime + 1f;
                TryUpgradeFont();
            }

            UpdateConnection();
            UpdateEffects();
            UpdateBanner();
        }

        private void UpdateConnection()
        {
            var (color, label) = _service.LinkStatus switch
            {
                LinkStatus.Connected    => (new Color(0.25f, 0.85f, 0.35f), "StreamLink"),
                LinkStatus.AuthFailed   => (new Color(0.95f, 0.25f, 0.25f), "StreamLink: wrong password"),
                LinkStatus.Disconnected => (new Color(0.5f, 0.5f, 0.5f), "StreamLink: off"),
                _                       => (new Color(1f, 0.7f, 0.1f), "StreamLink: connecting"),
            };

            _connectionDot.color = color;
            _connectionText.text = label;
        }

        private void UpdateEffects()
        {
            int i = 0;
            if (_service.IsInSong)
            {
                foreach (var effect in _service.Director.Effects.Active)
                {
                    if (i == _rows.Count)
                    {
                        _rows.Add(CreateRow(i));
                    }

                    var row = _rows[i++];
                    row.Root.gameObject.SetActive(true);
                    var color = GroupColor(effect.Group);
                    row.Accent.color = color;
                    row.BarFill.color = color;
                    row.Name.text = effect.Describe();
                    row.Time.text = $"{Mathf.CeilToInt((float) effect.RemainingSeconds)}s";

                    float fraction = effect.DurationSeconds > 0
                        ? Mathf.Clamp01((float) (effect.RemainingSeconds / effect.DurationSeconds))
                        : 0f;
                    row.Bar.anchorMax = new Vector2(fraction, 1f);
                }
            }

            for (; i < _rows.Count; i++)
            {
                _rows[i].Root.gameObject.SetActive(false);
            }
        }

        private void UpdateBanner()
        {
            if (_bannerTime < 0f)
            {
                if (!_bannerQueue.TryDequeue(out var effect))
                {
                    _banner.gameObject.SetActive(false);
                    return;
                }

                ShowBanner(effect);
                _bannerTime = 0f;
            }

            _bannerTime += Time.unscaledDeltaTime;

            float alpha;
            float offset;
            if (_bannerTime < BANNER_IN)
            {
                float t = Ease(_bannerTime / BANNER_IN);
                alpha = t;
                offset = (1f - t) * 30f;
            }
            else if (_bannerTime < BANNER_IN + BANNER_HOLD)
            {
                alpha = 1f;
                offset = 0f;
            }
            else
            {
                float t = Mathf.Clamp01((_bannerTime - BANNER_IN - BANNER_HOLD) / BANNER_OUT);
                alpha = 1f - t;
                offset = 0f;
            }

            _bannerGroup.alpha = alpha;
            _banner.anchoredPosition = new Vector2(0f, -90f + offset);

            if (_bannerTime >= BANNER_IN + BANNER_HOLD + BANNER_OUT)
            {
                _bannerTime = -1f;
            }
        }

        private void ShowBanner(ActiveEffect effect)
        {
            _banner.gameObject.SetActive(true);

            var (platformName, platformColor) = PlatformStyle(effect.Source.Platform);
            _bannerBadge.gameObject.SetActive(platformName != null);
            if (platformName != null)
            {
                _bannerBadge.color = platformColor;
                _bannerBadgeText.text = platformName;
            }

            _bannerAccent.color = GroupColor(effect.Group);

            string user = string.IsNullOrEmpty(effect.Source.User) ? "Someone" : "@" + effect.Source.User;
            string accent = ColorUtility.ToHtmlStringRGB(GroupColor(effect.Group));
            _bannerText.text =
                $"<b>{Escape(user)}</b>  →  <color=#{accent}><b>{effect.Describe()}</b></color>  " +
                $"<alpha=#AA>{effect.DurationSeconds:0}s";
        }

        // Layout

        private void Build()
        {
            _font = TMP_Settings.defaultFontAsset;
            TryUpgradeFont();

            var canvasObject = new GameObject("StreamHud", typeof(RectTransform));
            canvasObject.transform.SetParent(transform, false);
            var canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // Above gameplay, below YARG's toasts (1000)
            canvas.sortingOrder = 900;
            var scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            _root = (RectTransform) canvasObject.transform;

            BuildBanner();
            BuildConnection();
        }

        private void BuildBanner()
        {
            _banner = NewRect("Banner", _root);
            _banner.anchorMin = _banner.anchorMax = new Vector2(0.5f, 1f);
            _banner.pivot = new Vector2(0.5f, 1f);
            _banner.anchoredPosition = new Vector2(0f, -90f);
            _bannerGroup = _banner.gameObject.AddComponent<CanvasGroup>();
            AddImage(_banner, PanelColor);

            var layout = _banner.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(0, 26, 0, 0);
            layout.spacing = 16f;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;
            var fitter = _banner.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var accent = NewRect("Accent", _banner);
            _bannerAccent = AddImage(accent, Color.white);
            SetPreferred(accent, 8f, 64f);

            var badge = NewRect("Platform", _banner);
            _bannerBadge = AddImage(badge, Color.gray);
            var badgeLayout = badge.gameObject.AddComponent<HorizontalLayoutGroup>();
            badgeLayout.padding = new RectOffset(10, 10, 4, 4);
            badgeLayout.childControlWidth = badgeLayout.childControlHeight = true;
            _bannerBadgeText = AddText(NewRect("Text", badge), 18f, TextAlignmentOptions.Center);
            _bannerBadgeText.fontStyle = FontStyles.Bold;

            _bannerText = AddText(NewRect("Text", _banner), 30f, TextAlignmentOptions.Left);

            _banner.gameObject.SetActive(false);
        }

        private EffectRow CreateRow(int index)
        {
            var root = NewRect("Effect", _root);
            // Left of the (centered) highway, a little above the middle of the screen
            root.anchorMin = root.anchorMax = new Vector2(0f, 0.5f);
            root.pivot = new Vector2(0f, 0.5f);
            root.sizeDelta = new Vector2(ROW_WIDTH, ROW_HEIGHT);
            root.anchoredPosition = new Vector2(40f, 120f - index * (ROW_HEIGHT + ROW_GAP));
            AddImage(root, PanelColor);

            var accent = NewRect("Accent", root);
            accent.anchorMin = new Vector2(0f, 0f);
            accent.anchorMax = new Vector2(0f, 1f);
            accent.pivot = new Vector2(0f, 0.5f);
            accent.sizeDelta = new Vector2(6f, 0f);

            var name = AddText(NewRect("Name", root), 22f, TextAlignmentOptions.Left);
            Stretch(name.rectTransform, 18f, 60f, 4f, 8f);

            var time = AddText(NewRect("Time", root), 20f, TextAlignmentOptions.Right);
            Stretch(time.rectTransform, 18f, 14f, 4f, 8f);
            time.alpha = 0.8f;

            var track = NewRect("BarTrack", root);
            track.anchorMin = new Vector2(0f, 0f);
            track.anchorMax = new Vector2(1f, 0f);
            track.pivot = new Vector2(0.5f, 0f);
            track.offsetMin = new Vector2(6f, 0f);
            track.offsetMax = new Vector2(0f, 4f);
            AddImage(track, BarTrack);

            var bar = NewRect("Bar", track);
            bar.anchorMin = Vector2.zero;
            bar.anchorMax = Vector2.one;
            bar.offsetMin = bar.offsetMax = Vector2.zero;

            return new EffectRow
            {
                Root = root,
                Accent = AddImage(accent, Color.white),
                Name = name,
                Time = time,
                Bar = bar,
                BarFill = AddImage(bar, Color.white),
            };
        }

        private void BuildConnection()
        {
            _connection = NewRect("Connection", _root);
            _connection.anchorMin = _connection.anchorMax = Vector2.zero;
            _connection.pivot = Vector2.zero;
            _connection.anchoredPosition = new Vector2(16f, 14f);
            _connection.sizeDelta = new Vector2(400f, 20f);

            var dot = NewRect("Dot", _connection);
            dot.anchorMin = dot.anchorMax = new Vector2(0f, 0.5f);
            dot.pivot = new Vector2(0f, 0.5f);
            dot.sizeDelta = new Vector2(10f, 10f);
            _connectionDot = AddImage(dot, Color.gray);

            _connectionText = AddText(NewRect("Label", _connection), 15f, TextAlignmentOptions.Left);
            Stretch(_connectionText.rectTransform, 18f, 0f, 0f, 0f);
            _connectionText.alpha = 0.75f;
        }

        // Helpers

        private void TryUpgradeFont()
        {
            foreach (var font in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
            {
                if (font.name == "Barlow-Bold" || font.name == "Barlow-SemiBold")
                {
                    _font = font;
                    _fontIsFallback = false;
                    break;
                }
            }

            if (_fontIsFallback || _root == null)
            {
                return;
            }

            foreach (var text in _root.GetComponentsInChildren<TextMeshProUGUI>(true))
            {
                text.font = _font;
            }
        }

        private static RectTransform NewRect(string name, Transform parent)
        {
            var obj = new GameObject(name, typeof(RectTransform));
            obj.transform.SetParent(parent, false);
            return (RectTransform) obj.transform;
        }

        private static Image AddImage(RectTransform rect, Color color)
        {
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private TextMeshProUGUI AddText(RectTransform rect, float size, TextAlignmentOptions alignment)
        {
            var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            if (_font != null)
            {
                text.font = _font;
            }

            text.fontSize = size;
            text.alignment = alignment;
            text.color = Color.white;
            text.raycastTarget = false;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.richText = true;
            return text;
        }

        private static void SetPreferred(RectTransform rect, float width, float height)
        {
            var element = rect.gameObject.AddComponent<LayoutElement>();
            element.preferredWidth = element.minWidth = width;
            element.preferredHeight = element.minHeight = height;
        }

        private static void Stretch(RectTransform rect, float left, float right, float bottom, float top)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, -top);
        }

        private static float Ease(float t) => 1f - (1f - t) * (1f - t);

        // Viewer names come from chat, so they must not be able to inject rich text tags
        private static string Escape(string text) =>
            "<noparse>" + text.Replace("</noparse>", "", System.StringComparison.OrdinalIgnoreCase) + "</noparse>";

        private static Color GroupColor(ConflictGroup group) => group switch
        {
            ConflictGroup.Speed    => new Color(1f, 0.69f, 0.13f),
            ConflictGroup.NoteType => new Color(0.19f, 0.78f, 0.96f),
            ConflictGroup.Lanes    => new Color(0.89f, 0.33f, 0.94f),
            _                      => Color.white,
        };

        private static (string Name, Color Color) PlatformStyle(string platform) => platform switch
        {
            "twitch"  => ("TWITCH", new Color(0.57f, 0.27f, 1f)),
            "youtube" => ("YOUTUBE", new Color(0.9f, 0.1f, 0.15f)),
            "tiktok"  => ("TIKTOK", new Color(0.1f, 0.1f, 0.1f)),
            "kick"    => ("KICK", new Color(0.2f, 0.75f, 0.1f)),
            "test"    => ("TEST", new Color(0.4f, 0.4f, 0.45f)),
            _         => (null, default),
        };
    }
}
