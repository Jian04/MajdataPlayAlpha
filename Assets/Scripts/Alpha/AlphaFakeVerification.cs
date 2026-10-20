#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MajdataPlay.Scenes.Game;
using MajdataPlay.Scenes.Game.Buffers;
using MajdataPlay.Scenes.Game.Notes;
using MajdataPlay.Scenes.Game.Notes.Behaviours;
using MajdataPlay.Scenes.Game.Notes.Controllers;
using MajdataPlay.Scenes.Game.Parsing;
using MajdataPlay.Scenes.Game.Notes.Slide;
using MajdataPlay.IO;
using MajSimai;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace MajdataPlay.Editor
{
    internal static class AlphaFakeVerification
    {
        delegate void JudgeCount<T>(T note, in NoteJudgeResult result, int multiple) where T : NoteDrop;
        static GameObject root;
        static int checks;
        static void Require(bool condition, string name)
        {
            if (!condition) throw new Exception(name);
            checks++;
        }
        public static void Run()
        {
            root = new GameObject("Alpha fake verification");
            var clock = new Clock();
            var settings = MajEnv.Settings;
            typeof(MajEnv).GetProperty("Settings").SetValue(null, new MajdataPlay.Settings.GameSetting());
            var manager = root.AddComponent<NotePoolManager>();
            var counter = root.AddComponent<ObjectCounter>();
            using var taps = new ReturningPool<TapPoolingInfo, TapQueueInfo>(root);
            using var holds = new ReturningPool<HoldPoolingInfo, TapQueueInfo>(root);
            using var touches = new ReturningPool<TouchPoolingInfo, TouchQueueInfo>(root);
            using var touchHolds = new ReturningPool<TouchHoldPoolingInfo, TouchQueueInfo>(root);
            Set(manager, "tapPool", taps); Set(manager, "holdPool", holds);
            Set(manager, "touchPool", touches); Set(manager, "touchHoldPool", touchHolds);
            try
            {
                CheckPooled<TapDrop>(clock, manager, counter, taps, 0);
                CheckPooled<HoldDrop>(clock, manager, counter, holds, 1);
                CheckPooled<TouchDrop>(clock, manager, counter, touches, 0);
                CheckPooled<TouchHoldDrop>(clock, manager, counter, touchHolds, 1);
                CheckSlide<SlideDrop>(clock);
                CheckSlide<ExtendSlideDrop>(clock);
                CheckSlide<WifiDrop>(clock);
                CheckAlphaMineTrail(clock);
                CheckSizeRendering(clock);
                CheckNewNoteTransitions(clock);
                Invoke(counter, "UpdateAccRate");
                Require(counter.TotalNoteScore == 0 && counter.CurrentNoteScore == 0 && counter.AllFinished,
                    "An all-fake chart must have zero score/count without division by zero");
                CheckLoaderQueues();
                CheckFakeAnswerSounds();
                CheckMegalovaniaFakeTaps(clock, manager, taps);
                CheckDensity();
                CheckFriendsFakePerformance(clock);
                CheckInheritance(clock, counter);
                CheckMotion(clock);
                CheckReverseSV(clock);
                CheckSVPool(clock);
                CheckImageInheritance(clock);
                CheckMegalovaniaImages(clock);
                CheckInheritedHoldDirection(clock);
                CheckStreamEachLine(clock);
                CheckGuideStarOrder(clock);
                Debug.Log($"ALPHA FAKE CHECK PASS: {checks} runtime checks; independent rendering/recycling, no input/audio/score, contiguous real queues and unsupported geometry recovery.");
            }
            finally
            {
                MajdataPlay.Alpha.AlphaSimaiParser.Clear();
                taps.Dispose(); holds.Dispose(); touches.Dispose(); touchHolds.Dispose();
                UnityEngine.Object.DestroyImmediate(root);
                typeof(MajEnv).GetProperty("Settings").SetValue(null, settings);
            }
        }
        static void CheckNewNoteTransitions(Clock clock)
        {
            var chart = MajdataPlay.Alpha.AlphaSimaiParser.ParseChart("", "", "(120){4}<ANGLEV*tap=(0,90,2,linear)><SIZEV*(1,2,2,linear)><COLORV*(FF0000,0000FF,2,linear)><ALPHAV*(0,1,2,linear)>1,");
            var timeline = MajdataPlay.Alpha.AlphaSimaiParser.GetTimeline(chart);
            var tap = MakeNote<TapDrop>(clock);
            tap.AlphaTimeline = timeline;
            var sprite = (SpriteRenderer)Get(tap,"_thisRenderer");
            sprite.sharedMaterial = Resources.Load<Material>("AlphaNote");
            tap.transform.localPosition = new Vector3(2, 3, 0);
            tap.transform.localRotation = Quaternion.Euler(0, 0, 20);
            clock.Time = 1;
            for (var i=0; i<3; i++)
            {
                tap.ApplyAlphaVisuals();
                Require(Math.Abs(Mathf.DeltaAngle(tap.transform.localEulerAngles.z,65))<.001f,"ANGLEV real Tap orientation");
                Require(tap.transform.localPosition==new Vector3(2,3,0),"ANGLEV moved note position");
                Require(Vector3.Distance(tap.transform.localScale,new Vector3(1.5f,1.5f,1))<.001f,"Animated SIZE real Tap scale");
                var block=new MaterialPropertyBlock(); sprite.GetPropertyBlock(block);
                Require(Math.Abs(block.GetFloat("_NoteAlpha")-.5f)<.001f,"Animated ALPHA renderer");
                Require(Vector4.Distance(block.GetVector("_NoteColor"),new Vector4(128f/255,0,128f/255,1))<.001f,"Animated RGB renderer");
            }
            tap.RestoreAlphaVisualScales();
            Require(Math.Abs(Mathf.DeltaAngle(tap.transform.localEulerAngles.z,20))<.001f,"Native pose restored before pool reuse/input");
            Require(tap.transform.localScale==Vector3.one,"Native scale restored before pool reuse/input");
            tap.ApplyAlphaVisuals();
            var stopwatch=new System.Diagnostics.Stopwatch();
            var before=GC.GetAllocatedBytesForCurrentThread(); stopwatch.Start();
            for(var frame=0;frame<600;frame++) { clock.Time=frame/120f; tap.ApplyAlphaVisuals(); }
            stopwatch.Stop(); var allocated=GC.GetAllocatedBytesForCurrentThread()-before;
            Require(allocated==0,"Animated note drawing allocated each frame: "+allocated);
            Debug.Log($"PLAY NEW VISUAL PASS: real Tap pose/scale/RGB/opacity/reset; 600 Unity Mono updates, {stopwatch.Elapsed.TotalMilliseconds:F2} ms, {allocated} B");
            UnityEngine.Object.DestroyImmediate(tap.gameObject);
        }

        static void CheckSizeRendering(Clock clock)
        {
            clock.Time = 0;
            var timeline = MajdataPlay.Alpha.AlphaSimaiParser.GetTimeline(
                MajdataPlay.Alpha.AlphaSimaiParser.ParseChart("", "", "(120){4}<SIZE*(2,0.5)><SIZE*slide=(3,0.25)><SIZE*slidestar=(0.75,1.25)>1,"));
            foreach (var note in new NoteDrop[] { MakeNote<TapDrop>(clock), MakeNote<HoldDrop>(clock), MakeNote<TouchDrop>(clock), MakeNote<TouchHoldDrop>(clock) })
            {
                note.AlphaTimeline = timeline;
                note.transform.localScale = new Vector3(1.3f, 1.3f, 1);
                note.transform.localPosition = new Vector3(2, 1, 0);
                var child = new GameObject("tail or fan").transform;
                child.SetParent(note.transform, false); child.localPosition = new Vector3(0, -2, 0);
                var point = new GameObject("center"); point.transform.SetParent(note.transform, false);
                if (note is TouchHoldDrop) Set(note, "_pointObject", point);
                for (var repeat = 0; repeat < 3; repeat++)
                {
                    note.ApplyAlphaVisuals();
                    Require(Vector3.Distance(note.transform.localScale, new Vector3(2.6f, .65f, 1)) < .0001f,
                        note.GetType().Name + " SIZE must scale the whole note without accumulating");
                    Require(note.transform.localPosition == new Vector3(2, 1, 0) && child.localPosition == new Vector3(0, -2, 0),
                        "SIZE changed authored positions instead of complete note dimensions");
                    if (note is TouchHoldDrop) Require(Vector3.Distance(point.transform.lossyScale, new Vector3(1.3f, 1.3f, 1)) < .0001f,
                        "TouchHold SIZE enlarged the fixed center point");
                }
                note.RestoreAlphaVisualScales();
                Require(Vector3.Distance(note.transform.localScale, new Vector3(1.3f, 1.3f, 1)) < .0001f,
                    "SIZE did not restore native dimensions before judgment or pool reuse");
                UnityEngine.Object.DestroyImmediate(note.gameObject);
            }
            foreach (var slide in new SlideBase[] { MakeNote<SlideDrop>(clock), MakeNote<ExtendSlideDrop>(clock), MakeNote<WifiDrop>(clock) })
            {
            slide.AlphaTimeline = timeline;
            var bars = (MajdataPlay.Buffers.RentedList<SpriteRenderer>)Get(slide, "SlideBarRenderers");
            var bar = Renderer(); bar.transform.SetParent(slide.transform, false);
            bar.transform.localPosition = new Vector3(2, 3, 0); bar.transform.localScale = new Vector3(.6f, .8f, 1);
            bars.Add(bar);
            var stars = (Memory<GameObject>)Get(slide, "Stars");
            var star = Renderer(); star.transform.SetParent(root.transform, false);
            star.transform.localScale = new Vector3(1.5f, 1.5f, 1);
            stars.Span[0] = star.gameObject;
            var rootScale = slide.transform.localScale;
            for (var repeat = 0; repeat < 3; repeat++)
            {
                slide.ApplyAlphaVisuals();
                Require(Vector3.Distance(bar.transform.localScale, new Vector3(1.8f, .2f, 1)) < .0001f,
                    "Slide SIZE does not match each local View arrow scale");
                Require(Vector3.Distance(star.transform.localScale, new Vector3(1.125f, 1.875f, 1)) < .0001f,
                    "moving star must use slidestar SIZE independently of route SIZE");
                Require(slide.transform.localScale == rootScale && bar.transform.localPosition == new Vector3(2, 3, 0),
                    "Slide SIZE changed the route root or judgment path");
                var block = new MaterialPropertyBlock(); bar.GetPropertyBlock(block);
                Require(block.GetVector("_NoteSize") == Vector4.one, "Slide SIZE must not rescale batched shader vertices");
            }
            slide.AlphaExpression = "1-5[4:1]";
            Require(Vector3.Distance(bar.transform.localScale, new Vector3(.6f, .8f, 1)) < .0001f,
                "pool/expression change must restore the original arrow scale");
            UnityEngine.Object.DestroyImmediate(star.gameObject);
            UnityEngine.Object.DestroyImmediate(slide.gameObject);
            }
            Debug.Log("ALPHA SIZE CHECK PASS: local arrows, separate moving stars, complete Hold/Touch/TouchHold dimensions, fixed center, native judgment and pool reset.");
        }
        static T MakeNote<T>(Clock clock) where T : NoteDrop
        {
            var obj = new GameObject(typeof(T).Name);
            obj.transform.SetParent(root.transform);
            var note = obj.AddComponent<T>();
            Set(note, "_gameObject", obj); Set(note, "_transform", obj.transform);
            Set(note, "_noteController", clock); Set(note, "_state", NoteStatus.Inited);
            // Deliberately leave audio, effects, input and judge services unassigned.
            // Any accidental call into those paths fails this test immediately.
            for (var type = typeof(T); type != null; type = type.BaseType)
            foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (field.FieldType == typeof(SpriteRenderer)) field.SetValue(note, Renderer());
                else if (field.FieldType == typeof(SpriteMask)) field.SetValue(note, Child("mask").AddComponent<SpriteMask>());
                else if (field.FieldType == typeof(SpriteRenderer[]) && field.GetValue(note) is SpriteRenderer[] renderers)
                    for (var i = 0; i < renderers.Length; i++) renderers[i] = Renderer();
            }
            note.IsFake = true;
            note.Timing = 2;
            if (note is TapDrop tap) tap.RendererState = RendererStatus.On;
            return note;
        }
        static void CheckPooled<T>(Clock clock, NotePoolManager manager, ObjectCounter counter, IReturnCount pool, float length) where T : NoteDrop
        {
            var note = MakeNote<T>(clock);
            Set(note, "ObjectCounter", counter);
            if (note is NoteLongDrop longNote) longNote.Length = length;
            if (note is HoldDrop) Set(note, "_poolManager", manager);
            else Set(note, "_notePoolManager", manager);
            var pre = pool.Returns;
            clock.Time = 0;
            Invoke(note, "OnPreUpdate");
            Require(!note.IsEnded && pool.Returns == pre, typeof(T).Name + " expired before timing");
            clock.Time = 2;
            Invoke(note, "OnPreUpdate");
            Require(!note.IsEnded, typeof(T).Name + " did not retain its normal display lifetime");
            counter.ReportResult(note, new NoteJudgeResult { Grade = JudgeGrade.Perfect, IsBreak = true });
            Require(counter.CurrentNoteScore == 0, "Fake score feedback was not blocked");
            clock.Time = 5;
            Invoke(note, "OnPreUpdate");
            Require(note.IsEnded && pool.Returns == pre + 1, typeof(T).Name + " did not recycle exactly once");
            Invoke(note, "OnPreUpdate");
            Require(pool.Returns == pre + 1, typeof(T).Name + " was recycled twice");
        }
        static void CheckSlide<T>(Clock clock) where T : SlideBase
        {
            var slide = MakeNote<T>(clock);
            slide.IsMine = true; // Fake mines bypass all mine judgment paths too.
            slide.Timing = -1; slide.StartTiming = 0; slide.Length = 1;
            Set(slide, "_state", NoteStatus.Running);
            var areas = new SlideArea[4];
            for (var i = 0; i < areas.Length; i++)
                areas[i] = new SlideArea(new[] { ((SensorArea)i, false) }, (i + 1) * 2);
            ((Memory<SlideArea>[])Get(slide, "JudgeQueues"))[0] = areas;
            Set(slide, "JudgeQueueLength", 4);
            var bars = (MajdataPlay.Buffers.RentedList<GameObject>)Get(slide, "SlideBars");
            var renderers = (MajdataPlay.Buffers.RentedList<SpriteRenderer>)Get(slide, "SlideBarRenderers");
            for (var i = 0; i < 10; i++)
            {
                var bar = Child("bar"); bars.Add(bar); renderers.Add(bar.AddComponent<SpriteRenderer>());
            }
            var transforms = (Memory<Transform>)Get(slide, "StarTransforms");
            for (var i = 0; i < transforms.Length; i++) transforms.Span[i] = Child("star").transform;
            if (slide is SingleSlideBase)
            {
                ((MajdataPlay.Buffers.RentedList<Vector3>)Get(slide, "StarPositions")).Add(Vector3.zero);
                ((MajdataPlay.Buffers.RentedList<Vector3>)Get(slide, "StarPositions")).Add(Vector3.right);
                ((MajdataPlay.Buffers.RentedList<Quaternion>)Get(slide, "StarRotations")).Add(Quaternion.identity);
                ((MajdataPlay.Buffers.RentedList<Quaternion>)Get(slide, "StarRotations")).Add(Quaternion.identity);
            }
            else
            {
                var starts = (Vector3[])Get(slide, "_starStartPositions");
                var ends = (Vector3[])Get(slide, "_starEndPositions");
                for (var i = 0; i < starts.Length; i++) { starts[i] = Vector3.zero; ends[i] = Vector3.right; }
            }
            clock.Time = 0.5f;
            Invoke(slide, "OnPreUpdate"); Invoke(slide, "OnUpdate");
            Require(!slide.IsEnded && Math.Abs(transforms.Span[0].position.x - 0.5f) < 0.001f,
                typeof(T).Name + " fake guide star did not follow its path");
            var hidden = slide is WifiDrop ? 5 : 4;
            Require(bars[0].layer == MajEnv.HIDDEN_LAYER && bars[hidden].layer == MajEnv.DEFAULT_LAYER,
                typeof(T).Name + " fake track did not erase behind its star");
            Require(!areas[0].IsFinished, "Fake rendering consumed a judgment area");
            clock.Time = 1.01f;
            Invoke(slide, "OnPreUpdate");
            Require(slide.IsEnded && slide.IsFinished, typeof(T).Name + " fake slide did not clean up at its endpoint");
            foreach (var area in areas) area.Dispose();
        }
        static void CheckAlphaMineTrail(Clock clock)
        {
            MajdataPlay.Alpha.AlphaSimaiParser.Clear();
            foreach (var token in new[] { "A1>A4m[4:1]", "A1m>A4m[4:1]", "A1m>A4[4:1]", "A1-A5m[4:1]", "B1?<B5>B8m[1.02##18.36]", "D1?<D5>D1<D5>D1<D5>D1<D5>D1<D5>D1<D5>D1<D5>D1<D5>D1<D5>D1m[2.04##16.32]" })
            {
                var parsed = MajdataPlay.Alpha.AlphaPathParser.Parse(0, 120, token);
                Require(parsed != null && parsed.IsMineSlide == !token.Contains("A1m>A4["),
                    "TouchSlide mine parser lost body/head distinction: " + token);
                if (!parsed.IsMineSlide) continue;
                MajdataPlay.Alpha.AlphaPathParser.TryRead(token, out var expression);
                var metadata = MajdataPlay.Alpha.AlphaPathMetadata.Build(expression.path);
                var slide = MakeNote<ExtendSlideDrop>(clock);
                slide.Metadata = metadata; slide.IsFake = false; slide.IsMine = true;
                slide.Timing = -1; slide.StartTiming = (float)parsed.SlideStartTime; slide.Length = (float)parsed.SlideTime;
                Set(slide, "_state", NoteStatus.Running);
                typeof(ExtendSlideDrop).GetMethod("InitSlideTable", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(slide, new object[] { metadata });
                var bars = (MajdataPlay.Buffers.RentedList<GameObject>)Get(slide, "SlideBars");
                var renderers = (MajdataPlay.Buffers.RentedList<SpriteRenderer>)Get(slide, "SlideBarRenderers");
                for (var i = 0; i < metadata.ArrowPoses.Length - 2; i++)
                { var bar = Child("mine arrow"); bars.Add(bar); renderers.Add(bar.AddComponent<SpriteRenderer>()); }
                var positions = (MajdataPlay.Buffers.RentedList<Vector3>)Get(slide, "StarPositions");
                var rotations = (MajdataPlay.Buffers.RentedList<Quaternion>)Get(slide, "StarRotations");
                foreach (var pose in metadata.ArrowPoses)
                { positions.Add(new Vector3(pose.X, pose.Y)); rotations.Add(Quaternion.Euler(0, 0, pose.RotZ)); }
                var stars = (Memory<GameObject>)Get(slide, "Stars");
                var transforms = (Memory<Transform>)Get(slide, "StarTransforms");
                var star = Child("mine guide"); star.AddComponent<SpriteRenderer>();
                stars.Span[0] = star; transforms.Span[0] = star.transform;
                var originalQueueLength = slide.QueueRemaining;
                clock.Time = slide.StartTiming + slide.Length * .5f;
                Invoke(slide, "OnUpdate");
                Require(slide.QueueRemaining == originalQueueLength && !(bool)Get(slide, "IsJudged"),
                    "Timed mine rendering consumed the real judgment queue");
                Require(bars.Any(b => b.layer == MajEnv.HIDDEN_LAYER) && bars.Any(b => b.layer == MajEnv.DEFAULT_LAYER),
                    "Mine path did not erase progressively behind guide");
                Require(Vector3.Distance(star.transform.position, positions[positions.Count / 2]) < .4f,
                    "TouchSlide mine guide did not move along actual geometry");
                clock.Time = slide.StartTiming + slide.Length * (1 - metadata.SlideConst) + .001f;
                Invoke(slide, "OnUpdate");
                Require((bool)Get(slide, "IsJudged") && (JudgeGrade)Get(slide, "JudgeResult") == JudgeGrade.Perfect && slide.QueueRemaining == originalQueueLength,
                    "Safe mine lost native Perfect or consumed the sensor queue");
                clock.Time = slide.StartTiming + slide.Length;
                slide.RefreshAlphaMineTrail();
                Require(bars.All(b => b.layer == MajEnv.HIDDEN_LAYER), "Mine visual path leaked beyond its endpoint");
                UnityEngine.Object.DestroyImmediate(slide.gameObject);
            }
            CheckAlphaMineContact(clock);
            Debug.Log("ALPHA TOUCHSLIDE MINE CHECK PASS: timed guide/trail, safe native Perfect, forced sensor contact fails including safe-boundary frame, head/body distinction, unchanged judge queue.");
        }

        static void CheckAlphaMineContact(Clock clock)
        {
            var managerObject = Child("mine contact sensors");
            var manager = managerObject.AddComponent<NoteManager>();
            var sensors = (SwitchStatus[])Get(manager, "_sensorStatusInThisFrame");
            foreach (var token in new[] { "A1>A4m[4:1]", "A1-A5m[4:1]" })
            foreach (var atBoundary in new[] { false, true })
            {
                MajdataPlay.Alpha.AlphaPathParser.TryRead(token, out var expression);
                var metadata = MajdataPlay.Alpha.AlphaPathMetadata.Build(expression.path);
                var slide = MakeNote<ExtendSlideDrop>(clock);
                slide.Metadata = metadata; slide.IsFake = false; slide.IsMine = true;
                slide.Timing = -1; slide.StartTiming = 0; slide.Length = 1;
                Set(slide, "NoteManager", manager); Set(slide, "IsCheckable", true);
                Set(slide, "IsSoundPlayed", true); Set(slide, "_state", NoteStatus.Running);
                typeof(ExtendSlideDrop).GetMethod("InitSlideTable", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(slide, new object[] { metadata });
                var transforms = (Memory<Transform>)Get(slide, "StarTransforms");
                transforms.Span[0] = Child("contact guide").transform;
                ((MajdataPlay.Buffers.RentedList<Vector3>)Get(slide, "StarPositions")).Add(Vector3.zero);
                ((MajdataPlay.Buffers.RentedList<Vector3>)Get(slide, "StarPositions")).Add(Vector3.right);
                ((MajdataPlay.Buffers.RentedList<Quaternion>)Get(slide, "StarRotations")).Add(Quaternion.identity);
                ((MajdataPlay.Buffers.RentedList<Quaternion>)Get(slide, "StarRotations")).Add(Quaternion.identity);
                clock.Time = .25f;
                Array.Clear(sensors, 0, sensors.Length);
                for (var i = 0; i < metadata.JudgeAreaQueue.Length; i++)
                {
                    sensors[(int)metadata.JudgeAreaQueue[i].SensorA] = SwitchStatus.On;
                    if (i == metadata.JudgeAreaQueue.Length - 1 && atBoundary)
                        clock.Time = slide.StartTiming + slide.Length * (1 - metadata.SlideConst) + .001f;
                    // OnUpdate reads the real native sensor queue and resolves
                    // that frame's contact before awarding a safe result.
                    Invoke(slide, "OnUpdate");
                    sensors[(int)metadata.JudgeAreaQueue[i].SensorA] = SwitchStatus.Off;
                    Invoke(slide, "OnUpdate");
                }
                var grade = (JudgeGrade)Get(slide, "JudgeResult");
                Require(slide.IsFinished && (bool)Get(slide, "IsJudged") &&
                    (grade == JudgeGrade.Miss || grade == JudgeGrade.TooFast),
                    $"Touched mine became safe JUST: {token}, boundary={atBoundary}, grade={grade}");
                UnityEngine.Object.DestroyImmediate(slide.gameObject);
            }
            UnityEngine.Object.DestroyImmediate(managerObject);
        }

        static void CheckSVPool(Clock clock)
        {
            var source = "(168){8}1,<SV*5>,,,,,,<SV*-0.3>{16}" + string.Concat(Enumerable.Repeat("6,5,", 56)) +
                "<SV*3>,,,,,,,<SV*-0.3>" + string.Concat(Enumerable.Repeat("6,5,", 56)) + "<SV*2>1,";
            // Reproduce the reported song when it is locally available; do not ship its chart.
            var chartFile = @"D:\Maimai\release\MajdataPlayAlpha\MaiCharts\Alpha\7wonders\maidata.txt";
            var actual = System.IO.File.Exists(chartFile);
            if (actual)
            {
                var match = System.Text.RegularExpressions.Regex.Match(System.IO.File.ReadAllText(chartFile),
                    @"(?ms)^&inote_5=(.*?)(?=^&[a-zA-Z0-9_]+=|\z)");
                if (match.Success) source = match.Groups[1].Value;
                else actual = false;
            }
            var chart = MajdataPlay.Alpha.AlphaSimaiParser.ParseChart("14+", "test", source);
            MajdataPlay.Alpha.AlphaSimaiParser.Activate(chart);
            var loader = root.GetComponent<NoteLoader>();
            var indices = (Dictionary<int, int>)typeof(NoteLoader).GetField("_noteIndex", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            for (var i = 1; i <= 16; i++) indices[i] = 0;
            var create = typeof(NoteLoader).GetMethod("CreateTap", BindingFlags.Instance | BindingFlags.NonPublic);
            var infos = new List<TapPoolingInfo>();
            foreach (var timing in chart.NoteTimings)
                foreach (var note in timing.Notes)
                    if (note.Type == MajSimai.SimaiNoteType.Tap)
                        infos.Add((TapPoolingInfo)create.Invoke(loader, new object[] { note, timing }));
            var required = MajdataPlay.Alpha.AlphaPoolSizing.RequiredCapacity(infos, 96);
            Require(required > 96, "Negative/positive SV reproduction did not exercise native pool exhaustion");
            var points = infos.Select(n => n.AppearTiming - .15f).Distinct().ToArray();
            Require(points.All(t => infos.Count(n => n.AppearTiming - .15f <= t && n.Timing + .6f >= t) <= required),
                "SV pool still cannot fit concurrently revealed notes");
            Require(MajdataPlay.Alpha.AlphaPoolSizing.RequiredCapacity(new[] { new TapPoolingInfo() }, 96) == 96,
                "Native chart pool size changed");
            foreach (var change in MajdataPlay.Alpha.AlphaSimaiParser.Current.Changes.Where(c => c.Command == MajdataPlay.Alpha.AlphaVisualTimeline.Kind.SV && c.Type == "" && c.Value < 0).Take(2))
            {
                var info = infos.First(n => n.Timing > change.Time + .5);
                var tap = MakeNote<TapDrop>(clock); tap.Timing = info.Timing; tap.Speed = info.Speed; tap.AlphaTimeline = info.AlphaTimeline;
                Set(tap, "_outerPos", MajdataPlay.Scenes.Game.Utils.NoteHelper.GetTapPosition(info.StartPos, 4.8f));
                Set(tap, "USERSETTING_TAP_SCALE", 1f);
                var line = (SpriteRenderer)Get(tap, "_tapLineRenderer");
                Set(tap, "_tapLineTransform", line.transform); Set(tap, "_tapLineObject", line.gameObject);
                clock.Time = info.Timing - .2f; tap.OnUpdate();
                Require(tap.transform.position.magnitude > 4.8f, "SV reversal Tap did not approach outside the ring");
                Require(((SpriteRenderer)Get(tap, "_thisRenderer")).enabled, "SV reversal Tap renderer disabled");
                Require(info.AppearTiming - .15 <= clock.Time, "SV reversal Tap allocated after it needed to be visible");
                Debug.Log($"SV REVERSAL TAP: target={info.Timing:F3}, radius={tap.transform.position.magnitude:F3}, appears={info.AppearTiming:F3}");
                UnityEngine.Object.DestroyImmediate(tap.gameObject);
            }
            Debug.Log($"ALPHA SV POOL CHECK PASS: {(actual ? "7wonders difficulty 5" : "negative-positive-negative sequence")}, {infos.Count} taps, capacity 96 -> {required}, all reveal intervals fit.");
            MajdataPlay.Alpha.AlphaSimaiParser.Clear();
        }
        static void CheckLoaderQueues()
        {
            var loader = root.AddComponent<NoteLoader>();
            var noteIndex = (Dictionary<int, int>)typeof(NoteLoader).GetField("_noteIndex", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            noteIndex.Clear();
            for (var i = 1; i <= 8; i++) noteIndex.Add(i, 0);
            var chart = MajdataPlay.Alpha.AlphaSimaiParser.ParseChart("14", "test", "(120){4}<FAKE*true>1,<FAKE*false>1,1,");
            MajdataPlay.Alpha.AlphaSimaiParser.Activate(chart);
            var create = typeof(NoteLoader).GetMethod("CreateTap", BindingFlags.Instance | BindingFlags.NonPublic);
            var infos = new TapPoolingInfo[3];
            for (var i = 0; i < 3; i++)
            {
                var timing = chart.NoteTimings[i];
                infos[i] = (TapPoolingInfo)create.Invoke(loader, new object[] { timing.Notes[0], timing });
            }
            Require(infos[0].IsFake && infos[0].QueueInfo.Index == -1 && infos[1].QueueInfo.Index == 0 && infos[2].QueueInfo.Index == 1,
                "Fake note consumed a real input queue index");
            var typedHS = MajdataPlay.Alpha.AlphaSimaiParser.ParseChart("", "", "(120){4}<HS*tap=3,star=2>1$,1,");
            MajdataPlay.Alpha.AlphaSimaiParser.Activate(typedHS);
            for(var i=0;i<2;i++)
            {
                var timing=typedHS.NoteTimings[i];
                var info=(TapPoolingInfo)create.Invoke(loader,new object[]{timing.Notes[0],timing});
                Require(Math.Abs(info.Speed/loader.NoteSpeed-(i==0?2:3))<.001f,
                    "Forced star must use star HS, not tap HS");
            }
            var streamHead = MajdataPlay.Alpha.AlphaSimaiParser.ParseChart("", "",
                "(120){4},\n@{4}<SPAWN*4.8><FAKE*true><ALPHA*star=0>4m-1m[0##0.1],\n1,");
            MajdataPlay.Alpha.AlphaSimaiParser.Activate(streamHead);
            var headTiming = streamHead.NoteTimings.ToArray().Single(t=>t.Notes.Any(n=>n.Type==SimaiNoteType.Slide));
            var original = headTiming.Notes.First(n=>n.Type==SimaiNoteType.Slide);
            var segment = (SimaiNote)Activator.CreateInstance(typeof(NoteLoader).GetNestedType("SubSlideNote",BindingFlags.NonPublic),true);
            segment.GetType().GetProperty("Origin").SetValue(segment,original);
            segment.Type=SimaiNoteType.Slide; segment.RawContent="4-1[0##0.1]"; segment.SlideTime=.1;
            var head = (TapPoolingInfo)typeof(NoteLoader).GetMethod("CreateStar",BindingFlags.Instance|BindingFlags.NonPublic)
                .Invoke(loader,new object[]{4,segment,headTiming});
            Require(head.IsFake && head.IsMine && head.QueueInfo.Index==-1,"Split Slide head lost original Fake/Mine stream state");
            Require(head.AlphaExpression==original.RawContent && head.AlphaTimeline==MajdataPlay.Alpha.AlphaSimaiParser.ForNote(original),
                "Split Slide head lost original expression/timeline");
            Require(head.AlphaTimeline.Radius(true,head.Timing,"star",false,false,true)==4.8f &&
                head.AlphaTimeline.Alpha("star",head.Timing,head.Timing,false,false,true)==0,
                "Split Slide head lost SPAWN or invisible-head ALPHA");
            var completion=Child("Fake stream completion counter").AddComponent<ObjectCounter>();
            completion.CountNoteSums(streamHead);
            var fakeHead=MakeNote<TapDrop>(new Clock()); fakeHead.IsFake=head.IsFake;
            completion.ReportResult(fakeHead,new NoteJudgeResult{Grade=JudgeGrade.Perfect});
            Require(completion.TapSum==1 && completion.SlideSum==0 && !completion.AllFinished,
                "Fake stream head changed the real chart completion count");
            var realHead=MakeNote<TapDrop>(new Clock()); realHead.IsFake=false;
            var countResult=(JudgeCount<TapDrop>)typeof(ObjectCounter).GetMethod("UpdateJudgeCount",BindingFlags.Instance|BindingFlags.NonPublic)
                .MakeGenericMethod(typeof(TapDrop)).CreateDelegate(typeof(JudgeCount<TapDrop>),completion);
            countResult(realHead,new NoteJudgeResult{Grade=JudgeGrade.Perfect},1);
            Require(completion.AllFinished && MajdataPlay.Alpha.AlphaSimaiParser.Current.EnvironmentFinished(headTiming.Timing),
                "A completed real Tap plus a Fake stream cannot enter native end/Skip handling");
            UnityEngine.Object.DestroyImmediate(fakeHead.gameObject); UnityEngine.Object.DestroyImmediate(realHead.gameObject);
            UnityEngine.Object.DestroyImmediate(completion.gameObject);
            SlideDataBuilder.InitializeSlideAreaLookup();
            var geometry = MajdataPlay.Alpha.AlphaSimaiParser.ParseChart("14", "test", "(120){4}1-2[4:1]/3,1-3-5[4:1],8P9K1[4:1],");
            var filtered = NoteLoader.FilterUnsupportedNotes(geometry);
            Require(filtered.NoteTimings[0].Notes.Length == 1 && filtered.NoteTimings[0].Notes[0].StartPosition == 3,
                "Unsupported geometry removed a valid same-slot sibling");
            Require(filtered.NoteTimings[1].Notes.Length == 1 && filtered.NoteTimings[2].Notes.Length == 1,
                "Valid chain or SlideCode was discarded by geometry recovery");
            Require(MajdataPlay.Alpha.AlphaSimaiParser.GetWarnings(filtered).Count == 1, "Geometry recovery did not record a warning");
            var unsupportedChains = MajdataPlay.Alpha.AlphaSimaiParser.ParseChart("14", "test",
                "(120){4}1w5-3[4:1]/4,1-3[4:1]-5/6,1-3[4:1]-5[4:1],");
            var recoveredChains = NoteLoader.FilterUnsupportedNotes(unsupportedChains);
            Require(recoveredChains.NoteTimings[0].Notes.Length == 1 && recoveredChains.NoteTimings[0].Notes[0].StartPosition == 4,
                "An unsupported WiFi chain removed a valid sibling");
            Require(recoveredChains.NoteTimings[1].Notes.Length == 1 && recoveredChains.NoteTimings[1].Notes[0].StartPosition == 6,
                "Invalid chain timing removed a valid sibling");
            Require(recoveredChains.NoteTimings[2].Notes.Length == 1 && MajdataPlay.Alpha.AlphaSimaiParser.GetWarnings(recoveredChains).Count == 2,
                "Supported per-section chain timing was discarded");
        }
        static GameObject Child(string name)
        {
            var child = new GameObject(name); child.transform.SetParent(root.transform); return child;
        }
        static void CheckFakeAnswerSounds()
        {
            var chart=MajdataPlay.Alpha.AlphaSimaiParser.ParseChart("","",
                "(120){4}<FAKE*True>1,2h[4:1],3m-7m[4:1],<FAKE*False>4,<FAKE*star=True>5-1[4:1],");
            var audio=root.AddComponent<NoteAudioManager>();
            System.Threading.Tasks.Task.Run(()=>audio.GenerateAnswerSFX(chart,false,0)).GetAwaiter().GetResult();
            var memory=Get(audio,"_answerTimingPoints");
            var points=(Array)memory.GetType().GetMethod("ToArray").Invoke(memory,null);
            var point=points.GetValue(0); var pointType=point.GetType();
            Require(points.Length==1 && Math.Abs((float)pointType.GetField("Timing").GetValue(point)-1.5f)<.001f &&
                (bool)pointType.GetField("IsNormal").GetValue(point) && !(bool)pointType.GetField("IsMine").GetValue(point),
                "Answer audio contains Fake Tap/Hold/Mine/head or removed the real Tap");
        }
        static void CheckMegalovaniaFakeTaps(Clock clock, NotePoolManager manager, IReturnCount pool)
        {
            var args=Environment.GetCommandLineArgs();
            var index=Array.IndexOf(args,"-alphaImageFixture");
            if(index<0) return;
            using var input=System.IO.File.OpenRead(System.IO.Path.Combine(args[index+1],"maidata.txt"));
            var chart=MajdataPlay.Alpha.AlphaSimaiParser.ParseChart("","",SimaiParser.ParseMetadata(input).Fumens[4]);
            MajdataPlay.Alpha.AlphaSimaiParser.Activate(chart);
            var mines=0; var ordinary=0;
            foreach(var timing in chart.NoteTimings)
            foreach(var source in timing.Notes)
            {
                if(source.Type!=SimaiNoteType.Tap || !MajdataPlay.Alpha.AlphaFakeNotes.IsFake(source,timing)) continue;
                var timeline=MajdataPlay.Alpha.AlphaSimaiParser.ForNote(source);
                var each=MajdataPlay.Alpha.AlphaSimaiParser.StateEach(source) ?? timing.Notes.Length>1;
                if(timeline.Radius(false,timing.Timing,"tap",each,source.IsBreak,source.IsMine)!=0) continue;
                var tap=MakeNote<TapDrop>(clock);
                tap.Timing=(float)timing.Timing;tap.IsMine=source.IsMine;
                Set(tap,"_notePoolManager",manager);
                var before=pool.Returns;
                clock.Time=tap.Timing-.001f; Invoke(tap,"OnPreUpdate");
                Require(!tap.IsEnded,"Fake Tap disappeared before its beat: "+source.RawContent);
                clock.Time=tap.Timing; Invoke(tap,"OnPreUpdate");
                Require(tap.IsEnded==source.IsMine,"Fake Mine must disappear at its beat; ordinary Fake retains View's miss window");
                clock.Time=tap.Timing+.151f; Invoke(tap,"OnPreUpdate");
                Require(tap.IsEnded && pool.Returns==before+1,"Fake Tap did not recycle exactly once");
                if(source.IsMine) mines++; else ordinary++;
                UnityEngine.Object.DestroyImmediate(tap.gameObject);
            }
            Require(mines>0 && ordinary>0,"MEGALOVANIA no longer exercises both Fake Mine and ordinary DESTROY=0 Tap");
            Debug.Log($"MEGALOVANIA FAKE LIFETIME PASS: {mines} zero-destroy Mine heads disappear at their beat; {ordinary} ordinary Fake Taps keep View's 0.15s window; no input/audio/score calls.");
        }
        static void CheckInheritance(Clock clock, ObjectCounter counter)
        {
            var chart = MajdataPlay.Alpha.AlphaSimaiParser.ParseChart("14", "test", "(120){4}1~[5-7[8:1]],A1~[A1>A4[8:1]],");
            counter.CountNoteSums(chart);
            Invoke(counter, "UpdateAccRate");
            Require(counter.TotalNoteScore == 0 && counter.TapSum == 0 && counter.SlideSum == 0 && counter.AllFinished,
                "Borrowed trajectories entered native score/count");
            Require(MajdataPlay.Utils.ChartAnalyzer.AnalyzeMaidata(chart, 2).PeakDensity == 0,
                "Borrowed trajectories entered density");
            var sprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0,0,1,1), new Vector2(.5f,.5f));
            var oldClock = Majdata<INoteController>.Instance;
            Majdata<INoteController>.Instance = clock;
            MajdataPlay.Alpha.AlphaSimaiParser.Activate(chart);
            try
            {
                var timing = chart.NoteTimings[0];
                var obj = Child("Visual inheritance");
                var carrier = obj.AddComponent<MajdataPlay.Alpha.AlphaTrajectoryCarrier>();
                carrier.Configure(timing.Notes[0], timing, sprite, sprite);
                Require(obj.GetComponent<SpriteRenderer>().sortingLayerName=="Tap" &&
                    root.GetComponentsInChildren<SpriteRenderer>().Any(r=>r.name=="InheritedNoteGuide" && r.sortingLayerName=="TapLines"),
                    "Inherited Tap is behind the background cover instead of native note layers");
                Require(obj.GetComponent<NoteDrop>() == null, "Carrier created a judge note/head");
                clock.Time = -.1f; Invoke(carrier, "LateUpdate");
                Require(obj.GetComponent<SpriteRenderer>().forceRenderingOff, "Carrier appeared before its slot");
                clock.Time = .125f; Invoke(carrier, "LateUpdate");
                Require(!obj.GetComponent<SpriteRenderer>().forceRenderingOff && Vector3.Distance(obj.transform.localPosition, carrier.Evaluate(.5f)) < .001f,
                    "Carrier did not follow the borrowed trajectory");
                Require(counter.CurrentNoteScore == 0, "Carrier rendering produced score feedback");
                UnityEngine.Object.DestroyImmediate(obj);
                Require(root.GetComponentsInChildren<MajdataPlay.Alpha.AlphaTrajectoryCarrier>().Length == 0,
                    "Carrier did not recycle independently");
                var pathChart = MajdataPlay.Alpha.AlphaSimaiParser.ParseChart("", "", "(120){4}A1~[A1>A4[8:1]],");
                Require(NoteLoader.FilterUnsupportedNotes(pathChart).NoteTimings[0].Notes.Length == 1,
                    "Valid inherited TouchSlide geometry was discarded");
            }
            finally { Majdata<INoteController>.Instance = oldClock; UnityEngine.Object.DestroyImmediate(sprite); }
        }
        static void CheckMotion(Clock clock)
        {
            var chart = MajdataPlay.Alpha.AlphaSimaiParser.ParseChart("", "", "(120){4}<BOUNCE*1:1>1,,,,1,");
            MajdataPlay.Alpha.AlphaSimaiParser.Activate(chart);
            var hold = MakeNote<HoldDrop>(clock); hold.Speed = 10; hold.Length = .125f;
            Set(hold, "_tapLineTransform", Child("hold guide").transform);
            Set(hold, "_endTransform", Child("hold end").transform);
            Set(hold, "_outerPos", new Vector3(0,4.8f,0));
            Set(hold, "_tapLineObject", Child("hold guide object"));
            Set(hold, "_endObject", Child("hold end object"));
            clock.Time = 1.5f; Invoke(hold, "OnUpdate");
            var before = ((SpriteRenderer)Get(hold,"_thisRenderer")).size.y;
            clock.Time = 2.0625f; Invoke(hold, "OnUpdate");
            var after = ((SpriteRenderer)Get(hold,"_thisRenderer")).size.y;
            Require(Math.Abs(before-2.65f)<.001f && Math.Abs(after-2.025f)<.001f,
                "Short bounce hold body must retract at authored speed");
            UnityEngine.Object.DestroyImmediate(hold.gameObject);
            var reversed = MajdataPlay.Alpha.AlphaSimaiParser.ParseChart("", "", "(120){4}<SV*-1>1,,,,<SV*1>2,");
            MajdataPlay.Alpha.AlphaSimaiParser.Activate(reversed);
            var slide = MakeNote<SlideDrop>(clock); slide.Timing=2;
            Set(slide,"FadeInTiming",1.7f);
            var bars = (MajdataPlay.Buffers.RentedList<SpriteRenderer>)Get(slide,"SlideBarRenderers");
            var barRenderer = Renderer(); barRenderer.color = Color.clear; bars.Add(barRenderer);
            Set(slide,"FadeInCompletedTiming",2f); Set(slide,"FadeInCutoffTiming",1.9f);
            clock.Time=0;
            typeof(SlideBase).GetMethod("SlideBarFadeIn", BindingFlags.Instance|BindingFlags.NonPublic).Invoke(slide,null);
            Require(bars[0].color.a == 0, "Global negative SV made slide appear before the real head clock");
            foreach (var mode in new[] { "Once", "Rewind" })
            {
                var spawnChart = MajdataPlay.Alpha.AlphaSimaiParser.ParseChart("", "", "(120){4}<SPAWNMODE*" + mode + ">1,,<SV*-1>1,,<SV*1>1,,1,");
                MajdataPlay.Alpha.AlphaSimaiParser.Activate(spawnChart);
                var tap = MakeNote<TapDrop>(clock); tap.Timing = 3; tap.Speed = 4;
                clock.Time = 1;
                Require(tap.VisualPresentationScale == 1, "Spawn test did not cross ring");
                clock.Time = 2;
                Require(mode == "Once" ? tap.VisualPresentationScale == 1 : tap.VisualPresentationScale < 1,
                    "SPAWNMODE must preserve or rewind scale together with radius");
                Require(Math.Abs(tap.VisualPresentationRadius - (mode == "Once" ? .8f : 1.225f)) < .001f,
                    "SPAWNMODE radius mismatched scale");
                UnityEngine.Object.DestroyImmediate(tap.gameObject);
            }
        }
        static void CheckReverseSV(Clock clock)
        {
            var chart = MajdataPlay.Alpha.AlphaSimaiParser.ParseChart("", "", "(120){4}<SV*-1>1,,,,1,");
            MajdataPlay.Alpha.AlphaSimaiParser.Activate(chart);
            var cameraObject = Child("reverse SV camera");
            var camera = cameraObject.AddComponent<Camera>(); camera.enabled = false;
            camera.orthographic = true; camera.orthographicSize = 8; camera.aspect = 1;
            camera.transform.position = new Vector3(0, 0, -10);
            camera.cullingMask = 1 << 31; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
            cameraObject.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing = false;
            var target = RenderTexture.GetTemporary(256, 256, 0, RenderTextureFormat.ARGB32);
            var capture = new Texture2D(256, 256, TextureFormat.RGBA32, false);
            var oldTarget = RenderTexture.active;
            var white = new Texture2D(16, 16); white.SetPixels(Enumerable.Repeat(Color.white, 256).ToArray()); white.Apply();
            var sprite = Sprite.Create(white, new Rect(0, 0, 16, 16), new Vector2(.5f, .5f), 16);
            var snapshots = new List<string>();
            try
            {
                for (var key = 1; key <= 8; key++)
                {
                    var tap = MakeNote<TapDrop>(clock); tap.Timing = 2; tap.Speed = 8;
                    Set(tap, "_outerPos", MajdataPlay.Scenes.Game.Utils.NoteHelper.GetTapPosition(key, 4.8f));
                    Set(tap, "_noteAppearRate", .265f); Set(tap, "USERSETTING_TAP_SCALE", 1f);
                    var renderer = (SpriteRenderer)Get(tap, "_thisRenderer"); renderer.transform.SetParent(tap.transform, false);
                    renderer.gameObject.layer = 31; renderer.sprite = sprite; renderer.color = Color.cyan;
                    var line = (SpriteRenderer)Get(tap, "_tapLineRenderer");
                    Set(tap, "_tapLineTransform", line.transform); Set(tap, "_tapLineObject", line.gameObject);
                    var previousRadius = float.PositiveInfinity;
                    foreach (var time in new[] { 1.7f, 1.9f, 1.99f })
                    {
                        clock.Time = time; tap.OnUpdate();
                        var expected = 4.8f + (2 - time) * 8;
                        Require(Math.Abs(tap.transform.position.magnitude - expected) < .001f, "Reverse SV radial position differs from ViewAlpha");
                        Require(tap.transform.position.magnitude < previousRadius && tap.transform.position.magnitude > 4.8f,
                            "Pure negative SV did not approach from outside judgment ring");
                        Require(renderer.enabled && tap.transform.localScale.x == 1, "Reverse SV tap was hidden/shrunk outside the ring");
                        previousRadius = tap.transform.position.magnitude;
                        UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
                        RenderTexture.active = target; capture.ReadPixels(new Rect(0, 0, 256, 256), 0, 0); capture.Apply();
                        Require(capture.GetPixels().Count(c => c.g > .4f && c.b > .4f) > 30, "Negative SV Tap did not render in actual camera pixels");
                    }
                    snapshots.Add(key + ":7.2->5.6->4.88");
                    UnityEngine.Object.DestroyImmediate(tap.gameObject);
                }
                Debug.Log("ALPHA REVERSE SV CHECK PASS: 8 Tap directions approach from outside, native renderer/camera pixels visible; " + string.Join(";", snapshots));
            }
            finally
            {
                RenderTexture.active = oldTarget; RenderTexture.ReleaseTemporary(target);
                UnityEngine.Object.DestroyImmediate(capture); UnityEngine.Object.DestroyImmediate(sprite); UnityEngine.Object.DestroyImmediate(white);
                UnityEngine.Object.DestroyImmediate(cameraObject); MajdataPlay.Alpha.AlphaSimaiParser.Clear();
            }
        }

        static void CheckImageInheritance(Clock clock)
        {
            var folder = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath,"..","Temp","ImageInheritanceCheck"));
            System.IO.Directory.CreateDirectory(folder);
            var texture = new Texture2D(16,16);
            texture.SetPixels(System.Linq.Enumerable.Repeat(Color.cyan,256).ToArray()); texture.Apply();
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(folder,"skin.png"),texture.EncodeToPNG());
            var model = Sprite.Create(Texture2D.whiteTexture,new Rect(0,0,1,1),new Vector2(.5f,.5f));
            MajdataPlay.Alpha.NoteSkinLibrary.SetChartFolder(folder);
            MajdataPlay.Alpha.AlphaSimaiParser.Clear();
            try
            {
                foreach (var kind in new[] { "tap", "touch", "touchhold", "slide" })
                {
                    NoteDrop note = kind == "tap" ? MakeNote<TapDrop>(clock) : kind == "touch" ? MakeNote<TouchDrop>(clock) :
                        kind == "touchhold" ? MakeNote<TouchHoldDrop>(clock) : MakeNote<SlideDrop>(clock);
                    SpriteRenderer[] targets;
                    if(note is TouchDrop touch) targets=touch.InheritedSkinRenderers;
                    else if(note is TouchHoldDrop touchHold) targets=touchHold.InheritedSkinRenderers;
                    else if(note is SlideBase slide)
                    {
                        var bars = (MajdataPlay.Buffers.RentedList<SpriteRenderer>)Get(slide,"SlideBarRenderers");
                        bars.Add(Renderer()); bars.Add(Renderer()); targets = new[]{bars[0],bars[1]};
                    }
                    else targets = new[]{(SpriteRenderer)Get(note,"_thisRenderer")};
                    foreach(var target in targets) target.sprite=model;
                    note.AlphaExpression = kind == "tap" ? "1~[skin.png]" : kind == "touch" ? "A1~[skin.png]" : kind == "touchhold" ? "Ch[8:1]~[skin.png]" : "1-5~[skin.png][8:1]";
                    note.ApplyAlphaVisuals();
                    Require(System.Linq.Enumerable.All(targets,r=>r.sprite && r.sprite!=model && r.sprite.texture.width==16),
                        kind+" image did not replace the appropriate sprite");
                    var expectedWidth = kind == "tap" ? MajdataPlay.Alpha.NoteSkinLibrary.ViewTapWidth : model.bounds.size.x;
                    Require(System.Linq.Enumerable.All(targets,r=>Math.Abs(r.sprite.bounds.size.x-expectedWidth)<.0001f),
                        kind+" image changed reference dimensions");
                    UnityEngine.Object.DestroyImmediate(note.gameObject);
                }
                var hold=MakeNote<HoldDrop>(clock); hold.AlphaExpression="1h[8:1]~[skin.png]";
                var inherited=(Sprite)typeof(NoteDrop).GetMethod("InheritedHeadSprite",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(hold,new object[]{model});
                Require(inherited && inherited!=model && inherited.texture.width==16,"Hold active/off skin lookup ignored image inheritance");
                UnityEngine.Object.DestroyImmediate(hold.gameObject);
            }
            finally
            {
                MajdataPlay.Alpha.NoteSkinLibrary.Clear();
                UnityEngine.Object.DestroyImmediate(model); UnityEngine.Object.DestroyImmediate(texture);
            }
        }
        static void CheckMegalovaniaImages(Clock clock)
        {
            var arguments = Environment.GetCommandLineArgs();
            var fixtureIndex = Array.IndexOf(arguments, "-alphaImageFixture");
            if (fixtureIndex < 0) return;
            var folder = arguments[fixtureIndex + 1];
            var referenceIndex = Array.IndexOf(arguments, "-alphaImageReference");
            var referenceFolder = arguments[referenceIndex + 1];
            var cameraObject = new GameObject("Inherited image GPU comparison");
            var camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true; camera.orthographicSize = 4;
            camera.transform.position = new Vector3(0, 0, -10);
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
            camera.cullingMask = 1 << 31;
            var output = new RenderTexture(256, 256, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            var pixels = new Texture2D(256, 256, TextureFormat.RGBA32, false, true);
            var reference = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            var previousTarget = RenderTexture.active;
            var texture = new Texture2D(2, 2);
            texture.LoadImage(System.IO.File.ReadAllBytes(System.IO.Path.Combine(Application.streamingAssetsPath, "Skins/Deluxe/TapSkins/tap.png")));
            var model = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), Vector2.one * .5f, 100);
            MajdataPlay.Alpha.NoteSkinLibrary.SetChartFolder(folder);
            try
            {
                foreach (var source in new[] { "8~[2.png]", "4~[4.png]", "7~[1.png]", "3~[3.png]", "2d~[6.png]", "6d~[8.png]" })
                {
                    var tap = MakeNote<TapDrop>(clock);
                    var head = (SpriteRenderer)Get(tap, "_thisRenderer");
                    head.transform.SetParent(tap.transform, false); head.sprite = model;
                    tap.transform.localRotation = Quaternion.Euler(0, 0, -22.5f - 45 * (source[0] - '1'));
                    tap.transform.localScale = Vector3.one;
                    tap.AlphaTimeline = MajdataPlay.Alpha.AlphaVisualTimeline.Empty;
                    tap.AlphaExpression = source;
                    tap.ApplyAlphaVisuals();
                    var width = head.sprite.bounds.size.x;
                    var rotation = Quaternion.Angle(tap.transform.localRotation, Quaternion.identity);
                    Debug.Log($"MEGALOVANIA IMAGE: {source}, width={width}, rotation={rotation}");
                    Require(Math.Abs(width - 1.22f) < .0001f && rotation < .0001f,
                        "Custom Tap must use ViewAlpha's unpadded 122px reference and upright image orientation: " + source);
                    tap.transform.localScale = Vector3.one * 3; head.gameObject.layer = 31;
                    UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(camera,
                        new UniversalRenderPipeline.SingleCameraRequest { destination = output });
                    RenderTexture.active = output;
                    pixels.ReadPixels(new Rect(0, 0, 256, 256), 0, 0); pixels.Apply();
                    var image = MajdataPlay.Alpha.AlphaNoteDecoration.Read(source).position.skin;
                    reference.LoadImage(System.IO.File.ReadAllBytes(System.IO.Path.Combine(referenceFolder, image)));
                    var actualPixels = pixels.GetPixels32(); var expectedPixels = reference.GetPixels32();
                    double error = 0;
                    for (var i = 0; i < actualPixels.Length; i++)
                        error += Math.Abs(actualPixels[i].r - expectedPixels[i].r) + Math.Abs(actualPixels[i].g - expectedPixels[i].g) + Math.Abs(actualPixels[i].b - expectedPixels[i].b);
                    error /= actualPixels.Length * 3d * 255;
                    Debug.Log($"MEGALOVANIA IMAGE GPU: {source}, mean pixel difference={error:F6}");
                    Require(error < .015, "Custom image GPU pixels differ from View: " + source);
                    tap.AlphaTimeline = MajdataPlay.Alpha.AlphaSimaiParser.GetTimeline(MajdataPlay.Alpha.AlphaSimaiParser.ParseChart("", "", "(120){4}<SIZE*(3,4)>1,"));
                    tap.transform.localScale = Vector3.one;
                    tap.ApplyAlphaVisuals();
                    Require(Vector3.Distance(tap.transform.localScale, new Vector3(3, 4, 1)) < .0001f && Math.Abs(head.bounds.size.x - 3.66f) < .0001f,
                        "SIZE did not match View's custom-image width");
                    UnityEngine.Object.DestroyImmediate(tap.gameObject);
                }
                Debug.Log("MEGALOVANIA IMAGE PASS: six real PNG/JPG Tap and D-head images, upright orientation, View reference dimensions and nonuniform SIZE.");
            }
            finally
            {
                MajdataPlay.Alpha.NoteSkinLibrary.Clear();
                RenderTexture.active = previousTarget;
                UnityEngine.Object.DestroyImmediate(cameraObject); UnityEngine.Object.DestroyImmediate(pixels);
                UnityEngine.Object.DestroyImmediate(reference); output.Release(); UnityEngine.Object.DestroyImmediate(output);
                UnityEngine.Object.DestroyImmediate(model); UnityEngine.Object.DestroyImmediate(texture);
            }
        }
        static void CheckDensity()
        {
            var allFake = MajdataPlay.Alpha.AlphaSimaiParser.ParseChart("14", "test", "(120){4}<FAKE*true>1,2,");
            var empty = MajdataPlay.Utils.ChartAnalyzer.AnalyzeMaidata(allFake, 2);
            Require(empty.PeakDensity == 0 && empty.Esti == 0, "All-fake density must be zero and finite");
            var fakeHead = MajdataPlay.Alpha.AlphaSimaiParser.ParseChart("14", "test", "(120){4}<FAKE*star=true>1-5[4:1],");
            Require(MajdataPlay.Utils.ChartAnalyzer.AnalyzeMaidata(fakeHead, 2).PeakDensity == 1,
                "A fake head was included in slide density");
            var fakeSlide = MajdataPlay.Alpha.AlphaSimaiParser.ParseChart("14", "test", "(120){4}<FAKE*slide=true>1-5[4:1],");
            Require(MajdataPlay.Utils.ChartAnalyzer.AnalyzeMaidata(fakeSlide, 2).PeakDensity == 1,
                "A fake slide was included in star-head density");
        }
        static void CheckFriendsFakePerformance(Clock clock)
        {
            var args = Environment.GetCommandLineArgs();
            var at = Array.IndexOf(args, "-alphaPerformanceChart");
            if (at < 0) return;
            using var input = System.IO.File.OpenRead(args[at + 1]);
            var metadata = SimaiParser.ParseMetadata(input);
            var chart = MajdataPlay.Alpha.AlphaSimaiParser.ParseChart("", "", metadata.Fumens[4]);
            var timeline = MajdataPlay.Alpha.AlphaSimaiParser.GetTimeline(chart);
            var candidates = chart.NoteTimings.ToArray().SelectMany(t => t.Notes
                .Where(n => n.Type == SimaiNoteType.Tap && MajdataPlay.Alpha.AlphaFakeNotes.IsFake(n, t, timeline: timeline))
                .Select(n => (Timing: t, Note: n))).ToArray();
            Require(candidates.Length > 0, "Performance fixture has no fake Taps");
            int start = 0, best = 0, count = 0;
            for (var end = 0; end < candidates.Length; end++)
            {
                while (candidates[end].Timing.Timing - candidates[start].Timing.Timing > .5) start++;
                if (end - start + 1 > count) { best = start; count = end - start + 1; }
            }
            var notes = new TapDrop[count];
            var samples = new double[120];
            var watch = new System.Diagnostics.Stopwatch();
            try
            {
                for (var i = 0; i < count; i++)
                {
                    var item = candidates[best + i];
                    var tap = MakeNote<TapDrop>(clock);
                    tap.Timing = (float)item.Timing.Timing;
                    tap.StartPos = item.Note.StartPosition;
                    tap.Speed = 10 * timeline.ForNote(item.Note).HSpeed("tap", tap.Timing, item.Timing.HSpeed, false, item.Note.IsBreak, item.Note.IsMine);
                    tap.AlphaTimeline = timeline.ForNote(item.Note);
                    tap.AlphaExpression = item.Note.RawContent;
                    tap.IsBreak = item.Note.IsBreak; tap.IsEX = item.Note.IsEx;
                    var guide = (SpriteRenderer)Get(tap, "_tapLineRenderer");
                    Set(tap, "_tapLineTransform", guide.transform); Set(tap, "_tapLineObject", guide.gameObject);
                    notes[i] = tap;
                }
                void Frame(int frame)
                {
                    clock.Time = (float)candidates[best].Timing.Timing - .15f + frame % 60 / 120f;
                    foreach (var tap in notes)
                    {
                        tap.RestoreAlphaVisualScales(); tap.OnUpdate(); tap.ApplyAlphaVisuals();
                    }
                }
                for (var i = 0; i < 30; i++) Frame(i);
                var allocated = GC.GetAllocatedBytesForCurrentThread();
                for (var frame = 0; frame < samples.Length; frame++)
                {
                    watch.Restart(); Frame(frame); watch.Stop(); samples[frame] = watch.Elapsed.TotalMilliseconds;
                }
                allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
                Array.Sort(samples);
                Require(allocated == 0, "Dense fake playback allocated per frame: " + allocated);
                Debug.Log($"FRIENDS FAKE PERFORMANCE: {candidates.Length} fake Taps; peak {count} in 0.5s at {candidates[best].Timing.Timing:F3}s; " +
                    $"120 Unity Mono frames, median {samples[60]:F3} ms, p95 {samples[114]:F3} ms, {allocated} B; CPU note updates only, not device FPS.");
            }
            finally
            {
                foreach (var note in notes) if (note) UnityEngine.Object.DestroyImmediate(note.gameObject);
            }
        }
        static void CheckInheritedHoldDirection(Clock clock)
        {
            foreach(var command in new[]{"<SPAWN*5.2>","<SV*-1>","<SPAWN*5.2><SV*-1>","<SV*-1><SPAWN*-3><DESTROY*-5>"})
            {
                var chart=MajdataPlay.Alpha.AlphaSimaiParser.ParseChart("","","(120){4}"+command+"1h[1:1]~[bones.png],");
                var timeline=MajdataPlay.Alpha.AlphaSimaiParser.GetTimeline(chart);
                var hold=MakeNote<HoldDrop>(clock);hold.Timing=2;hold.Length=2;hold.Speed=10;hold.StartPos=1;
                hold.AlphaTimeline=timeline;hold.AlphaExpression="1h[1:1]~[bones.png]";
                Set(hold,"_alphaInheritedImage",true);
                Set(hold,"_outerPos",new Vector3(0,4.8f,0));
                Set(hold,"_tapLineTransform",Child("image hold guide").transform);
                Set(hold,"_endTransform",Child("image hold tail").transform);
                Set(hold,"_tapLineObject",Child("image hold guide object"));
                Set(hold,"_endObject",Child("image hold tail object"));
                var spawn=timeline.Radius(true,2,"hold",false,false,false);
                var destroy=timeline.Radius(false,2,"hold",false,false,false);
                foreach(var now in new[]{.5f,1.5f,1.99f,2.1f})
                {
                    clock.Time=now;Invoke(hold,"OnUpdate");
                    var radius=hold.VisualPresentationRadius;
                    if(now>=2) radius=destroy;
                    var expected=-22.5f+((radius<0)!=(destroy<spawn)?180f:0f);
                    Require(Quaternion.Angle(hold.transform.rotation,Quaternion.Euler(0,0,expected))<.001f,
                        "Image Hold rotation differs from View for "+command+" at "+now);
                    Require(!((SpriteRenderer)Get(hold,"_thisRenderer")).flipY && !((SpriteRenderer)Get(hold,"_exRenderer")).flipY,
                        "Image Hold mirrors its texture instead of rotating the complete image");
                    Require(((Transform)Get(hold,"_tapLineTransform")).localScale.x>=0,"Image Hold guide rotated twice at negative radius");
                }
                UnityEngine.Object.DestroyImmediate(hold.gameObject);
            }
            Debug.Log("INHERITED HOLD DIRECTION PASS: inward SPAWN, negative SV, negative radius and combined cases match View's full-image rotation without texture mirroring.");
        }
        static void CheckStreamEachLine(Clock clock)
        {
            var chart=MajdataPlay.Alpha.AlphaSimaiParser.ParseChart("","",
                "(120){4},\n@{4}<SV*-1>2/3/4/5/6/7,<SV*1><ALPHAV*0>,\n,,1,");
            MajdataPlay.Alpha.AlphaSimaiParser.Activate(chart);
            var timing=chart.NoteTimings.ToArray().First(t=>t.Notes.Length==6);
            var loader=root.GetComponent<NoteLoader>();
            var createTap=typeof(NoteLoader).GetMethod("CreateTap",BindingFlags.Instance|BindingFlags.NonPublic);
            var first=(TapPoolingInfo)createTap.Invoke(loader,new object[]{timing.Notes[0],timing});
            var second=(TapPoolingInfo)createTap.Invoke(loader,new object[]{timing.Notes[1],timing});
            var info=(EachLinePoolingInfo)typeof(NoteLoader).GetMethod("CreateEachLine",BindingFlags.Instance|BindingFlags.NonPublic)
                .Invoke(loader,new object[]{timing,first,second});
            Require(info.AlphaTimeline==first.AlphaTimeline && info.AlphaTimeline!=MajdataPlay.Alpha.AlphaSimaiParser.Current,
                "Each guide lost independent stream settings");
            var tap=MakeNote<TapDrop>(clock);tap.AlphaTimeline=first.AlphaTimeline;tap.Timing=first.Timing;tap.Speed=first.Speed;
            first.EachLineBinding.Bind(tap);
            var obj=Child("independent each guide");var renderer=obj.AddComponent<SpriteRenderer>();
            var line=obj.AddComponent<EachLineDrop>();
            Set(line,"_sr",renderer);Set(line,"_noteController",clock);Set(line,"_gameObject",obj);Set(line,"_transform",obj.transform);
            line.State=NoteStatus.Inited;line.timing=first.Timing;
            Set(line,"_distanceProvider",info.DistanceProvider);Set(line,"_alphaTimeline",info.AlphaTimeline);
            clock.Time=first.Timing+.6f;line.ApplyAlphaVisuals();
            var block=new MaterialPropertyBlock();renderer.GetPropertyBlock(block);
            Require(block.GetFloat("_NoteAlpha")==0,"Independent ALPHAV=0 leaves visible each guides");
            clock.Time=first.Timing-1;Invoke(line,"OnLateUpdate");
            Require(!renderer.enabled && obj.layer==MajEnv.HIDDEN_LAYER,"SV-rewound each guide remains visible below the spawn fade threshold");
            UnityEngine.Object.DestroyImmediate(obj);UnityEngine.Object.DestroyImmediate(tap.gameObject);
            Debug.Log("STREAM EACH GUIDE PASS: independent ALPHAV hides the guide, and reverse SV hides it below the spawn fade threshold.");
        }
        static void CheckGuideStarOrder(Clock clock)
        {
            var cameraObject=Child("guide order GPU camera");var camera=cameraObject.AddComponent<Camera>();
            camera.orthographic=true;camera.orthographicSize=1;camera.transform.position=new Vector3(0,0,-10);
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;camera.cullingMask=1<<31;camera.enabled=false;
            cameraObject.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing=false;
            var output=new RenderTexture(64,64,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear);
            var pixels=new Texture2D(64,64,TextureFormat.RGBA32,false,true);
            var sprite=Sprite.Create(Texture2D.whiteTexture,new Rect(0,0,1,1),Vector2.one*.5f,1);
            var previous=RenderTexture.active;
            try
            {
                foreach(var kind in new[]{"slide","touchslide","wifi"})
                foreach(var earlierOnTop in new[]{true,false})
                {
                    SlideBase first=kind=="slide"?MakeNote<SlideDrop>(clock):kind=="wifi"?MakeNote<WifiDrop>(clock):MakeNote<ExtendSlideDrop>(clock);
                    SlideBase second=kind=="slide"?MakeNote<SlideDrop>(clock):kind=="wifi"?MakeNote<WifiDrop>(clock):MakeNote<ExtendSlideDrop>(clock);
                    foreach(var slide in new[]{first,second})
                    {
                        slide.SortOrder=earlierOnTop?(slide==first?-1:-200):(slide==first?200:400);
                        var stars=(Memory<GameObject>)Get(slide,"Stars");
                        for(var i=0;i<(kind=="wifi"?3:1);i++)
                        {
                            var star=Renderer();star.transform.SetParent(slide.transform,false);star.gameObject.layer=31;
                            star.sprite=sprite;star.color=slide==first?Color.blue:Color.red;star.sortingLayerName="SlideStars";
                            stars.Span[i]=star.gameObject;
                        }
                        typeof(SlideBase).GetMethod("SetStarSortOrder",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(slide,null);
                    }
                    UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=output});
                    RenderTexture.active=output;pixels.ReadPixels(new Rect(0,0,64,64),0,0);pixels.Apply();var centre=pixels.GetPixel(32,32);
                    Require(earlierOnTop?centre.b>.9f&&centre.r<.05f:centre.r>.9f&&centre.b<.05f,
                        "Overlapping moving stars ignore slide order: "+kind+", earlierOnTop="+earlierOnTop+", pixel="+centre);
                    UnityEngine.Object.DestroyImmediate(first.gameObject);UnityEngine.Object.DestroyImmediate(second.gameObject);
                }
                Debug.Log("GUIDE STAR ORDER GPU PASS: blue earlier / red later overlap in normal Slide, TouchSlide and WiFi; both sorting modes verified from rendered pixels.");
            }
            finally
            {
                RenderTexture.active=previous;UnityEngine.Object.DestroyImmediate(cameraObject);UnityEngine.Object.DestroyImmediate(pixels);
                output.Release();UnityEngine.Object.DestroyImmediate(output);UnityEngine.Object.DestroyImmediate(sprite);
            }
        }
        static SpriteRenderer Renderer() => Child("renderer").AddComponent<SpriteRenderer>();
        static FieldInfo Field(object obj, string name)
        {
            for (var type = obj.GetType(); type != null; type = type.BaseType)
            {
                var field = type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly);
                if (field != null) return field;
            }
            throw new MissingFieldException(obj.GetType().Name, name);
        }
        static void Set(object obj, string name, object value) => Field(obj, name).SetValue(obj, value);
        static object Get(object obj, string name) => Field(obj, name).GetValue(obj);
        static void Invoke(object obj, string name) => obj.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(obj, null);
        interface IReturnCount { int Returns { get; } }
        sealed class ReturningPool<TInfo, TQueue> : NotePool<TInfo, TQueue>, IReturnCount where TInfo : NotePoolingInfo where TQueue : NoteQueueInfo
        {
            public int Returns { get; private set; }
            public ReturningPool(GameObject parent) : base(parent, Child("pool").transform, Array.Empty<TInfo>(), 0) { }
            public override void Collect(in IPoolableNote<TInfo, TQueue> note) => Returns++;
        }
        sealed class Clock : INoteController
        {
            public float Time;
            public float ThisFrameSec => Time;
            public float AudioLength => 10;
            public bool IsStart => true;
            public bool IsAutoplay => true;
            public GameModInfo ModInfo => default;
            public JudgeGrade AutoplayGrade => JudgeGrade.Perfect;
            public Material BreakMaterial => null;
            public Material DefaultMaterial => null;
            public Material HoldShineMaterial => null;
        }
    }
}
#endif
