using System;
using System.Collections.Generic;
using MajSimai;
using MajdataPlay.Alpha;
using MajdataPlay.Scenes.Game.Notes.Behaviours;
using MajdataPlay.Scenes.Game.Notes;
using MajdataPlay.Scenes.Game.Notes.Touch;
using MajdataPlay.Scenes.Game.Buffers;
using MajdataPlay.Scenes.Game.Parsing;
using UnityEngine;

namespace MajdataPlay.Scenes.Game
{
    public partial class NoteLoader
    {
        void CreateAlphaSlide(SimaiTimingPoint timing, FoldedSimaiNote note, IList<NotePoolingInfo?> eachNotes,
            IList<ITouchGroupInfoProvider> touchMembers)
        {
            if (!AlphaPathParser.TryRead(note.RawContent, out var expression)) throw new FormatException("Invalid Alpha path");
            if (expression.path.segments.Count == 1 && expression.path.segments[0].shape == "w")
            {
                CreateAlphaWifi(timing, note, eachNotes, expression);
                return;
            }
            var metadata = AlphaPathMetadata.Build(expression.path);
            var obj = Instantiate(slidePrefab[SLIDE_PREFAB_MAP["Ex"]], notes.transform.GetChild(3));
            var slide = obj.GetComponent<ExtendSlideDrop>();
            var starInfos = new TapPoolingInfo?[note.Count];
            var each = NoteCreateHelper.IsEachNote(note, timing.Notes);
            if (!note.IsSlideNoHead)
                for (var i = 0; i < note.Count; i++)
                    if (expression.position.IsTouch)
                    {
                        var head = new SimaiNote { Type = SimaiNoteType.Touch, StartPosition = note.StartPosition, TouchArea = note.TouchArea,
                            IsBreak = note.IsBreak, IsEx = note.IsEx, IsMine = note.IsMine, IsHanabi = note.IsHanabi, RawContent = note.RawContent };
                        var headTimeline = AlphaSimaiParser.ForNote(note);
                        if (!ReferenceEquals(headTimeline, AlphaSimaiParser.Current)) AlphaSimaiParser.Current.NoteOverrides[head] = headTimeline;
                        if (AlphaSimaiParser.Current.NoteContexts.TryGetValue(note, out var headContext)) AlphaSimaiParser.Current.NoteContexts[head] = headContext;
                        _poolManager.AddTouch(CreateTouch(head, timing, touchMembers));
                    }
                    else
                    {
                        var info = CreateStar(note.StartPosition, note, timing);
                        _poolManager.AddTap(info); eachNotes.Add(info); starInfos[i] = info;
                    }
            slide.Metadata = metadata;
            slide.StartPos = note.StartPosition;
            slide.EndPos = Math.Clamp(expression.path.segments[expression.path.segments.Count - 1].endPosition, 1, 8);
            slide.IsBreak = note.IsSlideBreak;
            slide.IsEach = !note.IsMineSlide && (each || note.Count > 1);
            slide.IsMine = note.IsMineSlide;
            slide.IsFake = AlphaFakeNotes.IsFake(note, timing, true);
            slide.AlphaExpression = note.RawContent;
            slide.AlphaTimeline = AlphaSimaiParser.ForNote(note);
            slide.AlphaEachInStream = AlphaSimaiParser.StateEach(note);
            slide.Speed = Math.Abs(NoteSpeed * AlphaSimaiParser.ForNote(note).HSpeed("slide", timing.Timing, 1, false, false, false));
            slide.StartTiming = (float)note.SlideStartTime;
            slide.Timing = (float)timing.Timing;
            slide.Length = (float)note.SlideTime;
            slide.IsSlideNoHead = _isSlideNoHead;
            slide.IsSlideNoTrack = _isSlideNoTrack;
            slide.Multiple = note.Count;
            var bars = metadata.ArrowPoses.Length - 2;
            if (MajEnv.Settings.Display.SlideSortOrder == MajdataPlay.Settings.JudgeModeOption.Classic) _slideLayer += bars;
            slide.SortOrder = _slideLayer;
            if (MajEnv.Settings.Display.SlideSortOrder != MajdataPlay.Settings.JudgeModeOption.Classic) _slideLayer -= bars;
            slide.Init();
            UpdateStarRotateSpeed(new CreateSlideResult<ExtendSlideDrop> { SlideInstance = slide, StarInfos = starInfos }, slide.Length, metadata.SlideLength);
            AddSlideToQueue(timing, slide);
        }

        void CreateAlphaWifi(SimaiTimingPoint timing, FoldedSimaiNote note, IList<NotePoolingInfo?> eachNotes,
            MajdataCore.NoteExpressionData expression)
        {
            var end = expression.path.segments[0].end.position;
            var proxy = new SubSlideNote { Origin = note, RawContent = $"{note.StartPosition}w{end}",
                StartPosition = note.StartPosition, Type = SimaiNoteType.Slide, IsSlideNoHead = true,
                IsSlideBreak = note.IsSlideBreak, IsMineSlide = note.IsMineSlide,
                SlideStartTime = note.SlideStartTime, SlideTime = note.SlideTime };
            var result = CreateWifi(timing, proxy, note.Count);
            var slide = result.SlideInstance;
            slide.EndPos = end;
            var heads = new TapPoolingInfo?[note.Count];
            if (!note.IsSlideNoHead)
                for (var i = 0; i < note.Count; i++)
                {
                    var head = CreateStar(note.StartPosition, note, timing);
                    heads[i] = head; _poolManager.AddTap(head); eachNotes.Add(head);
                }
            slide.Init();
            slide.ApplyAlphaWifi(expression);
            UpdateStarRotateSpeed(new CreateSlideResult<WifiDrop> { SlideInstance = slide, StarInfos = heads }, slide.Length, 9.6f);
            AddSlideToQueue(timing, slide);
        }
    }
}
