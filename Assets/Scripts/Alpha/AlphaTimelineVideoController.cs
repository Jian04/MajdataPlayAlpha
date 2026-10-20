using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Cysharp.Threading.Tasks;
using MajdataPlay.Scenes.Game;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

namespace MajdataPlay.Alpha
{
    [DefaultExecutionOrder(560)]
    internal sealed class AlphaTimelineVideoController : MonoBehaviour
    {
        sealed class Visual
        {
            internal AlphaMediaClip Clip;
            internal RawImage Image;
            internal VideoPlayer Player;
            internal Texture2D Still;
            internal RenderTexture Target;
        }
        readonly List<Visual> _visuals = new();
        GamePlayManager _game;
        BGManager _background;
        bool _disposed;
        internal async UniTask Prepare(AlphaMediaTimeline project)
        {
            _game = GetComponent<GamePlayManager>();
            _background = Majdata<BGManager>.Instance;
            foreach (var clip in project.Clips.Where(c => c.Track == 0).OrderBy(c => c.TrackIndex).ThenBy(c => c.TimelineStart))
            {
                if (!File.Exists(clip.SourcePath)) { Debug.LogWarning("剪辑视频素材不存在：" + clip.SourcePath); continue; }
                var visual = new Visual { Clip = clip, Image = _background.CreateAlphaOverlay("AlphaEditedPV") };
                if (_visuals.Count > 0 && visual.Image && _visuals[_visuals.Count - 1].Image)
                    visual.Image.rectTransform.SetSiblingIndex(_visuals[_visuals.Count - 1].Image.rectTransform.GetSiblingIndex() + 1);
                _visuals.Add(visual);
                try
                {
                    if (clip.IsStillImage)
                    {
                        var data = await File.ReadAllBytesAsync(clip.SourcePath);
                        await UniTask.SwitchToMainThread();
                        if (_disposed) return;
                        visual.Still = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                        if (!visual.Still.LoadImage(data)) throw new IOException("图片解码失败");
                        visual.Image.texture = visual.Still;
                    }
                    else
                    {
                        var obj = new GameObject("AlphaEditedPVPlayer"); obj.transform.SetParent(transform, false);
                        visual.Player = obj.AddComponent<VideoPlayer>();
                        var player = visual.Player;
                        player.playOnAwake = false; player.isLooping = false; player.audioOutputMode = VideoAudioOutputMode.None;
                        player.source = VideoSource.Url; player.url = clip.SourcePath; player.renderMode = VideoRenderMode.RenderTexture;
                        var failed = false;
                        player.errorReceived += (_, message) => { failed = true; Debug.LogWarning("剪辑视频解码失败：" + message); };
                        player.Prepare();
                        var deadline = Time.realtimeSinceStartup + 20;
                        while (!_disposed && !failed && !player.isPrepared && Time.realtimeSinceStartup < deadline) await UniTask.Yield();
                        if (_disposed) return;
                        if (!player.isPrepared || failed) throw new IOException("视频解码失败");
                        visual.Target = new RenderTexture(Math.Max(1, (int)player.width), Math.Max(1, (int)player.height), 0);
                        visual.Target.Create(); player.targetTexture = visual.Target; visual.Image.texture = visual.Target;
                    }
                }
                catch (Exception error) { Debug.LogWarning("剪辑视频已跳过：" + error.Message); }
            }
        }
        void LateUpdate()
        {
            if (!_game || _disposed) return;
            var playing = _game.IsStart && _game.State is not (GamePlayStatus.Blocking or GamePlayStatus.Ended);
            // Clip positions are seconds on the original track, independent of practice speed.
            var time = _game.ThisFrameSec * _game.PlaybackSpeed;
            foreach (var visual in _visuals)
            {
                var clip = visual.Clip;
                var active = _game.IsStart && _game.State != GamePlayStatus.Ended && clip.Active(time);
                if (visual.Player)
                {
                    var source = clip.SourceTime(time);
                    active &= source < visual.Player.length;
                    if (playing && active && visual.Player.isPrepared)
                    {
                        visual.Player.playbackSpeed = _game.PlaybackSpeed;
                        if (!visual.Player.isPlaying || Math.Abs(visual.Player.time - source) > .15) visual.Player.time = source;
                        if (!visual.Player.isPlaying) visual.Player.Play();
                    }
                    else if (visual.Player.isPlaying) visual.Player.Pause();
                }
                var brightness = Mathf.Max(0, 1 + (float)clip.Brightness);
                if (visual.Image) visual.Image.color = new Color(brightness, brightness, brightness, active ? (float)(clip.Opacity * clip.Fade(time)) : 0);
            }
        }
        void OnDestroy()
        {
            _disposed = true;
            foreach (var visual in _visuals)
            {
                if (visual.Player) Destroy(visual.Player.gameObject);
                if (visual.Still) Destroy(visual.Still);
                if (visual.Image) Destroy(visual.Image.gameObject);
                if (visual.Target) { visual.Target.Release(); Destroy(visual.Target); }
            }
        }
    }
}
