using System;
using MajdataCore;
using MajdataPlay.Alpha;
using MajdataPlay.Scenes.Game.Notes.Slide;
using MajdataPlay.Settings;
using UnityEngine;

namespace MajdataPlay.Scenes.Game.Notes.Behaviours
{
    internal sealed partial class WifiDrop
    {
        SlideArea[][] _alphaWifiAreas;
        float[][] _alphaWifiProgress;

        internal static MajdataPlay.Scenes.Game.Parsing.Slide.ExtendSlideMetadata[] AlphaWifiRoutes(NoteExpressionData expression)
        {
            var result = new MajdataPlay.Scenes.Game.Parsing.Slide.ExtendSlideMetadata[3];
            var tail = expression.path.segments[0].end;
            for (var i = 0; i < 3; i++)
            {
                var end = new SlidePositionData { area = 'K', position = (tail.position + i + 6) % 8 + 1, isDZone = tail.isDZone };
                var path = new SlidePathData { head = expression.position };
                path.segments.Add(new SlidePathSegmentData { start = expression.position, end = end, shape = "-" });
                result[i] = AlphaPathMetadata.Build(path);
            }
            return result;
        }

        internal void ApplyAlphaWifi(NoteExpressionData expression)
        {
            DisposeAlphaWifi();
            var routes = AlphaWifiRoutes(expression);
            _alphaWifiAreas = new SlideArea[3][];
            _alphaWifiProgress = new float[3][];
            var head = expression.position;
            var tail = expression.path.segments[0].end;
            if (head.isDZone)
            {
                Transform.Rotate(0, 0, 22.5f, Space.World);
                var rotation = Quaternion.Euler(0, 0, 22.5f);
                for (var i = 0; i < 3; i++)
                {
                    _starStartPositions[i] = rotation * _starStartPositions[i];
                    StarTransforms.Span[i].rotation = rotation * StarTransforms.Span[i].rotation;
                }
                if (SlideOK) { SlideOK.transform.position = rotation * SlideOK.transform.position; SlideOK.transform.rotation = rotation * SlideOK.transform.rotation; }
            }
            JudgeQueueLength = 0;
            for (var i = 0; i < 3; i++)
            {
                _starEndPositions[i] = AlphaPathMetadata.Position('K', (tail.position + i + 6) % 8 + 1, tail.isDZone);
                StarTransforms.Span[i].position = _starStartPositions[i];
                var metadata = routes[i];
                var areas = new SlideArea[metadata.JudgeAreaQueue.Length];
                _alphaWifiAreas[i] = areas;
                _alphaWifiProgress[i] = metadata.AreaProgress;
                for (var j = 0; j < areas.Length; j++)
                {
                    var entry = metadata.JudgeAreaQueue[j];
                    var progress = metadata.AreaProgress[j];
                    var finish = j + 1 == areas.Length ? SlideBars.Count : Mathf.FloorToInt(metadata.AreaProgress[j + 1] * SlideBars.Count);
                    areas[j] = new SlideArea(new[] { (entry.SensorA, j + 1 == areas.Length) },
                        Mathf.FloorToInt(progress * SlideBars.Count), finish) { IsSkippable = USERSETTING_SLIDE_SKIPPING };
                }
                JudgeQueues[i] = areas;
                JudgeQueueLength = Math.Max(JudgeQueueLength, areas.Length);
            }
        }

        void AlphaWifiAutoplayUpdate()
        {
            if (!IsAutoplay || IsMine || State is not (NoteStatus.Running or NoteStatus.Arrived)) return;
            var progress = Length <= 0 ? 1 : Mathf.Clamp01((Length - GetRemainingTimeWithoutOffset()) / Length);
            if (AutoplayMode == AutoplayModeOption.Enable)
            {
                if (progress >= 1) { Autoplay(); return; }
                if (progress > 0) PlaySFX();
                AutoplayProgress = progress;
                var hide = SlideBars.Count;
                for (var i = 0; i < 3; i++)
                {
                    var passed = 0;
                    for (var j = 1; j < _alphaWifiProgress[i].Length; j++)
                        if (progress >= _alphaWifiProgress[i][j]) passed = j;
                    var delta = passed - (_alphaWifiAreas[i].Length - JudgeQueues[i].Length);
                    if (delta > 0) JudgeQueues[i] = JudgeQueues[i].Slice(Math.Min(delta, JudgeQueues[i].Length));
                    if (!JudgeQueues[i].IsEmpty) hide = Math.Min(hide, JudgeQueues[i].Span[0].ArrowProgressWhenOn);
                }
                HideBar(hide);
            }
            else if (AutoplayMode is AutoplayModeOption.DJAuto_ButtonRing_First or AutoplayModeOption.DJAuto_TouchPanel_First)
            {
                if (IsFinished) return;
                // Sample the same three rendered rays. Native sensor/mine checks consume these events.
                for (var step = 0; step <= 4; step++)
                {
                    var at = Mathf.Lerp(DJAutoplayProgress, progress, step / 4f);
                    for (var i = 0; i < 3; i++)
                        SlideDJAutoSimulateSensorPress(Vector3.Lerp(_starStartPositions[i], _starEndPositions[i], at), i == 1 ? .8f : .15f);
                }
                DJAutoplayProgress = progress;
            }
        }

        void DisposeAlphaWifi()
        {
            if (_alphaWifiAreas == null) return;
            foreach (var areas in _alphaWifiAreas)
                if (areas != null) for (var i = 0; i < areas.Length; i++) areas[i].Dispose();
            _alphaWifiAreas = null;
            _alphaWifiProgress = null;
        }
    }
}
