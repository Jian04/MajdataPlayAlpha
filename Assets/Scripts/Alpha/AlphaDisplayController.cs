using System;
using System.Collections.Generic;
using System.Linq;
using MajdataCore;
using UnityEngine;
using TMPro;
using MajdataPlay.Scenes.Game;

namespace MajdataPlay.Alpha
{
    [DefaultExecutionOrder(500)]
    internal sealed class AlphaDisplayController : MonoBehaviour
    {
        internal static float JudgeTextOpacity { get; private set; } = 1;
        readonly Dictionary<string, TMP_FontAsset> _fonts = new();
        readonly Dictionary<int, TextMeshProUGUI> _texts = new();
        readonly Dictionary<int, AlphaDisplayEvent> _active = new();
        RectTransform _parent;
        TMP_FontAsset _defaultFont;
        GamePlayManager _gameplay;
        SpriteRenderer _judgeLine, _expandedLine;
        Color _judgeLineBaseline;
        void ApplyJudgeLine(AlphaVisualTimeline timeline, float time)
        {
            if (!_judgeLine)
            {
                var outline = Majdata<OutlineLoader>.Instance;
                if (!outline) return;
                _judgeLine = outline.GetComponent<SpriteRenderer>();
                if (!_judgeLine) return;
                _judgeLineBaseline = _judgeLine.color;
            }
            var color = _judgeLineBaseline;
            var from = color; var target = color;
            AlphaDisplayEvent paint = null, expand = null;
            foreach (var ev in timeline.DisplayEvents)
            {
                if ((float)ev.Time > time) break;
                if (ev.Name == "JLINE")
                {
                    from = paint == null ? color : Color.Lerp(from, target, paint.Duration <= 0 ? 1 : Mathf.Clamp01(((float)ev.Time - (float)paint.Time) / paint.Duration));
                    target = ev.RGBA.HasValue ? new Color(((ev.RGBA.Value >> 24) & 255) / 255f, ((ev.RGBA.Value >> 16) & 255) / 255f, ((ev.RGBA.Value >> 8) & 255) / 255f, (ev.RGBA.Value & 255) / 255f) : _judgeLineBaseline;
                    paint = ev;
                }
                if (ev.Name == "JUDGELINEEXPAND") expand = ev;
            }
            if (paint != null) color = Color.Lerp(from, target, paint.Duration <= 0 ? 1 : Mathf.Clamp01((time - (float)paint.Time) / paint.Duration));
            var opacity = timeline.DisplayValue("JUDGELINE", time) ?? 1;
            color.a *= opacity;
            _judgeLine.color = color;
            if (expand == null || expand.Value == 0) { if (_expandedLine) _expandedLine.forceRenderingOff = true; return; }
            if (!_expandedLine)
            {
                var obj = new GameObject("AlphaExpandedJudgeLine");
                obj.transform.SetParent(_judgeLine.transform, false);
                _expandedLine = obj.AddComponent<SpriteRenderer>();
                _expandedLine.sharedMaterial = _judgeLine.sharedMaterial;
                _expandedLine.sortingLayerID = _judgeLine.sortingLayerID;
                _expandedLine.sortingOrder = _judgeLine.sortingOrder + 1;
            }
            _expandedLine.sprite = _judgeLine.sprite;
            _expandedLine.transform.localRotation = Quaternion.Euler(0, 0, expand.Value * (expand.Duration <= 0 ? 1 : Mathf.Clamp01((time - (float)expand.Time) / expand.Duration)));
            _expandedLine.color = new Color(1, 1, 1, opacity);
            _expandedLine.forceRenderingOff = opacity <= .001f;
        }
        void LateUpdate()
        {
            _gameplay ??= GetComponent<GamePlayManager>();
            if (!_gameplay || !_gameplay.IsStart) return;
            var timeline = AlphaSimaiParser.Current;
            if (!timeline.HasDisplay) return;
            var time = _gameplay.ThisFrameSec;
            if (timeline.DisplayEvents.Any(e => e.Name is "JLINE" or "JUDGELINE" or "JUDGELINEEXPAND")) ApplyJudgeLine(timeline, time);
            Majdata<ObjectCounter>.Instance?.SetAlphaInfoVisibility(timeline.DisplayValue("SHOWCOMBOINFO", time), timeline.DisplayValue("SHOWJUDGEINFO", time));
            JudgeTextOpacity = timeline.DisplayValue("SHOWJUDGETEXT", time) ?? 1;
            AlphaDisplayEvent comboMode = null;
            foreach (var ev in timeline.DisplayEvents)
                if (ev.Name == "COMBODISPLAY" && time >= ev.Time + ev.Duration * .5f) comboMode = ev;
            Majdata<ObjectCounter>.Instance?.SetAlphaComboDisplay(comboMode == null || comboMode.ResetToDefault ? null : (int)comboMode.Value);
            var brightness = timeline.DisplayValue("INNERBRIGHTNESS", time, MajEnv.Settings.Game.BackgroundDim);
            if (brightness.HasValue) Majdata<BGManager>.Instance?.SetBackgroundDim(brightness.Value);
            _active.Clear();
            foreach (var ev in timeline.DisplayEvents)
                if (ev.Name == "TEXT" && (float)ev.Time <= time) _active[ev.Subtitle.index] = ev;
            foreach (var pair in _texts)
            {
                if (!_active.TryGetValue(pair.Key, out var ev) || ev.Subtitle.text.Length == 0 ||
                    ev.Subtitle.duration >= 0 && time - (float)ev.Time >= ev.Subtitle.duration)
                    pair.Value.gameObject.SetActive(false);
            }
            foreach (var pair in _active)
            {
                var ev = pair.Value;
                var text = ev.Subtitle;
                var elapsed = time - (float)ev.Time;
                if (text.text.Length == 0 || text.duration >= 0 && elapsed >= text.duration) continue;
                if (!_parent)
                {
                    var root = UnityEngine.Object.FindObjectsByType<ScreenPosCanvasMover>(FindObjectsSortMode.None).FirstOrDefault();
                    if (root) _parent = root.GetComponent<RectTransform>();
                    else _parent = Majdata<ObjectCounter>.Instance?.GetComponentInParent<Canvas>()?.GetComponent<RectTransform>();
                    if (!_parent) continue;
                }
                if (!_texts.TryGetValue(pair.Key, out var label))
                {
                    var obj = new GameObject("AlphaSubtitle" + pair.Key, typeof(RectTransform));
                    obj.transform.SetParent(_parent, false);
                    label = obj.AddComponent<TextMeshProUGUI>();
                    var reference = _parent.GetComponentInChildren<TextMeshProUGUI>();
                    if (reference) label.font = reference.font;
                    _defaultFont ??= label.font;
                    label.alignment = TextAlignmentOptions.TopLeft;
                    label.raycastTarget = false;
                    _texts[pair.Key] = label;
                }
                var rt = label.rectTransform;
                rt.anchorMin = new Vector2(text.x, 1 - text.y); rt.anchorMax = rt.anchorMin;
                rt.pivot = new Vector2(0, 1); rt.anchoredPosition = new Vector2(20, -20);
                rt.sizeDelta = new Vector2(Math.Max(50, _parent.rect.width * (1 - text.x) - 40), Math.Max(100, _parent.rect.height * (1 - text.y) - 40));
                if (_defaultFont) label.font = _defaultFont;
                var typewriter = text.style.Equals("Typewriter", StringComparison.OrdinalIgnoreCase);
                label.text = typewriter ? Typewriter(text.text, elapsed, text.transition) : text.text;
                if (!string.IsNullOrEmpty(text.font) && text.font != "Default")
                {
                    if (!_fonts.TryGetValue(text.font, out var font))
                    {
                        if (FontFiles.IsFontFile(text.font)) font = RuntimeFontFiles.Load(text.font, NoteSkinLibrary.ChartFolder);
                        else
                        {
                        var osName = text.font switch { "MicrosoftYaHei" => "Microsoft YaHei", "CascadiaMono" => "Cascadia Mono", "CascadiaCode" => "Cascadia Code", "NotoSansSC" => "Noto Sans SC", "NotoSerifSC" => "Noto Serif SC", "GlobalMonospace" => "Consolas", _ => text.font };
                        var installed = Font.GetOSInstalledFontNames();
                        if (installed.Any(n => n.Equals(osName, StringComparison.OrdinalIgnoreCase)))
                            font = TMP_FontAsset.CreateFontAsset(Font.CreateDynamicFontFromOSFont(osName, 32));
                        else Debug.LogWarning("字幕字体未安装，使用默认字体：" + osName);
                        }
                        _fonts[text.font] = font;
                    }
                    if (font) label.font = font;
                }
                label.fontSize = text.size > 0 ? text.size : 32;
                var alpha = !typewriter && text.transition > 0 ? Mathf.Clamp01(elapsed / text.transition) : 1;
                
                label.color = new Color((text.RGB >> 16 & 255) / 255f, (text.RGB >> 8 & 255) / 255f, (text.RGB & 255) / 255f, alpha); label.gameObject.SetActive(true);
                // Style is evaluated from chart time, so seeking restores the same frame.
                if (text.style.Equals("Slide", StringComparison.OrdinalIgnoreCase)) rt.anchoredPosition += new Vector2(-40 * (1 - alpha), 0);
                else if (text.style.Equals("Scale", StringComparison.OrdinalIgnoreCase)) rt.localScale = Vector3.one * (.8f + .2f * alpha);
                else rt.localScale = Vector3.one;
            }
        }
        internal static string Typewriter(string text, float elapsed, float duration)
        {
            if (duration <= 0 || elapsed >= duration) return text;
            var elements = System.Globalization.StringInfo.ParseCombiningCharacters(text);
            var count = Mathf.Clamp(Mathf.CeilToInt(elements.Length * elapsed / duration), 0, elements.Length);
            return count == 0 ? "" : text.Substring(0, count == elements.Length ? text.Length : elements[count]);
        }
        void OnDestroy()
        {
            JudgeTextOpacity = 1;
            foreach (var pair in _fonts) if (pair.Value && !FontFiles.IsFontFile(pair.Key)) Destroy(pair.Value);
            foreach (var text in _texts.Values) if (text) Destroy(text.gameObject);
            if (_expandedLine) Destroy(_expandedLine.gameObject);
        }
    }
}
