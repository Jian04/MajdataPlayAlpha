using System;
using System.Collections.Generic;
using MajSimai;
using MajdataCore;
using MajdataPlay.Scenes.Game;
using UnityEngine;

namespace MajdataPlay.Alpha
{
    // A display object, not a NoteDrop: no input, judgment queue, count or sound.
    [DefaultExecutionOrder(500)]
    internal sealed class AlphaTrajectoryCarrier : MonoBehaviour
    {
        readonly List<Vector3> _path = new();
        readonly List<float> _distances = new();
        SpriteRenderer _sprite, _line;
        float _start, _duration, _total;
        AlphaVisualTimeline _timeline;
        string _type;
        bool _each, _break, _mine, _rotatingStar;
        float _starRotation;
        MaterialPropertyBlock _properties, _lineProperties;
        internal void Configure(SimaiNote note, SimaiTimingPoint timing, Sprite model = null, Sprite guide = null)
        {
            timing = AlphaSimaiParser.ForNoteTiming(note, timing);
            _timeline = AlphaSimaiParser.ForNote(note);
            var expression = AlphaNoteDecoration.Read(note.RawContent);
            if (expression?.trajectory == null || !AlphaTrajectoryPathGeometry.TryBuild(new AlphaRouteSource(), expression.trajectory.segments, _path))
                throw new FormatException("继承轨迹无法绘制");
            _start = (float)timing.Timing;
            _duration = Math.Max(.01f, (float)note.SlideTime);
            _distances.Add(0);
            for (var i = 1; i < _path.Count; i++) { _total += Vector3.Distance(_path[i - 1], _path[i]); _distances.Add(_total); }
            _type = expression.modifiers.HasHead(NoteModifierFlags.ForceStar) ? "star" : note.Type switch { SimaiNoteType.Hold => "hold", SimaiNoteType.Touch => "touch", SimaiNoteType.TouchHold => "touchhold", _ => "tap" };
            _rotatingStar = _type == "star" && expression.modifiers.HasHead(NoteModifierFlags.FakeRotate);
            _each = AlphaSimaiParser.StateEach(note) ?? timing.Notes.Length > 1; _break = note.IsBreak; _mine = note.IsMine;
            _sprite = gameObject.AddComponent<SpriteRenderer>();
            _sprite.sortingLayerName = _type is "touch" or "touchhold" ? "Touch" : "Tap"; _sprite.sortingOrder = 1000;
            if (model) _sprite.sprite = model;
            else if (_type == "star") { var skin = MajInstances.SkinManager.GetStarSkin(); _sprite.sprite = _mine ? skin.Mine : _break ? skin.Break : _each ? skin.Each : skin.Normal; }
            else if (_type == "hold") { var skin = MajInstances.SkinManager.GetHoldSkin(); _sprite.sprite = _mine ? skin.Mine : _break ? skin.Break : _each ? skin.Each : skin.Normal; }
            else if (_type == "touch") { var skin = MajInstances.SkinManager.GetTouchSkin(); _sprite.sprite = _mine ? skin.Mine : _break ? skin.Break : _each ? skin.Each : skin.Normal; }
            else if (_type == "touchhold") { var skin = MajInstances.SkinManager.GetTouchHoldSkin(); _sprite.sprite = (_mine ? skin.Fans_Mine : _break ? skin.Fans_Break : skin.Fans)[0]; }
            else { var skin = MajInstances.SkinManager.GetTapSkin(); _sprite.sprite = _mine ? skin.Mine : _break ? skin.Break : _each ? skin.Each : skin.Normal; }
            if (expression.position.HasSkin)
            {
                var width = _type == "tap" ? NoteSkinLibrary.ViewTapWidth : _type == "star" ? NoteSkinLibrary.ViewStarWidth : 0f;
                var inherited = NoteSkinLibrary.TryCreateSprite(expression.position.skin, _sprite.sprite, out var reason, width);
                if (inherited) _sprite.sprite = inherited;
                else Debug.LogWarning("继承图片已跳过：" + reason);
            }
            if (_type is not ("touch" or "touchhold"))
            {
            var lineObject = new GameObject("InheritedNoteGuide"); lineObject.transform.SetParent(transform.parent, false);
            _line = lineObject.AddComponent<SpriteRenderer>();
            _line.sprite = guide ? guide : MajInstances.SkinManager.GetTapSkin().GuideLines[_break ? 2 : _each ? 1 : 0];
            _line.sortingLayerName = "TapLines"; _line.sortingOrder = -1000;
            _line.forceRenderingOff = true;
            }
            _sprite.forceRenderingOff = true;
        }
        internal Vector3 Evaluate(float progress)
        {
            var target = Mathf.Clamp01(progress) * _total;
            var upper = _distances.BinarySearch(target); if (upper < 0) upper = ~upper;
            if (upper <= 0) return _path[0]; if (upper >= _path.Count) return _path[_path.Count - 1];
            var lower = upper - 1; var span = _distances[upper] - _distances[lower];
            return Vector3.Lerp(_path[lower], _path[upper], span <= .0001f ? 0 : (target - _distances[lower]) / span);
        }
        void LateUpdate()
        {
            var clock = Majdata<INoteController>.Instance;
            if (clock == null || _sprite == null) return;
            var now = clock.ThisFrameSec;
            if (clock.IsStart && now > _start + _duration)
            {
                _sprite.forceRenderingOff = true;
                if (_line) _line.forceRenderingOff = true;
                Destroy(gameObject); return;
            }
            var visible = clock.IsStart && now >= _start && now <= _start + _duration;
            _sprite.forceRenderingOff = !visible;
            if (_line) _line.forceRenderingOff = !visible;
            if (!visible) return;
            var timeline = _timeline;
            var progress = timeline.SlideProgress(now, _start, _duration);
            if (_rotatingStar && clock.IsStart) _starRotation += 400f * Time.deltaTime;
            SetPose(progress);
            transform.localRotation *= Quaternion.Euler(0, 0, timeline.Angle(_type, _start, now, _each, _break, _mine));
            transform.localScale = Vector3.one * (_type == "star" ? 1.5f : 1);
            var size = timeline.Size(_type, _start, now, _each, _break, _mine);
            transform.localScale = Vector3.Scale(transform.localScale, new Vector3(size.X, size.Y, 1));
            var color = timeline.Color(_type, _start, now, _each, _break, _mine);
            var alpha = timeline.Alpha(_type, _start, now, _each, _break, _mine);
            AlphaNoteMaterial.Apply(_sprite, ref _properties, color, alpha, mine: _mine);
            if (!_line) return;
            AlphaNoteMaterial.Apply(_line, ref _lineProperties, color, alpha, mine: _mine);
            var radius = transform.localPosition.magnitude;
            _line.transform.localScale = Vector3.one * (radius / 4.8f);
            _line.transform.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(transform.localPosition.y, transform.localPosition.x) * Mathf.Rad2Deg - 90);
        }
        internal void SetPose(float progress)
        {
            transform.localPosition = Evaluate(progress);
            if (_type is "hold" or "touchhold" or "star")
            {
                var tangent = Evaluate(Math.Min(1, progress + .001f)) - Evaluate(Math.Max(0, progress - .001f));
                transform.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(tangent.y, tangent.x) * Mathf.Rad2Deg + 18 + _starRotation);
            }
            else transform.localRotation = Quaternion.identity;
        }
        void OnDestroy()
        {
            AlphaNoteMaterial.Release(_sprite); AlphaNoteMaterial.Release(_line);
            if (_line) Destroy(_line.gameObject);
        }
    }
}
