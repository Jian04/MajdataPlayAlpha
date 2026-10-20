using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Cysharp.Threading.Tasks;
using MajdataPlay.IO;
using MajdataPlay.Scenes.Game;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

namespace MajdataPlay.Alpha
{
    [DefaultExecutionOrder(550)]
    internal sealed class AlphaMediaController : MonoBehaviour
    {
        sealed class Asset
        {
            internal AudioSampleWrap Audio;
            internal Texture2D Image;
            internal VideoPlayer Video;
            internal RenderTexture Target;
            internal Texture Texture => Image ? Image : Target;
        }
        readonly Dictionary<string, Asset> _assets = new(StringComparer.OrdinalIgnoreCase);
        readonly List<AlphaDisplayEvent> _events = new();
        AlphaDisplayEvent _audioEvent, _overlayEvent;
        GamePlayManager _game;
        BGManager _background;
        RawImage _overlay, _outgoing;
        bool _disposed;
        internal async UniTask Prepare(AlphaVisualTimeline timeline, string folder, bool editedAudio = false)
        {
            _game = GetComponent<GamePlayManager>();
            _background = Majdata<BGManager>.Instance;
            _events.AddRange(timeline.DisplayEvents.Where(e => e.Name == "PVOVERLAY" || e.Name == "AUDIO" && !editedAudio));
            foreach (var ev in _events.Where(e => e.Value == 1))
            {
                if (_assets.ContainsKey(ev.Path)) continue;
                var asset = new Asset(); _assets[ev.Path] = asset;
                try
                {
                    if (string.IsNullOrEmpty(folder)) throw new IOException("当前歌曲没有本地素材目录");
                    var root = System.IO.Path.GetFullPath(folder) + System.IO.Path.DirectorySeparatorChar;
                    var path = System.IO.Path.GetFullPath(System.IO.Path.Combine(root, ev.Path.Replace('/', System.IO.Path.DirectorySeparatorChar)));
                    if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !File.Exists(path)) throw new IOException("找不到谱面素材：" + ev.Path);
                    if (ev.Name == "AUDIO")
                    {
                        asset.Audio = await Majdata<AudioManager>.Instance.LoadMusicAsync(path, false, true);
                        await UniTask.SwitchToMainThread();
                        if (_disposed) { asset.Audio.Dispose(); return; }
                        asset.Audio.SetVolume(MajEnv.Settings.Audio.Volume.Track);
                    }
                    else if (System.IO.Path.GetExtension(path).Equals(".mp4", StringComparison.OrdinalIgnoreCase))
                    {
                        var obj = new GameObject("AlphaPVOverlay"); obj.transform.SetParent(transform, false);
                        asset.Video = obj.AddComponent<VideoPlayer>();
                        asset.Video.playOnAwake = false; asset.Video.isLooping = false;
                        asset.Video.audioOutputMode = VideoAudioOutputMode.None;
                        asset.Video.source = VideoSource.Url; asset.Video.url = path;
                        asset.Video.renderMode = VideoRenderMode.RenderTexture;
                        var failed = false;
                        asset.Video.errorReceived += (_, error) => { failed = true; Debug.LogWarning("PV 覆盖素材已跳过：" + error); };
                        asset.Video.Prepare();
                        var deadline = Time.realtimeSinceStartup + 20;
                        while (!_disposed && !failed && !asset.Video.isPrepared && Time.realtimeSinceStartup < deadline) await UniTask.Yield();
                        if (_disposed) return;
                        if (!asset.Video.isPrepared || failed) throw new IOException("视频解码失败：" + ev.Path);
                        asset.Target = new RenderTexture(Math.Max(1, (int)asset.Video.width), Math.Max(1, (int)asset.Video.height), 0);
                        asset.Target.Create(); asset.Video.targetTexture = asset.Target;
                    }
                    else
                    {
                        var bytes = await File.ReadAllBytesAsync(path);
                        await UniTask.SwitchToMainThread();
                        if (_disposed) return;
                        asset.Image = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                        if (!asset.Image.LoadImage(bytes)) throw new IOException("图片解码失败：" + ev.Path);
                    }
                }
                catch (Exception error) { Debug.LogWarning("Alpha 素材已跳过：" + error.Message); }
            }
            if (_events.Any(e => e.Name == "PVOVERLAY") && _background)
            {
                _outgoing = _background.CreateAlphaOverlay("AlphaPVOutgoing");
                _overlay = _background.CreateAlphaOverlay("AlphaPVCurrent");
                if (_overlay && _outgoing) _overlay.rectTransform.SetSiblingIndex(_outgoing.rectTransform.GetSiblingIndex() + 1);
            }
        }
        Asset Get(AlphaDisplayEvent ev) => ev != null && ev.Value == 1 && _assets.TryGetValue(ev.Path, out var asset) ? asset : null;
        void LateUpdate()
        {
            if (!_game) return;
            if (!_game.IsStart || _game.State is GamePlayStatus.Blocking or GamePlayStatus.Ended)
            {
                foreach (var asset in _assets.Values) { if (asset.Audio?.IsPlaying == true) asset.Audio.Pause(); if (asset.Video && asset.Video.isPlaying) asset.Video.Pause(); }
                return;
            }
            var now = _game.ThisFrameSec;
            AlphaDisplayEvent audio = null, overlay = null, previousOverlay = null;
            foreach (var ev in _events)
            {
                if (ev.Time > now) continue;
                if (ev.Name == "AUDIO") audio = ev;
                else { previousOverlay = overlay; overlay = ev; }
            }
            if (audio != _audioEvent)
            {
                Get(_audioEvent)?.Audio?.Stop(); _audioEvent = audio;
            }
            var activeAudio = Get(audio)?.Audio;
            if (activeAudio != null && !activeAudio.IsEmpty)
            {
                var position = Math.Max(0, (now - audio.Time) * _game.PlaybackSpeed);
                if (position >= activeAudio.Length.TotalSeconds) { if (activeAudio.IsPlaying) activeAudio.Stop(); }
                else
                {
                    activeAudio.Speed = _game.PlaybackSpeed;
                    if (!activeAudio.IsPlaying || Math.Abs(activeAudio.CurrentSec - position) > .15)
                    { activeAudio.CurrentSec = position; if (!activeAudio.IsPlaying) activeAudio.Play(); }
                }
            }
            if (!_overlay) return;
            if (_overlayEvent != overlay) { Get(_overlayEvent)?.Video?.Pause(); _overlayEvent = overlay; }
            var current = Get(overlay); var outgoing = Get(previousOverlay);
            var blend = overlay == null || overlay.Duration <= 0 ? 1 : Mathf.Clamp01((float)(now - overlay.Time) / overlay.Duration);
            float Draw(RawImage image, Asset asset, AlphaDisplayEvent ev, float opacity)
            {
                var valid = asset?.Texture;
                image.texture = valid; image.color = new Color(1, 1, 1, valid ? opacity : 0);
                if (asset?.Video && ev != null)
                {
                    var elapsed = Math.Max(0, (now - ev.Time) * _game.PlaybackSpeed);
                    if (elapsed < asset.Video.length)
                    {
                        asset.Video.playbackSpeed = _game.PlaybackSpeed;
                        if (!asset.Video.isPlaying || Math.Abs(asset.Video.time - elapsed) > .2) asset.Video.time = elapsed;
                        if (!asset.Video.isPlaying) asset.Video.Play();
                    }
                    else { asset.Video.Pause(); image.color = Color.clear; return 0; }
                }
                return valid ? opacity : 0;
            }
            var previousAlpha = Draw(_outgoing, outgoing, previousOverlay, current?.Texture ? 1 : 1 - blend);
            var currentAlpha = Draw(_overlay, current, overlay, blend);
            _background.SetAlphaOriginalOpacity(currentAlpha >= 1 || previousAlpha >= 1 ? 0 : 1);
        }
        void OnDestroy()
        {
            _disposed = true;
            foreach (var asset in _assets.Values)
            {
                asset.Audio?.Dispose();
                if (asset.Image) Destroy(asset.Image);
                if (asset.Video) Destroy(asset.Video.gameObject);
                if (asset.Target) { asset.Target.Release(); Destroy(asset.Target); }
            }
            if (_overlay) Destroy(_overlay.gameObject);
            if (_outgoing) Destroy(_outgoing.gameObject);
            if (_background) _background.SetAlphaOriginalOpacity(1);
        }
    }
}
