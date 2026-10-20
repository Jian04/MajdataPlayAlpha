using System;
using System.Linq;
using System.Text.Json;
using MajSimai;
using MajdataPlay.Alpha;

static class Program
{
    static int checks;
    static void Assert(bool ok, string name) { if (!ok) throw new Exception(name); checks++; }
    static void Near(double a, double b, string name) => Assert(Math.Abs(a-b) < 1e-5, $"{name}: {a} != {b}");
    static AlphaVisualTimeline Timeline(string text) => AlphaSimaiParser.GetTimeline(AlphaSimaiParser.ParseChart("14+", "tester", text));
    static void CheckDisplayPerformance(string path)
    {
        using var input = System.IO.File.OpenRead(path);
        var metadata = SimaiParser.ParseMetadata(input);
        var timeline = Timeline(metadata.Fumens[4]);
        var changes = timeline.Changes.ToArray();
        float? Reference(string name, double time)
        {
            AlphaDisplayEvent previous = null;
            float from = 1, target = 1;
            foreach (var change in changes)
            {
                if (change.Command != AlphaVisualTimeline.Kind.Display || change.Display.Name != name || (float)change.Time > (float)time) continue;
                var ev = change.Display;
                from = previous == null ? 1 : previous.Duration <= 0 ? target : from + (target - from) * Math.Clamp((float)(ev.Time - previous.Time) / previous.Duration, 0, 1);
                target = ev.ResetToDefault ? 1 : ev.Value;
                previous = ev;
            }
            return previous == null ? null : previous.Duration <= 0 ? target : from + (target - from) * Math.Clamp((float)(time - previous.Time) / previous.Duration, 0, 1);
        }
        var names = new[] { "JUDGELINE", "SHOWCOMBOINFO", "SHOWJUDGEINFO", "SHOWJUDGETEXT", "INNERBRIGHTNESS" };
        for (var frame = 0; frame < 1200; frame++) foreach (var name in names)
        {
            var time = (frame % 300) * .5;
            var expected = Reference(name, time);
            var actual = timeline.DisplayValue(name, time);
            Assert(expected.HasValue == actual.HasValue && (!expected.HasValue || Math.Abs(expected.Value - actual.Value) < 1e-5), "Display state changed at " + time);
        }
        var watch = new System.Diagnostics.Stopwatch();
        double Measure(bool reference)
        {
            double sum = 0;
            watch.Restart();
            for (var frame = 0; frame < 1200; frame++) foreach (var name in names)
                sum += (reference ? Reference(name, frame * .125) : timeline.DisplayValue(name, frame * .125)) ?? 0;
            watch.Stop();
            return watch.Elapsed.TotalMilliseconds + sum * 0;
        }
        var before = Measure(true); var after = Measure(false);
        Console.WriteLine($"DISPLAY PERFORMANCE PASS: {changes.Length} total / {timeline.DisplayEvents.Length} display commands; 6000 queries {before:F2} -> {after:F2} ms; {checks} forward/rewind checks; .NET CPU only.");
    }
    static void CheckSpinStream()
    {
        const double bpm=222, slot=240d/222/32;
        const string carrier="1~[note.png]~[B1-B4[8:3]]";
        var source="(222){8}<ZOOM*(True,2.2,8:1)><JUDGELINE*(0)>,\n@{32}<SIZE*tap=(0.8,0.6)>"+carrier+"/1~[note.png]~[E8-E6[8:3]],,,,,,"+carrier+"{64},,,,"+carrier+",\n\n,,,{32}<ALPHA*0><SHOWJUDGETEXT*(False)><DESTROY*10>4/5/6,,,,,,4,";
        var chart=AlphaSimaiParser.ParseChart("10","tester",source);
        foreach(var warning in AlphaSimaiParser.GetWarnings(chart)) Console.WriteLine("SPIN WARNING "+warning);
        Assert(AlphaSimaiParser.GetWarnings(chart).Count==0,"Spin valid inline timing warned/skipped");
        var timeline=AlphaSimaiParser.GetTimeline(chart);
        var inherited=chart.NoteTimings.ToArray().Where(t=>t.Notes.Any(AlphaNoteDecoration.IsCarrier)).ToArray();
        var expected=new[]{240d/bpm/8,240d/bpm/8+6*slot,240d/bpm/8+8*slot};
        Assert(inherited.Length==3 && inherited[0].Notes.Length==2,"Spin carriers lost at inline division changes");
        for(var i=0;i<inherited.Length;i++) {
            Console.WriteLine($"SPIN {i}: {inherited[i].Timing:R}, expected {expected[i]:R}");
            Near(inherited[i].Timing,expected[i],"Spin overlay division clock");
            foreach(var note in inherited[i].Notes.Where(AlphaNoteDecoration.IsCarrier)) {
                var local=timeline.ForNote(note);
                Near(local.Alpha("tap",inherited[i].Timing,inherited[i].Timing,false,false,false),1,"Spin visible carrier alpha leaked from hidden main stream");
                Near(note.SlideTime,240d/bpm*3/8,"Spin inherited duration stored on its own clock");
            }
        }
        var main=chart.NoteTimings.ToArray().Where(t=>t.Notes.Any(n=>!AlphaNoteDecoration.IsCarrier(n))).ToArray();
        Near(main[0].Timing,240d/bpm/8*4,"Spin hidden main first beat");
        Near(timeline.ForNote(main[0].Notes[0]).Alpha("tap",main[0].Timing,main[0].Timing,false,false,false),0,"Spin hidden main alpha");
        var inline=AlphaSimaiParser.ParseChart("","","(120){4}\n@{4}1h[8:1]{8},2(240),unknown{16},3,\n");
        Assert(AlphaSimaiParser.GetWarnings(inline).Count==1,"Unknown notes must not swallow inline timing");
        Assert(inline.NoteTimings.Length==3,"Inline BPM/division dropped preceding note");
        Near(inline.NoteTimings[0].Notes[0].HoldTime,.25,"Inline division must preserve Hold duration");
        Near(inline.NoteTimings[1].Timing,.25,"Inline eighth gap");
        Near(inline.NoteTimings[2].Timing,.4375,"Unknown sibling preserves BPM and sixteenth gap");
        var fast=AlphaSimaiParser.ParseChart("","","(120){4}1,\n@{4}(240)1~[note.png]~[B1-B4[8:3]],\n2,");
        var merged=fast.NoteTimings.ToArray().Single(t=>t.Notes.Any(AlphaNoteDecoration.IsCarrier));
        Near(merged.Notes.Single(AlphaNoteDecoration.IsCarrier).SlideTime,.375,"Merged overlay keeps its own BPM duration");
    }
    static void CheckAlpha6()
    {
        var pseudoChart = AlphaSimaiParser.ParseChart("", "", "(205){8},,<ALPHA*0>" +
            string.Concat(Enumerable.Repeat("B1\u0060D2,", 32)));
        var pseudoTimeline = AlphaSimaiParser.GetTimeline(pseudoChart);
        Assert(AlphaSimaiParser.GetWarnings(pseudoChart).Count == 0 && pseudoChart.NoteTimings.Length == 64,
            "Pseudo-each Touch must retain both notes in every repeated slot");
        foreach (var point in pseudoChart.NoteTimings)
            Near(pseudoTimeline.Alpha("touch", point.Timing, point.Timing - .1, false, false, false), 0,
                "Pseudo-each Touch must retain ALPHA at authored time " + point.Timing);
        var shakeOnly = Timeline("(120){4}<SHAKE*(Instant,1,10,90,0.2)>1,");
        Assert(!shakeOnly.NeedsScreenRender(-.1f) && shakeOnly.NeedsScreenRender(.1f) && !shakeOnly.NeedsScreenRender(.3f),
            "Screen filter pass must only run while the effect is visible");
        Assert(shakeOnly.NeedsScreenRender(.1f), "Screen filter culling broke rewind");
        Assert(Timeline("(120){4}<KALEIDOSCOPE*(True,0)>1,").NeedsScreenRender(.1f), "Zero-degree kaleidoscope still needs rendering");
        Assert(Timeline("(120){4}<TRAIL*(True,0)>1,").NeedsScreenRender(-1), "Trail must preserve history seed before activation");
        var fadingTrail = Timeline("(120){4}<TRAIL*(True,1)>1,<TRAIL*(False,0.2)>2,");
        Assert(fadingTrail.NeedsTrailHistory(.1f) && fadingTrail.NeedsTrailHistory(.6f), "Trail active/fading history must remain");
        Assert(!fadingTrail.NeedsTrailHistory(.71f) && !fadingTrail.NeedsScreenRender(.71f), "Finished Trail must release its pass and history");
        Assert(fadingTrail.NeedsTrailHistory(.1f), "Trail history must reactivate after rewind");
        var laterTrail = Timeline("(120){4}<TRAIL*(True,1)>1,<TRAIL*(False,0)>2,,<TRAIL*(True,1)>3,");
        Assert(laterTrail.NeedsTrailHistory(.8f), "Trail must retain warm history for a later activation");
        foreach (var text in new[] { "(120){16}1", "(120){16}1\n", "(120){16}1\nE", "(120){4}\n@{16}1", "(120){4}\n@*{16}1*@" })
            Assert(AlphaSimaiParser.ParseChart("", "", text).NoteTimings.Length == 0, "Unterminated note must not play: " + text);
        foreach (var text in new[] { "(120){16}1,", "(120){16}1\n,", "(120){4}\n@{16}1,", "(120){4}\n@*{16}1,*@" })
            Assert(AlphaSimaiParser.ParseChart("", "", text).NoteTimings.Length == 1, "Authored comma must keep note: " + text);
        var incompleteTail = AlphaSimaiParser.ParseChart("", "", "(120){4}1,2,3");
        Assert(incompleteTail.NoteTimings.Length == 2 && incompleteTail.NoteTimings[1].Notes[0].StartPosition == 2,
            "Unterminated final slot must not affect preceding complete slots");
        foreach (var text in new[] { "(120){4}XD1[8:1]", "(120){4}\n@{16}XD1[8:1]" })
            Assert(!Timeline(text).HasNoise, "Unterminated noise slot must not play");
        foreach (var angle in new[] { 0, 90 })
        {
            var effect = AlphaScreenEffectParser.Parse($"SHAKE*(Instant,1,10,{angle},0.2)", 0, 120);
            Assert(effect.hasDirection, "Instant SHAKE direction missing");
            Near(effect.paramB, angle * Math.PI / 180, "Instant SHAKE direction");
            Near(effect.duration, .2, "Instant SHAKE duration");
            Near(effect.paramA, 10, "Instant SHAKE frequency");
        }
        CheckSpinStream();
        // The actual Overdead part5 paths used to be stripped as <B5>/<D5> commands.
        var overdeadPaths = new[] {
            "B1?<B5>B8m[1.02##18.36]",
            "D1?<D5>D1<D5>D1<D5>D1<D5>D1<D5>D1<D5>D1<D5>D1<D5>D1<D5>D1m[2.04##16.32]" };
        var overdead = AlphaSimaiParser.ParseChart("", "", "(250){4}<SV*touch=0,tap=NULL,hold=NULL>Cb/" + string.Join("/", overdeadPaths) + ",");
        Assert(AlphaSimaiParser.GetWarnings(overdead).Count == 0, "Overdead sensor turns mistaken for commands");
        Assert(overdead.NoteTimings.ToArray().Single().Notes.Length == 3, "Overdead Mine paths dropped");
        for (var i = 0; i < overdeadPaths.Length; i++)
        {
            var note = overdead.NoteTimings.ToArray().Single().Notes.Single(n => n.RawContent == overdeadPaths[i]);
            Assert(note.IsMineSlide && note.IsSlideNoHead && !note.IsMine, "Overdead Mine/head flags changed");
            Near(note.SlideStartTime, i == 0 ? 1.02 : 2.04, "Overdead explicit delay");
            Near(note.SlideTime, i == 0 ? 18.36 : 16.32, "Overdead total duration");
            Near(AlphaSimaiParser.GetTimeline(overdead).SlideProgress(1.02 + note.SlideTime / 2, 1.02, note.SlideTime), .5, "Touch SV must not freeze Slide");
        }
        var joinedArcs = AlphaSimaiParser.ParseChart("", "", "(120){4}A1<A5>A1m[4:1]/E1<E5>E1m[4:1]/B1<B5>B1m[4:1]/D1<D5>D1m[4:1]/A1<C>A5[4:1],");
        Assert(AlphaSimaiParser.GetWarnings(joinedArcs).Count == 0 && joinedArcs.NoteTimings.ToArray().Single().Notes.Length == 5, "TouchSlide turn combinations stripped");
        var unknownBesideArc = AlphaSimaiParser.ParseChart("", "", "(120){4}<A1*unknown><SHOW_UNKNOWN*true>A1<A5>A1m[4:1],");
        Assert(AlphaSimaiParser.GetWarnings(unknownBesideArc).Count == 2 && unknownBesideArc.NoteTimings.ToArray().Single().Notes.Length == 1, "Unknown directives must still skip without removing TouchSlide");
        foreach (var args in new[] { "True,0.5,True", "True,0.5,0.3,True", "Instant,0.5,8:1,True", "2,0.5,True", "0.2,1,0.2,0.5,True" })
        {
            var token = "VIGNETTE*(" + args + ")";
            var ev = AlphaScreenEffectParser.Parse(token, 0, 120);
            Assert(ev.gradient && ev.effect == "Vignette", "Vignette gradient lost: " + token);
            Near(ev.intensity, .5, "Vignette intensity changed");
        }
        foreach (var args in new[] { "True,0.5", "True,0.5,0.3", "2,0.5", "0.2,1,0.2,0.5" })
            Assert(AlphaScreenEffectParser.Parse("VIGNETTE*(" + args + ")", 0, 120).gradient, "Default soft vignette missing");
        Assert(!AlphaScreenEffectParser.Parse("VIGNETTE*(True,0.5,0.3,False)",0,120).gradient,"Explicit hard vignette lost");
        Assert(!MajdataCore.AlphaCommandGrammar.TryValidate("VIGNETTE*(True,0.5,0.3,garbage)", 120, out _), "Invalid gradient flag accepted");
        var softFade = new AlphaEffectTrack(new System.Collections.Generic.List<ScreenEffectEvent> {
            AlphaScreenEffectParser.Parse("VIGNETTE*(True,0.5,0,True)", 0, 120),
            AlphaScreenEffectParser.Parse("VIGNETTE*(False,1)", 1, 120) }, true);
        Near(softFade.Evaluate(1.5f, out var fading), .25, "Soft vignette fade-out changed");
        Assert(fading.gradient, "Fade-out reverted soft vignette to a hard cut");
        var replaySoft = softFade.Evaluate(.5f, out var replay);
        Assert(replay.gradient && replaySoft == .5f, "Vignette gradient lost after seeking");
        foreach (var raw in new[] { "1`2`3", "E1h[8:1]", "B1hfx[4:1]", "1?-5[4:1]", "1!-5[4:1]", "1?w5[4:1]", "1!w5[4:1]" })
        {
            var source = "(120){4}" + raw + ",";
            var native = SimaiParser.ParseChart(source);
            var alpha = AlphaSimaiParser.ParseChart("", "", source);
            Assert(AlphaSimaiParser.GetWarnings(alpha).Count == 0, "native syntax was filtered: " + raw);
            Assert(alpha.NoteTimings.Length == native.NoteTimings.Length, "native pseudo-each timings changed: " + raw);
            for (var i = 0; i < native.NoteTimings.Length; i++)
            {
                Near(alpha.NoteTimings[i].Timing, native.NoteTimings[i].Timing, "native timing changed: " + raw);
                Assert(alpha.NoteTimings[i].Notes.Length == native.NoteTimings[i].Notes.Length, "native slot changed: " + raw);
                for (var j = 0; j < native.NoteTimings[i].Notes.Length; j++)
                {
                    var a = alpha.NoteTimings[i].Notes[j]; var n = native.NoteTimings[i].Notes[j];
                    Assert(a.Type == n.Type && a.StartPosition == n.StartPosition && a.TouchArea == n.TouchArea &&
                        a.HoldTime == n.HoldTime && a.SlideTime == n.SlideTime && a.SlideStartTime == n.SlideStartTime &&
                        a.IsSlideNoHead == n.IsSlideNoHead && a.IsHanabi == n.IsHanabi && a.RawContent == n.RawContent,
                        "native note semantics changed: " + raw);
                }
            }
        }
        foreach (var noHead in new[] { '?', '!' })
        {
            var pair = AlphaSimaiParser.ParseChart("", "", $"(120){{4}}1{noHead}-5[8:1]/4,");
            var notes = pair.NoteTimings[0].Notes.ToArray();
            Assert(notes.Length == 2 && notes.Count(note => note.IsSlideNoHead) == 1 &&
                   notes.Count(note => !note.IsSlideNoHead && !note.IsMine) == 1,
                $"{noHead} no-head slide must not turn the adjacent tap into each");
        }
        Assert(AlphaNoteDecoration.Read("E1h[8:1]") == null, "native TouchHold intercepted by Alpha decoration");
        foreach (var group in new[] { 'A', 'B', 'C', 'D', 'E' })
        {
            Assert(MajdataCore.TouchSlideSensorRoute.AllowsKeyPathGroup(group, true, true) == (group is 'C' or 'D' or 'E'), "D-only group mismatch");
            Assert(MajdataCore.TouchSlideSensorRoute.AllowsKeyPathGroup(group, true, false), "D/A group missing");
            Assert(MajdataCore.TouchSlideSensorRoute.AllowsKeyPathGroup(group, false, true), "A/D group missing");
            Assert(MajdataCore.TouchSlideSensorRoute.AllowsKeyPathGroup(group, true, true, true), "mixed middle group missing");
        }
        foreach (var command in MajdataCore.AlphaCommandGrammar.Commands)
        {
            var args = command.scalar != null ? command.scalar.defaultText
                : string.Join(",", command.forms[0].slots.Where(s => !s.optional).Select(s => s.defaultText));
            var raw = "<" + command.name + "*" + (command.requiresParentheses ? "(" + args + ")" : args) + ">";
            var sample = AlphaSimaiParser.ParseChart("", "", "(120){4}" + raw + "1,");
            Assert(AlphaSimaiParser.GetWarnings(sample).Count == (command.name == "OUTERBRIGHTNESS" ? 1 : 0), "unhandled command catalog entry: " + raw);
        }
        foreach(var command in MajdataCore.AlphaCommandGrammar.Commands)
            foreach(var example in MajdataCore.AlphaCommandExamples.Examples(command.name).Split(" / "))
            {
                var sample=AlphaSimaiParser.ParseChart("","","(120){4}"+example+"1,");
                var expected=command.name=="OUTERBRIGHTNESS" || command.name=="JUDGELINE" && (example.Contains("*3") || example.Contains("*(3")) ? 1 : 0;
                Assert(AlphaSimaiParser.GetWarnings(sample).Count==expected,"ViewAlpha help example compatibility: "+example);
                Assert(sample.NoteTimings.Length==1 && sample.NoteTimings[0].Notes.Length==1,"Help example removed normal notes: "+example);
            }
        var finalSlot = AlphaSimaiParser.ParseChart("", "", "(120){4}1,2h[8:1]");
        Assert(finalSlot.NoteTimings.Length == 1 && finalSlot.NoteTimings[0].Notes[0].Type == SimaiNoteType.Tap, "Unterminated Hold must not play");
        var incomplete = AlphaSimaiParser.ParseChart("", "", "(120){4}1-5");
        Assert(incomplete.NoteTimings.IsEmpty && AlphaSimaiParser.GetWarnings(incomplete).Count > 0, "incomplete final slide not warned");
        Assert(AlphaSimaiParser.ParseChart("", "", "").NoteTimings.IsEmpty, "empty difficulty changed");
        foreach (var raw in new[] { "1d", "8dm", "2dh[8:1]", "1d$", "1d-5d[4:1]", "B1h[8:1]", "E3hbf[8:1]", "1hf[8:1]" })
        {
            var chart = AlphaSimaiParser.ParseChart("", "", "(120){4}" + raw + ",2,");
            Assert(AlphaSimaiParser.GetWarnings(chart).Count == 0, "extended head warning: " + raw);
            var expectedRaw = AlphaNoteDecoration.Read(raw) != null || AlphaPathParser.TryRead(raw, out _) ? raw
                : SimaiParser.ParseChart("(120){4}" + raw + ",").NoteTimings[0].Notes.Single().RawContent;
            Assert(chart.NoteTimings[0].Notes.Single().RawContent == expectedRaw, "extended/native head dropped: " + raw);
            Assert(!AlphaFakeNotes.IsFake(chart.NoteTimings[0].Notes[0], chart.NoteTimings[0]), "D/touchhold/firework must remain judgeable: " + raw);
        }
        var comments = AlphaSimaiParser.ParseChart("", "", "(120){4}1,|* \n garbage,<SV*NaN>2, *|3,\n@3/4\n4,\n@FF0000\n5,");
        Assert(AlphaSimaiParser.GetWarnings(comments).Count == 0, "comments/editor directives created bogus notes");
        Assert(comments.NoteTimings.ToArray().SelectMany(t=>t.Notes).Count() == 4, "comments changed actual notes");
        Near(comments.NoteTimings[3].Timing, 1.5, "meter must not change note/audio time");
        var meterSource="(120){4}1,\n@3/4\r\n2,\n@ 7 / 8\n3,\n@4/4\n4,E";
        var meterText=meterSource.ToCharArray();
        Assert(AlphaStreamSyntax.Prepare(meterText).Count==0,"meter markers must never create overlay streams");
        Assert(!new string(meterText).Contains('@'),"meter markers must be removed before MajSimai reads notes");
        var repeatedMeters=string.Concat(Enumerable.Repeat("@3/4\n",2000));
        var repeatedMeterText=repeatedMeters.ToCharArray();
        var allocatedBefore=GC.GetAllocatedBytesForCurrentThread();
        Assert(AlphaStreamSyntax.Prepare(repeatedMeterText).Count==0,"repeated meter markers created an overlay");
        Assert(GC.GetAllocatedBytesForCurrentThread()-allocatedBefore<repeatedMeters.Length*8+1024,"meter reading repeatedly copied the whole chart");
        var meterChart=AlphaSimaiParser.ParseChart("","",meterSource);
        Assert(AlphaSimaiParser.GetWarnings(meterChart).Count==0 && meterChart.NoteTimings.ToArray().SelectMany(p=>p.Notes).Count()==4,"meter markers created notes or warnings");
        for(var i=0;i<4;i++) Near(meterChart.NoteTimings[i].Timing,i*.5,"meter marker changed song position");
        foreach(var args in new[]{"True,45","True,45,.3","True,45,False","True,45,.3,False","True,45,True","True,45,.3,True","Instant,45,8:1,True","2,45,True",".2,1,.2,45,True","False,True","False,.2,False"})
        {
            var ev=AlphaScreenEffectParser.Parse("ROTATE*("+args+")",0,120);
            Assert(ev.rotatePV==args.EndsWith("True"),"ROTATE PV flag lost: "+args);
        }
        var rotation=Timeline("(120){4}<ROTATE*(True,30)>1,<ROTATE*(True,60,1,True)>2,,,<ROTATE*(True,90,1,False)>3,,,<ROTATE*(False,.5)>4,E");
        Near(rotation.ScreenEffect("Rotate",.25f,out _),30,"default ROTATE must rotate chart");
        Near(rotation.RotationForPV(.25f),0,"default ROTATE must exclude PV");
        Near(rotation.RotationForPV(1),30,"PV transition must have its own baseline");
        Near(rotation.RotationForPV(1.5f),60,"explicit PV rotation must follow authored angle");
        Near(rotation.RotationForPV(2.5f),30,"switching PV off must interpolate toward its base pose");
        Near(rotation.RotationForPV(3.1f),0,"PV must stop following later default commands");
        Near(rotation.RotationForPV(.25f),0,"rewinding must restore default PV exclusion");
        var overlay = AlphaSimaiParser.ParseChart("", "", "(120){8}<COLOR*FF0000>1,\n@{4}<COLOR*00FF00><FAKE*true>2,3,\n4,5,6,7,");
        Assert(AlphaSimaiParser.GetWarnings(overlay).Count == 0, "overlay warnings");
        Assert(overlay.NoteTimings.Length == 5, "overlay advanced main timeline");
        Near(overlay.NoteTimings[1].Timing, .25, "overlay starts at main cursor");
        Assert(overlay.NoteTimings[1].Notes.Length == 2 && overlay.NoteTimings[3].Notes.Length == 2, "overlay timing merge");
        var timeline = AlphaSimaiParser.GetTimeline(overlay);
        var green = overlay.NoteTimings[1].Notes.Single(n=>n.StartPosition==2);
        var red = overlay.NoteTimings[1].Notes.Single(n=>n.StartPosition==4);
        Assert(timeline.ForNote(green).Color("tap", .25, .25, true, false, false) == 0x00FF00, "overlay color lost");
        Assert(timeline.ForNote(red).Color("tap", .25, .25, true, false, false) == 0xFF0000, "overlay color leaked to main");
        Assert(AlphaFakeNotes.JudgableChart(overlay).NoteTimings.ToArray().Sum(t=>t.Notes.Length) == 5, "overlay fake counted for score");
        AlphaSimaiParser.TransformTimeline(overlay, .5, -1);
        Near(AlphaSimaiParser.GetTimeline(overlay).ForNote(green).Changes.First().Time, -.875, "practice must transform overlay state");
        var block = AlphaSimaiParser.ParseChart("", "", "(120){4}1,\n@* {8}2,\n(240)3,4, *@\n5,");
        Assert(AlphaSimaiParser.GetWarnings(block).Count==0, "multiline overlay warnings");
        Near(block.NoteTimings.ToArray().Single(t=>t.Notes.Any(n=>n.StartPosition==3)).Timing,.75,"overlay BPM/local division");
        Near(block.NoteTimings.ToArray().Single(t=>t.Notes.Any(n=>n.StartPosition==4)).Timing,.875,"overlay preserves local BPM across lines");
        AlphaSimaiParser.Activate(block);
        var blockSlot = block.NoteTimings.ToArray().Single(t=>t.Notes.Any(n=>n.StartPosition==3));
        Near(AlphaSimaiParser.ForNoteTiming(blockSlot.Notes.Single(), blockSlot).Bpm, 240, "overlay note BPM context lost");
        AlphaSimaiParser.Clear();
        var ownEach = AlphaSimaiParser.ParseChart("", "", "(120){4}1,\n@{4}2/3,\n4,");
        AlphaSimaiParser.Activate(ownEach);
        var mergedSlot = ownEach.NoteTimings[1];
        Assert(AlphaSimaiParser.StateEach(mergedSlot.Notes.Single(n=>n.StartPosition==4)) == false, "overlay changed main each state");
        Assert(AlphaSimaiParser.StateEach(mergedSlot.Notes.Single(n=>n.StartPosition==2)) == true, "overlay each context lost");
        AlphaSimaiParser.Clear();
        foreach (var token in new[] {"SHOWJUDGETEXT*(false)", "COMBODISPLAY*(Score)", "COMBODISPLAY*(DXACCDOWN,8:1)", "AUDIO*(True,media/test.wav)", "AUDIO*(False)", "PVOVERLAY*(True,media/bg.png,8:1)", "PVOVERLAY*(False,0.5)"})
        {
            var display=AlphaSimaiParser.ParseChart("", "", "(120){4}<"+token+">1,");
            Assert(AlphaSimaiParser.GetWarnings(display).Count==0 && AlphaSimaiParser.GetTimeline(display).HasDisplay,"display/media not supported: "+token);
        }
        var unsupported=AlphaSimaiParser.ParseChart("", "", "(120){4}<COMBODISPLAY*(CSCORE)>1,");
        Assert(AlphaSimaiParser.GetWarnings(unsupported).Count==1 && unsupported.NoteTimings[0].Notes.Length==1,"unmapped combo mode must warn without stopping");
        var dim=Timeline("(120){4}<INNERBRIGHTNESS*(0.75)>1,");
        Near(dim.DisplayValue("INNERBRIGHTNESS",0).Value,.75,"dim semantics");
        var pathHS=Timeline("(120){4}<HS*slide=2>1,<HS*slide=NULL>2,");
        Near(pathHS.HSpeed("slide",.5,1,false,false,false),1,"slide HS NULL restores 1");
    }
    static void Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--benchmark-visual")
        {
            using var input = System.IO.File.OpenRead(args[1]);
            var metadata = SimaiParser.ParseMetadata(input);
            var chart = AlphaSimaiParser.ParseChart("", "", metadata.Fumens[4]);
            var timeline = AlphaSimaiParser.GetTimeline(chart);
            var notes = chart.NoteTimings.ToArray().SelectMany(t => t.Notes.Select(n => (Time: t.Timing, Note: n)))
                .Take(64).ToArray();
            double Sample(int frames)
            {
                double checksum = 0;
                for (var frame = 0; frame < frames; frame++)
                    foreach (var item in notes)
                    {
                        var local = timeline.ForNote(item.Note);
                        var type = item.Note.Type == SimaiNoteType.Hold ? "hold" : "tap";
                        var now = item.Time + (frame % 60) / 60d;
                        checksum += local.Color(type, item.Time, now, true, false, false) ?? 0;
                        checksum += local.Alpha(type, item.Time, now, true, false, false);
                        checksum += local.Size(type, item.Time, now, true, false, false).X;
                        checksum += local.Radius(true, item.Time, type, true, false, false);
                        checksum += local.Radius(false, item.Time, type, true, false, false);
                    }
                return checksum;
            }
            Sample(10);
            var allocated = GC.GetAllocatedBytesForCurrentThread();
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var result = Sample(600);
            watch.Stop();
            allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
            Console.WriteLine($"VISUAL BENCHMARK: 600 frames x {notes.Length} notes, {watch.Elapsed.TotalMilliseconds:F2} ms, {allocated} allocated bytes, checksum={result:R}");
            return;
        }
        if (args.Length == 3 && args[0] == "--compare-view-json")
        {
            using var input=System.IO.File.OpenRead(args[1]);
            var metadata=SimaiParser.ParseMetadata(input);
            var chart=AlphaSimaiParser.ParseChart("","",metadata.Fumens[1]);
            foreach(var warning in AlphaSimaiParser.GetWarnings(chart)) Console.WriteLine(warning);
            Assert(AlphaSimaiParser.GetWarnings(chart).Count==0,"Original Spin chart has parser warnings");
            using var doc=JsonDocument.Parse(System.IO.File.ReadAllText(args[2]));
            var expected=doc.RootElement.GetProperty("timingList").EnumerateArray().SelectMany(t=>
                t.GetProperty("noteList").EnumerateArray().Select(n=>(Raw:n.GetProperty("isTrajectoryOnly").GetBoolean() ? n.GetProperty("pathExpression").GetString()! : n.GetProperty("noteContent").GetString()!+(n.GetProperty("isBreak").GetBoolean()?"b":""),
                    Time:t.GetProperty("time").GetDouble(),Duration:n.GetProperty("slideTime").GetDouble(),Carrier:n.GetProperty("isTrajectoryOnly").GetBoolean()))).ToArray();
            var actual=chart.NoteTimings.ToArray().SelectMany(t=>t.Notes.Select(n=>(Raw:AlphaNoteDecoration.IsCarrier(n)?n.RawContent:n.RawContent+(n.IsBreak?"b":""),Time:t.Timing+metadata.Offset,Duration:n.SlideTime,Carrier:AlphaNoteDecoration.IsCarrier(n)))).ToArray();
            System.IO.File.WriteAllText(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(args[1])!,"play-parsed.json"),JsonSerializer.Serialize(actual,new JsonSerializerOptions{IncludeFields=true}));
            Assert(expected.Length==actual.Length,$"Original Spin note count: Play {actual.Length}, View {expected.Length}");
            var timeline=AlphaSimaiParser.GetTimeline(chart);
            foreach(var group in expected.GroupBy(n=>n.Raw)) {
                var want=group.OrderBy(n=>n.Time).ToArray();
                var have=actual.Where(n=>n.Raw==group.Key).OrderBy(n=>n.Time).ToArray();
                Assert(want.Length==have.Length,$"Original Spin raw note lost: {group.Key}, Play={have.Length}, View={want.Length}");
                for(int i=0;i<want.Length;i++) {
                    Near(have[i].Time,want[i].Time,"Original Spin matches View beat: "+group.Key);
                    Near(have[i].Duration,want[i].Duration,"Original Spin matches View duration");
                }
            }
            foreach(var t in chart.NoteTimings) foreach(var n in t.Notes) {
                var carrier=AlphaNoteDecoration.IsCarrier(n);
                Near(timeline.ForNote(n).Alpha("tap",t.Timing,t.Timing,false,false,false),carrier?1:0,"Original Spin stream alpha isolation");
            }
            Console.WriteLine($"SPIN ORIGINAL VIEW MATCH PASS: {actual.Length} notes, all source expressions, beat times, durations and inherited/main opacity; first={metadata.Offset}, last={actual.Max(n=>n.Time):F6}s.");
            return;
        }
        if (args.Length == 3 && args[0] == "--inspect")
        {
            var metadata = SimaiParser.ParseMetadata(new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes(System.IO.File.ReadAllText(args[1]))));
            var chart = AlphaSimaiParser.ParseChart("", "", metadata.Fumens[int.Parse(args[2]) - 1]);
            foreach (var warning in AlphaSimaiParser.GetWarnings(chart)) Console.WriteLine(warning);
            foreach (var timing in chart.NoteTimings)
            foreach (var note in timing.Notes)
                if (AlphaPathParser.TryRead(note.RawContent, out _))
                    Console.WriteLine($"{timing.Timing:F3}s: {note.RawContent}, start={note.SlideStartTime:F3}, duration={note.SlideTime:F3}, mineHead={note.IsMine}, mineBody={note.IsMineSlide}, noHead={note.IsSlideNoHead}");
            return;
        }
        if(args.Length==2 && args[0]=="--catalog") {
            System.IO.File.WriteAllText(args[1],JsonSerializer.Serialize(MajdataCore.AlphaCommandGrammar.Commands.Select(c=>new { c.name, category=c.category.ToString(), canonical=c.Canonical, c.targets,
                example="<"+c.name+"*"+(c.requiresParentheses?"(":"")+(c.scalar!=null?c.scalar.defaultText:string.Join(",",c.forms[0].slots.Where(s=>!s.optional).Select(s=>s.defaultText)))+(c.requiresParentheses?")":"")+">" }),new JsonSerializerOptions{WriteIndented=true})); return;
        }
        if(args.Length==2 && args[0]=="--display-performance") { CheckDisplayPerformance(args[1]); return; }
        if(args.Length==2 && args[0]=="--chart") {
            var metadata=SimaiParser.ParseMetadata(new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes(System.IO.File.ReadAllText(args[1]))));
            var chart=AlphaSimaiParser.ParseChart("14","tester",metadata.Fumens[4]);
            var warnings=AlphaSimaiParser.GetWarnings(chart);
            foreach(var warning in warnings) Console.WriteLine(warning);
            Assert(warnings.All(w=>w.Content.Contains("OUTERBRIGHTNESS")),"Unexpected full showcase parse warning");
            Console.WriteLine($"ALL ALPHA CHART PASS: {chart.NoteTimings.Length} timing points, {AlphaSimaiParser.GetTimeline(chart).NoiseEvents.Length} noise events; end {chart.CommaTimings[^1].Timing:F2}s");return;
        }
        try { Run(); }
        catch (Exception error) { Console.Error.WriteLine(error); Environment.ExitCode = 1; }
    }
    static void Run()
    {
        NewSyntaxRegression.Run();
        CheckAlpha6();
        var noiseChart=AlphaSimaiParser.ParseChart("","","(120){4}1/XD1[8:1],2/XC[#0.12],XE1/3,E");
        var noiseTimeline=AlphaSimaiParser.GetTimeline(noiseChart);
        Assert(AlphaSimaiParser.GetWarnings(noiseChart).Count==0,"Noise/ordinary sibling or end marker was filtered");
        Assert(noiseTimeline.NoiseEvents.Length==3,"Noise event count changed");
        Assert(noiseChart.NoteTimings.ToArray().Sum(t=>t.Notes.Length)==3,"Noise enters score/note count");
        Near(noiseTimeline.NoiseEvents[0].End-noiseTimeline.NoiseEvents[0].Start,.25,"Noise beat length mismatch");
        Near(noiseTimeline.NoiseEvents[1].End-noiseTimeline.NoiseEvents[1].Start,.12,"Noise seconds length mismatch");
        Assert(noiseTimeline.Noise.IsBlocked(17,.1)&&!noiseTimeline.Noise.IsBlocked(17,.3),"Noise input window wrong");
        Assert(!noiseTimeline.Noise.IsBlocked(25,1.03),"Short visual noise persistence disables input past its duration");
        var exactNoise=Timeline("(120){4}XC[1:0],XD1[384:1],E");
        Near(exactNoise.NoiseEvents[0].End,exactNoise.NoiseEvents[0].Start,"Zero noise duration was extended");
        Near(exactNoise.NoiseEvents[1].End-exactNoise.NoiseEvents[1].Start,2d/384,"Short noise was extended to a frame");
        var warning=new float[33];var active=new bool[33];exactNoise.Noise.Evaluate(-.1,warning,active);
        Assert(warning[16]>0&&!active[16],"Zero noise must retain its warning");
        Assert(!exactNoise.Noise.IsBlocked(16,0)&&!exactNoise.Noise.TryGetVisualSpan(16,0,out _,out _),"Zero noise must never activate or render a field");
        Assert(exactNoise.Noise.TryGetVisualSpan(17,.502,out _,out _)&&!exactNoise.Noise.TryGetVisualSpan(17,.506,out _,out _),"Short noise visual span must match the actual duration");
        foreach (var zeroToken in new[] { "XC", "XC[1:0]" })
        {
            var zero = Timeline("(120){4},," + zeroToken + ",E");
            foreach (var zeroTimeline in new[] { zero, zero.ClampNoise(0, 3), zero.ClampNoise(1, 3) })
            {
                Assert(zeroTimeline.NoiseEvents.Length == 1, "Zero noise event lost: " + zeroToken + "; original=" + string.Join(",", zero.NoiseEvents.Select(z=>z.Start)));
                var zone = zeroTimeline.NoiseEvents.Single();
                Near(zone.End, zone.Start, "Zero noise must survive practice clipping: " + zeroToken);
                zeroTimeline.Noise.Evaluate(zone.Start-.01, warning, active);
                Assert(warning[16]>0 && !active[16], "Zero noise warning lost: " + zeroToken);
                foreach (var time in new[] { zone.Start, zone.Start+.001, zone.Start+.05, zone.Start+.1 })
                    Assert(!zeroTimeline.Noise.TryGetVisualSpan(16,time,out _,out _) && !zeroTimeline.Noise.IsBlocked(16,time),
                        "Zero noise creates an active field: " + zeroToken);
            }
        }
        Near(exactNoise.ClampNoise(0,3).NoiseEvents[1].End-.5, 2d/384, "Practice clipping extends 384th noise");
        var endingNoise = Timeline("(120){4}1,,,,XC[1:1],E");
        Near(endingNoise.NoiseEnd,4,"Last noise end must include its duration, not its prewarning lead");
        Assert(!endingNoise.EnvironmentFinished(0) && !endingNoise.EnvironmentFinished(3.99) && endingNoise.EnvironmentFinished(4),
            "Future noise or its active tail allows early song completion");
        Assert(AlphaVisualTimeline.Empty.EnvironmentFinished(-10),"No-noise charts should preserve native completion");
        Assert(endingNoise.ClampNoise(0,1).EnvironmentFinished(1),"Future noise outside the practice range prevents ending");
        Near(endingNoise.ClampNoise(1,3).NoiseEnd,3,"Practice noise must stop at the selected range end");
        Assert(AlphaNoiseParser.LeadTime(0)==0,"Zero HS caused an enormous prewarning/countdown");
        var normalWarning=Timeline("(120){4},,,,XC[1:1],E");
        var fastWarning=Timeline("(120){4}<SV*2>,,,,XC[1:1],E");
        var warningLead=normalWarning.NoiseEvents.Single().Start-normalWarning.NoiseEvents.Single().WarningStart;
        Near(warningLead,MajdataCore.AlphaVisualTiming.GetTouchMotionDuration(7.5f),"Noise warnings must use Touch, not Tap approach");
        Near(fastWarning.NoiseEvents.Single().Start-fastWarning.NoiseEvents.Single().WarningStart,warningLead/2,"Noise warning ignores SV");
        var warpedWarning=fastWarning.TransformTime(.5,3).NoiseEvents.Single();
        Near(warpedWarning.Start-warpedWarning.WarningStart,warningLead/4,"Practice timeline scales warning lead twice or loses it");
        var hsWarning=Timeline("(120){4}<HS*blockarea=2>,,,,XC[1:1],E");
        var legacyWarning=Timeline("(120){4}<HS*noise=2>,,,,XC[1:1],E");
        Near(legacyWarning.NoiseEvents.Single().WarningStart,hsWarning.NoiseEvents.Single().WarningStart,"legacy noise target must normalize to blockarea");
        var legacyColor=Timeline("(120){4}<COLOR*noise=00BBFF><SIZE*noise=2><ALPHA*noise=.4>XC[1:1],E");
        Assert(legacyColor.Color("blockarea",0,0,false,false,false)==0x00BBFF,"legacy noise COLOR must normalize to blockarea");
        Assert(legacyColor.Size("blockarea",0,0,false,false,false)==(2,2),"legacy noise SIZE must normalize to blockarea");
        Near(legacyColor.Alpha("blockarea",0,0,false,false,false),.4,"legacy noise ALPHA must normalize to blockarea");
        Near(hsWarning.NoiseEvents.Single().Start-hsWarning.NoiseEvents.Single().WarningStart,
            MajdataCore.AlphaVisualTiming.GetTouchMotionDuration(15),"typed noise HS must use the Touch speed curve");
        var globalHSWarning=Timeline("(120){4}<HS*2>,,,,XC[1:1],E");
        Near(globalHSWarning.NoiseEvents.Single().WarningStart,hsWarning.NoiseEvents.Single().WarningStart,"global HS excludes noise");
        Near(fastWarning.NoiseWarningProgress(fastWarning.NoiseEvents.Single(),2-warningLead/4),.5,"SV warning progression must use traveled distance");
        var noiseStyle=Timeline("(120){4}<COLOR*00BBFF><ALPHA*.3><SIZE*2>XC[1:1],<COLORV*blockarea=22FF44><ALPHAV*blockarea=.6>,");
        Assert(noiseStyle.Color("blockarea",0,0,false,false,false)==null,"global COLOR must exclude noise");
        Near(noiseStyle.Alpha("blockarea",0,0,false,false,false),1,"global ALPHA must exclude noise");
        Assert(noiseStyle.Size("blockarea",0,0,false,false,false)==(1,1),"global SIZE must exclude noise");
        Assert(noiseStyle.Color("blockarea",0,.5,false,false,false)==0x22FF44,"typed COLORV must tint active noise");
        Near(noiseStyle.Alpha("blockarea",0,.5,false,false,false),.6,"typed ALPHAV must fade active noise");
        var kaleidoscope=Timeline("(120){4}<KALEIDOSCOPE*(True,0)>1,<KALEIDOSCOPE*(True,90,1)>2,,<KALEIDOSCOPE*(False)>3,");
        Near(kaleidoscope.KaleidoscopeVisibility(0),1,"zero-degree kaleidoscope must remain enabled");
        Near(kaleidoscope.ScreenEffect("Kaleidoscope",1,out _),45,"kaleidoscope transition must rotate through degrees");
        Near(kaleidoscope.KaleidoscopeVisibility(1.5f),0,"kaleidoscope False must clear visibility");
        var noiseStream=Timeline("(120){4}<SV*4>,,,,\n@* {4}<SV*2><HS*blockarea=2><COLOR*blockarea=22FF44>,,,,XC[1:1], *@\nXD1[1:1],E");
        var streamZone=noiseStream.NoiseEvents.Single(zone=>zone.Sensor==16);
        Near(streamZone.Start-streamZone.WarningStart,MajdataCore.AlphaVisualTiming.GetTouchMotionDuration(15)/2,"noise stream lost its own SV/HS");
        Assert(noiseStream.ForNoise(streamZone).Color("blockarea",streamZone.Start,streamZone.Start,false,false,false)==0x22FF44,"noise stream lost its own COLOR");
        Assert(noiseStream.ForNoise(noiseStream.NoiseEvents.Single(zone=>zone.Sensor==17)).Color("blockarea",2,2,false,false,false)==null,"stream COLOR leaked into main noise");
        var transformedNoiseStream=noiseStream.TransformTime(.5,3).NoiseEvents.Single(zone=>zone.Sensor==16);
        Near(transformedNoiseStream.Start-transformedNoiseStream.WarningStart,(streamZone.Start-streamZone.WarningStart)/2,"practice warp lost independent noise stream speed");
        var levels=new float[33];var blocked=new bool[33];
        normalWarning.Noise.Evaluate(normalWarning.NoiseEvents.Single().WarningStart+.01,levels,blocked);
        Assert(levels[16]>0&&!blocked[16]&&normalWarning.Noise.WarningShine(16,normalWarning.NoiseEvents.Single().WarningStart+.01)>0,
            "Prewarning should flash without blocking input");

        foreach (var token in new[] { "1~[5-7[8:1]]", "1h[8:1]~[5-7[8:1]]", "A1~[A1>A4[8:1]]", "Ch[8:1]~[A1-A4[8:1]]", "1$~[5-7[8:1]]" })
        {
            var carrier = AlphaSimaiParser.ParseChart("14", "tester", "(120){4}" + token + ",2,");
            Assert(AlphaSimaiParser.GetWarnings(carrier).Count == 0, "inheritance must parse: " + token);
            Assert(AlphaNoteDecoration.IsCarrier(carrier.NoteTimings[0].Notes.Single()), "carrier decoration lost: " + token);
            Assert(AlphaFakeNotes.JudgableChart(carrier).NoteTimings.ToArray().SelectMany(t=>t.Notes).Count() == 1, "carrier counted for score: " + token);
            Assert(AlphaFakeNotes.IsFake(carrier.NoteTimings[0].Notes[0], carrier.NoteTimings[0]), "carrier not excluded from judgment: " + token);
        }
        var carrierSibling = AlphaSimaiParser.ParseChart("", "", "(120){4}1~[5-7[8:1]]/2,");
        Assert(AlphaNoteDecoration.WithoutCarriers(carrierSibling.NoteTimings[0]).Notes.Single().StartPosition == 2, "carrier must not occupy a hand or turn a real tap into each");
        var allCarriers = AlphaSimaiParser.ParseChart("", "", "(120){4}1~[5-7[8:1]],A1~[A1>A4[8:1]],");
        Assert(AlphaFakeNotes.JudgableChart(allCarriers).NoteTimings.ToArray().All(t=>t.Notes.Length==0), "all carriers must have zero score notes");
        foreach (var token in new[] { "1~[a.jpg]", "1h[8:1]~[1.jpg]", "1b~[a.png]", "1$~[star.png]", "A1~[touch.png]", "Ch[8:1]~[hold.png]", "1-5~[slide.png][8:1]", "1h[8:1]~[skins/a.jpg]" })
        {
            var decorated = AlphaSimaiParser.ParseChart("", "", "(120){4}" + token + "/2,");
            Assert(AlphaSimaiParser.GetWarnings(decorated).Count == 0 && decorated.NoteTimings[0].Notes.Length == 2, "image inheritance parse: " + token);
            var note = decorated.NoteTimings[0].Notes[0];
            Assert(!AlphaNoteDecoration.IsCarrier(note), "image skin treated as trajectory: " + token);
            Assert(AlphaFakeNotes.JudgableChart(decorated).NoteTimings[0].Notes.Length == 2, "image skin removed original judgment: " + token);
            var bare = SimaiParser.ParseChart("(120){4}" + AlphaNoteDecoration.Strip(token) + "/2,").NoteTimings[0].Notes[0];
            Assert(note.Type == bare.Type && note.IsBreak == bare.IsBreak && note.HoldTime == bare.HoldTime && note.SlideTime == bare.SlideTime, "skin changed judgment data: " + token);
        }
        var typedSpeed = Timeline("(120){4}<HS*tap=2,hold=-1,slide=.5>1,");
        Near(typedSpeed.HSpeed("tap",0,1,false,false,false),2,"typed HS tap");
        Near(typedSpeed.HSpeed("hold",0,1,false,false,false),-1,"typed HS signed hold");
        Near(typedSpeed.HSpeed("touch",0,3,false,false,false),3,"typed HS leaves native fallback");
        Near(typedSpeed.HSpeed("slide",0,1,false,false,false),.5,"typed HS slide appearance");
        var bounce = Timeline("(120){4}<BOUNCE*1:1><SPAWNMODE*Once>1,");
        Near(bounce.TransformTime(.5,1).BounceDuration("hold",4,false,false,false),1,"practice scales bounce duration");
        Near(bounce.BounceDuration("hold",4,false,false,false),2,"bounce BPM duration");
        Near(bounce.BounceStart(4,2,1,"tap",false,false,false),2,"bounce takeoff");
        Near(bounce.BounceProgress(3,4,2,1,"tap",false,false,false),.5,"bounce midpoint");
        Near(bounce.BounceRadius(.5f,1,1.225f,4.8f),1.225,"bounce apex");
        Near(bounce.BounceRadius(.5f,-1,1.225f,4.8f),8.375,"negative bounce stays on original key axis");
        Near(bounce.BounceHoldBody(3,4,.125,10,"hold",false,false,false,1.225f,4.8f),1.25,"short hold bounce retains authored length before landing");
        Near(bounce.BounceHoldBody(4.0625,4,.125,10,"hold",false,false,false,1.225f,4.8f),.625,"short bounce hold retracts at constant speed");
        Near(bounce.BounceHoldBody(4,4,2,10,"hold",false,false,false,1.225f,4.8f),3.575,"long bounce hold body clamps to travel length");
        var negativeBounce=Timeline("(120){4}<BOUNCE*1:1><SV*-1>1,");
        Near(negativeBounce.BounceHoldBody(3,4,.125,10,"hold",false,false,false,1.225f,4.8f),-1.25,"negative SV bounce hold retains signed body instead of collapsing");
        Assert(bounce.SpawnMode("star",4,false,false,false)==MajdataCore.SpawnVisualMode.Once,"SPAWNMODE once");
        var bounceReset=Timeline("(180){4}<BOUNCE*1:2>1,<BOUNCE*FALSE>2,");
        Near(bounceReset.BounceDuration("tap",0,false,false,false),8d/3,"bounce uses command BPM");
        Near(bounceReset.BounceDuration("tap",1,false,false,false),0,"bounce FALSE reset");
        var reversedTouch=Timeline("(120){4}<SV*-1>1,,,,<SV*1>2,,,,1,");
        Near(reversedTouch.TouchTiming(0,2,"touch",false,false,false,7),-2,"negative SV Touch must not appear already closed");
        Near(reversedTouch.TouchTiming(0,2,"touchhold",false,false,false,7),-2,"negative SV TouchHold approach");
        Near(reversedTouch.SlideProgress(1,0,2),.5,"global SV does not change slide motion");
        var lines=Timeline("(120){4}<JLINE*(00BBFF80,4:1)><JUDGELINE*0><JUDGELINEEXPAND*(.5,CW)>1,<JUDGELINE*1>2,");
        Assert(lines.DisplayEvents.Count()==4,"judge line commands missing");
        Assert(lines.DisplayEvents.First().RGBA==0x00BBFF80,"judge line RGBA tint");
        Near(lines.DisplayEvents.First().Duration,.5,"judge line beat duration");
        Near(lines.DisplayValue("JUDGELINE",0)!.Value,0,"judge line off");
        Near(lines.DisplayValue("JUDGELINE",.5)!.Value,1,"judge line on");
        Near(lines.DisplayEvents.First(e=>e.Name=="JUDGELINEEXPAND").Value,-22.5,"clockwise expanded line angle");
        var ccw=Timeline("(120){4}<JUDGELINEEXPAND*(1,-1)>1,");
        Near(ccw.DisplayEvents.Single().Value,22.5,"counterclockwise expanded line angle");
        foreach(var arguments in new[]{"True","True,.5,1","true,8:1,CCW","False","false"}) {
            var toggleChart=AlphaSimaiParser.ParseChart("","","(120){4}<JUDGELINEEXPAND*("+arguments+")>1,E");
            Assert(AlphaSimaiParser.GetWarnings(toggleChart).Count==0,"New expand state rejected: "+arguments);
            var entry=AlphaSimaiParser.GetTimeline(toggleChart).DisplayEvents.Single();
            Near(entry.Value,arguments.Equals("False",StringComparison.OrdinalIgnoreCase)?0:arguments.Contains("CCW")?22.5:-22.5,"Expand switch value wrong");
        }
        foreach(var command in new[]{"INNERBRIGHTNESS","COMBODISPLAY","SHOWJUDGEINFO","SHOWCOMBOINFO","SHOWJUDGETEXT","JUDGELINE"}) {
            var resetChart=AlphaSimaiParser.ParseChart("","","(120){4}<"+command+"*(dEfAuLt,8:1)>1,E");
            Assert(AlphaSimaiParser.GetWarnings(resetChart).Count==0,"Display Default rejected: "+command);
            var resetTimeline=AlphaSimaiParser.GetTimeline(resetChart);
            Assert(resetTimeline.DisplayEvents.Single().ResetToDefault,"Display Default became a hardcoded value: "+command);
            Near(resetTimeline.DisplayValue(command,1,.37f)!.Value,.37,"Display Default lost configured baseline");
            Assert(resetTimeline.TransformTime(.5,2).DisplayEvents.Single().ResetToDefault,"Practice copy lost Default");
        }
        var resetBrightness=Timeline("(120){4}<INNERBRIGHTNESS*(.8,1)>1,,,<INNERBRIGHTNESS*(Default,1)>2,E");
        Near(resetBrightness.DisplayValue("INNERBRIGHTNESS",1.5,.37f)!.Value,.8,"Default transition starts from current brightness");
        Near(resetBrightness.DisplayValue("INNERBRIGHTNESS",2,.37f)!.Value,.585,"Default fade midpoint incorrect");
        Near(resetBrightness.DisplayValue("INNERBRIGHTNESS",3,.37f)!.Value,.37,"Default final brightness incorrect");
        Near(resetBrightness.DisplayValue("INNERBRIGHTNESS",.5,.37f)!.Value,.585,"Default seek retains future reset");
        var unsupportedLineStyle=AlphaSimaiParser.ParseChart("","","(120){4}<JUDGELINE*3>1,");
        Assert(unsupportedLineStyle.NoteTimings.Length==1 && AlphaSimaiParser.GetWarnings(unsupportedLineStyle).Count==1,"unsupported judge line style should warn and preserve notes");
        var boundary = AlphaSimaiParser.ParseChart("", "", "(190){4}1,1,1,<COLOR*00BBFF>3h[8:1]/5h[8:1],");
        var firstColored = boundary.NoteTimings[3];
        Assert(AlphaSimaiParser.GetTimeline(boundary).Color("hold", (float)firstColored.Timing, -1, true, false, false) == 0x00BBFF, "COLOR lost on float timestamp boundary");
        foreach (var custom in new[] { "1rp5[4:1]", "1rq5[4:1]", "1P35[4:1]", "1Q35[4:1]", "A1>A4[4:1]", "A1-4[4:1]", "1d-5d[4:1]",
            "1rp3[4:1]", "1rq7[4:1]", "1Q85[4:1]", "A1P3A5[4:1]", "1rp3m[4:1]", "1P35m[4:1]", "1rp3-5[4:1]" })
        {
            var parsedPath = AlphaSimaiParser.ParseChart("", "", "(120){4}" + custom + ",");
            Assert(parsedPath.NoteTimings.Length == 1 && parsedPath.NoteTimings[0].Notes.Length == 1, "Alpha path not parsed: " + custom);
            Near(parsedPath.NoteTimings[0].Notes[0].SlideTime, 0.5, "Alpha duration: " + custom);
            Assert(AlphaSimaiParser.GetWarnings(parsedPath).Count == 0, "Alpha path warning: " + custom);
        }
        foreach (var shape in new[] { "rp", "rq", "P3", "Q8" })
            for (var start = 1; start <= 8; start++)
                for (var end = 1; end <= 8; end++)
                {
                    var token = $"{start}{shape}{end}[8:1]";
                    var parsedPath = AlphaSimaiParser.ParseChart("", "", "(120){4}" + token + ",");
                    Assert(parsedPath.NoteTimings.Length == 1 && parsedPath.NoteTimings[0].Notes.Length == 1 &&
                        parsedPath.NoteTimings[0].Notes[0].RawContent == token && AlphaSimaiParser.GetWarnings(parsedPath).Count == 0,
                        "rp/rq/PQ endpoint matrix: " + token);
                }
        var sizeTest = Timeline("(120){4}<SIZE*(2,0.5)>1,<SIZEV*hold=(3,2)>2,");
        Assert(sizeTest.Size("tap",0,0,false,false,false)==(2,0.5f), "SIZE pair");
        Assert(sizeTest.Size("slide",0,0,false,false,false)==(1,1), "global SIZE must not scale slide paths");
        Assert(sizeTest.Size("hold",0,1,false,false,false)==(3,2), "SIZEV typed pair");
        var liveSize = Timeline("(120){4}<SIZE*hold=(3,2),slide=(4,0.5)>1,<SIZEV*(1.25,0.75)>2,");
        Assert(liveSize.Size("hold",0,1,false,false,false)==(1.25f,.75f), "global SIZEV must override earlier typed Hold SIZE like View");
        Assert(liveSize.Size("slide",0,1,false,false,false)==(4,.5f), "global SIZEV must not resize Slide paths");
        foreach (var type in new[] { "tap", "hold", "star", "touch", "touchhold", "slidestar" })
            Assert(liveSize.Size(type,0,1,false,false,false)==(1.25f,.75f), "SIZEV all visual types: " + type);
        Assert(liveSize.Size("blockarea",0,1,false,false,false)==(1,1),"global SIZEV must exclude noise");
        var resetSize = Timeline("(120){4}<SIZE*hold=(3,2)><SIZEV*hold=(4,5)>1,<SIZEV*hold=NULL>2,");
        Assert(resetSize.Size("hold",0,1,false,false,false)==(3,2), "SIZEV reset restores birth SIZE");
        Assert(Timeline("(120){4}<SIZE*(0,2)>1,").Size("tap",0,0,false,false,false)==(1,2), "zero SIZE dimension uses View's legacy default");
        var noiseSizeChart = AlphaSimaiParser.ParseChart("", "", "(120){4}<SIZE*blockarea=(0.5,0.75)>XC[1:1],<SIZEV*blockarea=(2,3)>,<SIZEV*blockarea=NULL>,");
        var noiseSize = AlphaSimaiParser.GetTimeline(noiseSizeChart);
        Assert(AlphaSimaiParser.GetWarnings(noiseSizeChart).Count == 0, "typed noise SIZE/SIZEV must be recognized");
        Assert(noiseSize.Size("blockarea",0,0,false,false,false)==(.5f,.75f), "typed noise birth SIZE");
        Assert(noiseSize.Size("blockarea",0,.5,false,false,false)==(2,3), "typed noise SIZEV");
        Assert(noiseSize.Size("blockarea",0,1,false,false,false)==(.5f,.75f), "typed noise SIZEV NULL restores birth SIZE");
        var display = Timeline("(120){4}<TEXT*(\"测试,<>\",2,x=0.2,y=0.3,size=40)><SHOWCOMBOINFO*(False,0.5)><INNERBRIGHTNESS*(0.4)>1,");
        Assert(display.HasDisplay && display.DisplayEvents.Count()==3, "display directives missing");
        Near(display.DisplayValue("SHOWCOMBOINFO",.25)!.Value,.5,"combo visibility transition");
        Near(display.DisplayValue("INNERBRIGHTNESS",0)!.Value,.4,"inner brightness");
        var firstHolds = AlphaSimaiParser.ParseChart("14", "tester", "(190)<COLOR*00BBFF>\n{4}3h[8:1]/5h[8:1],3h[8:1]/4h[8:1],2h[8:1]/3h[8:1],1h[8:1]/3h[8:1],\n{8}3h[8:1]/8h[8:1],,3h[8:1]/7h[8:1],,4/6,4/6,4h[8:1]/6h[8:1],,");
        foreach (var timing in firstHolds.NoteTimings)
            foreach (var note in timing.Notes)
                Assert(AlphaSimaiParser.GetTimeline(firstHolds).Color("hold", timing.Timing, timing.Timing - 1, true, false, false) == 0x00BBFF, "first Hold color at " + timing.Timing);
        const string notes = "1/2/3m,4h[4:1],A1,Ch[4:1],1-5[4:1],8P9K1[4:1],";
        var plain = "(120){4}" + notes;
        var prefix = "<SV*0.5><COLOR*tap=FF2200,hold=00FF00><ALPHA*0.3><SPAWN*-7><DESTROY*8><GAUSSIAN*(True,0.4,0.2)>";
        var alpha = AlphaSimaiParser.ParseChart("14+", "tester", "(120){4}" + prefix + notes);
        // Same positions and every upstream note field, including mine and SlideCode flags.
        var clean = SimaiParser.ParseChart("14+", "tester", "(120){4}" + new string(' ', prefix.Length) + notes);
        Assert(JsonSerializer.Serialize(alpha.NoteTimings.ToArray()) == JsonSerializer.Serialize(clean.NoteTimings.ToArray()), "Alpha commands changed upstream notes/judgment data");
        Assert(alpha.NoteTimings.ToArray().SelectMany(x=>x.Notes).Any(x=>x.IsMine), "Mine base missing");
        Assert(alpha.NoteTimings.ToArray().SelectMany(x=>x.Notes).Any(x=>x.RawContent.Contains("K")), "SlideCode base missing");
        var baseline = SimaiParser.ParseChart(plain);
        Assert(alpha.NoteTimings.ToArray().Select(x=>x.Timing).SequenceEqual(baseline.NoteTimings.ToArray().Select(x=>x.Timing)), "Judge timings changed");
        Assert(alpha.NoteTimings.ToArray().Select(x=>x.HSpeed).SequenceEqual(baseline.NoteTimings.ToArray().Select(x=>x.HSpeed)), "HS was modified by SV");

        var sv = Timeline("(120){4}<SV*2>1,<SV*0>2,<SV*-1>3,<SV*1>4,");
        Near(sv.VisualTime(.25, .5,"tap",false,false,false), -.5, "SV 2 integral");
        Near(sv.VisualTime(.5,1,"tap",false,false,false),0,"SV pause");
        Near(sv.VisualTime(1,1.5,"tap",false,false,false),.5,"SV reverse");
        Near(sv.VisualTime(.25,1.5,"hold",false,false,false),0,"Hold spans multiple SV zones");
        Near(sv.VisualTime(1.25,1.25,"tap",false,false,false),0,"Judgment radius anchoring");
        var typed = Timeline("(120){4}<SV*2><SV*hold=0>1,<SV*3>2,<SV*hold=NULL>3,");
        Near(typed.VisualTime(0,1,"hold",false,false,false),0,"typed stop overrides global");
        Near(typed.VisualTime(1,1.5,"hold",false,false,false),-1.5,"NULL restores global");
        Near(typed.VisualTime(0,1,"tap",false,false,false),-2.5,"global fallback");
        var holdOnly=Timeline("(120){4}<SV*hold=0.5>1,");
        Assert(!holdOnly.HasSVFor("tap",false,false,false),"hold SV changed ordinary tap rendering");
        Assert(holdOnly.HasSVFor("hold",false,false,false),"typed hold SV was not activated");
        Near(holdOnly.AppearTime(2,1.5f,.5f,"tap",false,false,false),1.5,"unaffected tap retains upstream pool timing");
        Assert(!Timeline("(120){4}<SV*1>1,").HasSVFor("tap",false,false,false),"identity SV must retain upstream rendering");
        Near(Timeline("(120){4}1,").VisualTime(-1,1,"tap",false,false,false),-2,"plain display parity");
        var appear = Timeline("(120){4}<SV*0.1>1,,,,2,");
        Assert(appear.AppearTime(2,1.5f,.5f,"tap",false,false,false)<0,"slow notes must enter pool early");
        var motion=Timeline("(120){4}<SV*slide=0>1,<SV*slide=2>2,");
        Near(motion.SlideProgress(.25,0,1),0,"slide stop");
        Near(motion.SlideProgress(.75,0,1),.5,"normalized slide motion");
        Near(motion.SlideProgress(1,0,1),1,"original slide endpoint");
        Near(sv.SlideProgress(.25,0,1),.25,"global SV leaves slide trajectory timing alone");

        var colors = Timeline("(120){4}<COLOR*FF0000><COLOR*tap=00FF00,each=0000FF,break=FFFF00,mine=FF00FF><ALPHA*0.4><ALPHA*touch=0>1,<COLORV*tap=FFFFFF><ALPHAV*0.8>2,<COLORV*tap=NULL><ALPHAV*NULL>3,");
        Assert(colors.Color("tap",0,.25,false,false,false)==0x00FF00,"tap COLOR");
        Assert(colors.Color("tap",0,.25,true,false,false)==0x0000FF,"each precedence");
        Assert(colors.Color("tap",0,.25,true,true,false)==0xFFFF00,"break precedence");
        Assert(colors.Color("tap",0,.25,true,true,true)==0xFF00FF,"mine COLOR isolation");
        var mineChart = AlphaSimaiParser.ParseChart("14", "tester",
            "(120){4}<FAKE*True>2!q6m[0.2##0.01]/6!q2m[0.2##0.01],<COLOR*mine=11DDFF>2!q2m[0.2##0.05]/6!q6m[0.2##0.05],");
        var mineTiming = mineChart.NoteTimings.ToArray().First(t => t.Timing > 0 && t.Notes.Any(n => n.IsMineSlide));
        var mineNote = mineTiming.Notes.First(n => n.IsMineSlide);
        Assert(AlphaSimaiParser.GetTimeline(mineChart).ForNote(mineNote)
            .Color("slide", mineTiming.Timing, mineTiming.Timing, false, false, true) == 0x11DDFF,
            "MEGALOVANIA COLOR*mine on a slidecode mine");
        Assert(colors.Color("star",0,.25,false,false,false)==0x00FF00,"star tap fallback");
        Assert(colors.Color("tap",0,.75,false,false,false)==0xFFFFFF,"live COLOR updates active notes");
        Assert(colors.Color("tap",0,1.25,false,false,false)==0x00FF00,"live COLOR reset");
        Near(colors.Alpha("touch",0,.25,false,false,false),0,"zero alpha remains zero");
        Near(colors.Alpha("tap",0,.75,false,false,false),.8,"live alpha");
        Near(colors.Alpha("tap",0,1.25,false,false,false),.4,"alpha reset");
        var comment=Timeline("(120){4}|| <SV*100><COLOR*ZZZZZZ>\n1,");
        Assert(!comment.HasEffects,"comments must not execute commands");
        var curve=Timeline("(120){4}1<5[4:1],");
        Assert(!curve.HasEffects,"ordinary '<' slide mistaken for command");
        var culture=System.Globalization.CultureInfo.CurrentCulture;
        try { System.Globalization.CultureInfo.CurrentCulture=new("fr-FR"); Near(Timeline("(120){4}<SV*0.5>1,").VisualTime(0,1,"tap",false,false,false),-.5,"invariant parameter parsing"); }
        finally { System.Globalization.CultureInfo.CurrentCulture=culture; }
        foreach (var bad in new[]{"<SV*NaN>","<SV*tap=Infinity>","<COLOR*12345>","<ALPHA*oops>","<COLOR*bork=ABCDEF>","<ALPHA*0.3"})
        {
            var recovered=AlphaSimaiParser.ParseChart("14","tester","(120){4}"+bad+"1,");
            Assert(AlphaSimaiParser.GetWarnings(recovered).Count>0,"invalid parameter lacked warning: "+bad);
            Assert(!AlphaSimaiParser.GetTimeline(recovered).HasEffects,"invalid parameter applied partial state: "+bad);
        }
        var modified=AlphaSimaiParser.ParseChart("","","(120){4}<SV*2><COLOR*FF0000>1,<COLOR*0000FF>2,");
        AlphaSimaiParser.TransformTimeline(modified,.5,3);
        var transformed=AlphaSimaiParser.GetTimeline(modified);
        Assert(transformed.Color("tap",3.3,3.3,false,false,false)==0x0000FF,"speed/offset command transform");
        var negative=transformed.TransformTime(1,-4);
        Near(negative.VisualTime(0,1,"tap",false,false,false),-2,"negative practice offset must not reset SV at time zero");
        var clipped=new SimaiChart("","",modified.Fumen,modified.NoteTimings,null);
        AlphaSimaiParser.CopyTimeline(modified,clipped);
        Assert(AlphaSimaiParser.GetTimeline(clipped)==transformed,"practice clip lost commands");
        AlphaSimaiParser.Activate(modified); AlphaSimaiParser.Clear();
        Assert(!AlphaSimaiParser.Current.HasEffects,"previous song effects leaked");
        using var source=new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes("&title=Alpha test\n&artist=test\n&lv_5=14+\n&inote_5=(120){4}<SV*0.5>1,\n&lv_6=15\n&inote_6=(120){4}<ALPHA*oops>1,"));
        var metadata=SimaiParser.ParseMetadataAsync(source).GetAwaiter().GetResult();
        var imported=AlphaSimaiParser.ParseAsync(metadata).GetAwaiter().GetResult();
        Assert(imported.Charts.Length==7,"import lost difficulty slots");
        Assert(AlphaSimaiParser.GetTimeline(imported.Charts[4]).HasSV,"metadata import lost Alpha commands");
        Assert(imported.Charts[5].Fumen.Contains("<ALPHA*oops>") && imported.Charts[5].NoteTimings.Length==1 && AlphaSimaiParser.GetWarnings(imported.Charts[5]).Count==1,"invalid directive must preserve editable source and valid notes");
        var rings=Timeline("(120){4}<SPAWN*-8><DESTROY*12><SPAWN*hold=16><DESTROY*hold=-10>1,<SPAWN*NULL><DESTROY*NULL><SPAWN*hold=NULL><DESTROY*hold=NULL>2,");
        Near(rings.Radius(true,0,"tap",false,false,false),-8,"spawn radius is unrestricted");
        Near(rings.Radius(false,0,"hold",false,false,false),-10,"typed reverse destroy radius");
        Near(rings.Radius(true,.5,"hold",false,false,false),1.225,"spawn reset restores default");
        Near(rings.Radius(false,.5,"hold",false,false,false),4.8,"destroy reset restores default");
        Near(AlphaRingGeometry.Distance(-1,7,12,4.8f),11.8,"inward spawn travels toward destroy");
        Near(AlphaRingGeometry.Distance(0,7,12,-10),-10,"visual arrival stays anchored to custom destroy");
        Near(AlphaRingGeometry.GateRadius(-12,-8,12),-8,"pre-spawn signed radius remains on its own ring");
        Near(AlphaRingGeometry.GateRadius(15,12,4.8f),12,"inward pre-spawn radius gates correctly");
        Near(AlphaRingGeometry.Scale(13,12,4.8f,.25f),.75,"inward fade direction");
        Near(AlphaRingGeometry.Scale(12,12,12,.25f),1,"equal spawn/destroy avoids division by zero");
        var wide=Timeline("(120){4}<SPAWN*-10><DESTROY*10>1,");
        Assert(wide.AppearTime(10,9,1,"tap",false,false,false,7)<7,"longer ring path must allocate notes earlier");
        var effect=Timeline("(120){4}<GAUSSIAN*(True,1,1)>1,<GAUSSIAN*(False,1)>2,<FLASH*(Instant,1,4:1)>3,<MOVE*(True,0.2,-0.1,1)>4,<MOVE*(False,1)>5,");
        Near(effect.ScreenEffect("Gaussian",.25f,out _),.25,"state filter fade-in");
        Near(effect.ScreenEffect("Gaussian",.75f,out _),.375,"turn off during incomplete fade starts from current strength");
        Near(effect.ScreenEffect("Flash",1.25f,out _),1,"instant BPM duration filter");
        Near(effect.ScreenEffect("Flash",1.6f,out _),0,"instant filter ends at duration");
        effect.ScreenMove(1.75f,out var mx,out var my);
        Near(mx,.05,"move X transition"); Near(my,-.025,"move Y transition");
        Near(effect.ScreenEffect("Gaussian",.25f,out _),.25,"seek back reconstructs effect state");
        var fade=Timeline("(120){4}<FADE*(Instant,1,1)>1,");
        Near(fade.ScreenEffect("Flash",.5f,out _),-1,"FADE maps to negative FLASH");
        var envelope=Timeline("(120){4}<TINT*(0.5,1,0.5,0.8,FF0000)>1,");
        Near(envelope.ScreenEffect("Tint",.25f,out var tintEvent),.4,"legacy envelope attack");
        Assert(tintEvent.color=="FF0000","envelope color preserved");
        var sped=fade.TransformTime(.5,-1);
        Near(sped.ScreenEffect("Flash",-.75f,out _),-1,"practice transforms effect durations and time");
        foreach(var bad in new[]{"<SPAWN*touch=1>","<DESTROY*NaN>","<GAUSSIAN*(True,NaN)>","<TINT*(True,NOTHEX,1)>","<FLASH*1>"})
        {
            var recovered=AlphaSimaiParser.ParseChart("14","tester","(120){4}"+bad+"1,");
            Assert(AlphaSimaiParser.GetWarnings(recovered).Count>0,"invalid filter lacked warning: "+bad);
            Assert(!AlphaSimaiParser.GetTimeline(recovered).HasEffects,"invalid filter was applied: "+bad);
        }
        var tolerantSource="(120){4}<SHOWJUDGETEXT*true>1/A1-A4/2,hello/3,<FUTURE42*(1,2,\"a>b\")>4,1<5[4:1],<HS*2>6,";
        var tolerant=AlphaSimaiParser.ParseChart("14","tester",tolerantSource);
        Assert(tolerant.NoteTimings.ToArray().SelectMany(t=>t.Notes).Count()==6,"unknown tokens removed valid siblings");
        Assert(tolerant.NoteTimings.ToArray().SelectMany(t=>t.Notes).Any(n=>n.RawContent=="1<5[4:1]"),"ordinary < slide was stripped");
        Assert(AlphaSimaiParser.GetWarnings(tolerant).Count==4,"unknown directives/notes must each warn");
        Near(tolerant.NoteTimings[^1].Timing,2,"unknown tokens changed comma time");
        Near(tolerant.NoteTimings[^1].HSpeed,2,"native HS compatibility");
        Assert(tolerant.Fumen==tolerantSource,"recovering parser must preserve original source");
        foreach (var duration in new[] { "[4:10]", "[0.25##0.5]", "[120#4:1]" })
        {
            var valid=AlphaSimaiParser.ParseChart("14","tester","(120){4}1-5"+duration+",");
            Assert(valid.NoteTimings.Length==1 && valid.NoteTimings[0].Notes.Length==1 && AlphaSimaiParser.GetWarnings(valid).Count==0,"valid zero/BPM duration was discarded: "+duration);
        }
        foreach (var nativeNote in new[] { "1hb[8:1]", "1hmx[#0.25]", "B1hf[8:1]", "B1hbmx[120#4:1]", "B1h", "1$$", "1?-3[8:1]", "1!-3[8:1]", "1@-3[8:1]", "1-3[8:1]*-7[8:1]", "1-3[8:1]-5[8:1]" })
        {
            var nativeChart=AlphaSimaiParser.ParseChart("14","tester","(120){4}"+nativeNote+",");
            Assert(nativeChart.NoteTimings.Length==1 && nativeChart.NoteTimings[0].Notes.Length>0 && AlphaSimaiParser.GetWarnings(nativeChart).Count==0,"documented native note was discarded: "+nativeNote);
        }
        var multiline=AlphaSimaiParser.ParseChart("14","tester","(120){4}1,\nA1-A4/2,3,");
        Assert(AlphaSimaiParser.GetWarnings(multiline)[0].Line==2 && AlphaSimaiParser.GetWarnings(multiline)[0].Column==1,"warning source location");
        Near(multiline.NoteTimings[^1].Timing,1,"skipped notes do not collapse slots");
        var empty=AlphaSimaiParser.ParseChart("14","tester","(120){4}A1-A4,中文,unknown,");
        Assert(empty.NoteTimings.IsEmpty && AlphaSimaiParser.GetWarnings(empty).Count==3,"all unsupported slots should produce an empty playable result");
        foreach (var badControl in new[] { "<HS*bogus>", "(oops)", "{oops}" })
        {
            var control=AlphaSimaiParser.ParseChart("14","tester","(120){4}1,"+badControl+"2,3,");
            Assert(AlphaSimaiParser.GetWarnings(control).Count>0 && control.NoteTimings.ToArray().SelectMany(t=>t.Notes).Count()==3,"invalid time control must only warn: "+badControl);
            Near(control.NoteTimings[^1].Timing,1,"invalid time control must preserve previous settings");
        }
        var fake=AlphaSimaiParser.ParseChart("14","tester","(120){4}<FAKE*true>1,2h[4:1],A1,Ch[4:1],1-5[4:1],8P9K1[4:1],<FAKE*false>7,");
        var fakeStats=AlphaFakeNotes.JudgableChart(fake);
        Assert(fakeStats.NoteTimings.ToArray().SelectMany(t=>t.Notes).Count()==1,"global fake must exclude all note types from score/counts");
        Assert(fake.NoteTimings.ToArray().SelectMany(t=>t.Notes).Count()==7,"fake visuals were removed from original chart");
        var fakeHead=AlphaSimaiParser.ParseChart("14","tester","(120){4}<FAKE*star=1>1-5[4:1],");
        var headStats=AlphaFakeNotes.JudgableChart(fakeHead).NoteTimings[0].Notes[0];
        Assert(headStats.Type==SimaiNoteType.Slide && headStats.IsSlideNoHead,"fake star must preserve real slide");
        Assert(!fakeHead.NoteTimings[0].Notes[0].IsSlideNoHead,"count projection mutated visual star");
        var fakeSlide=AlphaSimaiParser.ParseChart("14","tester","(120){4}<FAKE*slide=true>1-5[4:1],");
        Assert(AlphaFakeNotes.JudgableChart(fakeSlide).NoteTimings[0].Notes[0].Type==SimaiNoteType.Tap,"fake slide must preserve real star-head score");
        var fakeNoHead=AlphaSimaiParser.ParseChart("14","tester","(120){4}<FAKE*slide=true>1-5![4:1],");
        Assert(AlphaFakeNotes.JudgableChart(fakeNoHead).NoteTimings[0].Notes.Length==0,"fake no-head slide should not invent a scored head");
        var fakeTyped=Timeline("(120){4}<FAKE*1><FAKE*tap=0,break=0,each=0>1,");
        Assert(!fakeTyped.IsFake("tap",0,false,false),"typed false overrides global fake");
        Assert(fakeTyped.IsFake("hold",0,false,false),"global fake remains for other types");
        Assert(!fakeTyped.IsFake("hold",0,false,true),"fake break override");
        Assert(!fakeTyped.IsFake("hold",0,true,false),"fake each override");
        var practiceTarget=AlphaSimaiParser.ParseChart("14","tester",fake.Fumen);
        AlphaSimaiParser.TransformTimeline(practiceTarget,.5,-1);
        Assert(!AlphaSimaiParser.GetTimeline(practiceTarget).IsFake("tap",2,false,false),"practice fake reset time must transform");
        var warningsCopy=new SimaiChart("14","tester",tolerant.Fumen,tolerant.NoteTimings,tolerant.CommaTimings);
        AlphaSimaiParser.CopyTimeline(tolerant,warningsCopy);
        Assert(AlphaSimaiParser.GetWarnings(warningsCopy).Count==4,"practice warning copy");
        var hanabi = AlphaSimaiParser.ParseChart("14", "test", "(120){4}1f,2,2f-6v4[1:1],A1-A4/3,");
        Assert(hanabi.NoteTimings[0].Notes[0].IsHanabi && hanabi.NoteTimings[0].Notes[0].Type == SimaiNoteType.Tap, "Tap f flag must preserve tap type");
        Assert(!hanabi.NoteTimings[1].Notes[0].IsHanabi, "Firework flag leaked to next note");
        Assert(hanabi.NoteTimings[2].Notes[0].IsHanabi && hanabi.NoteTimings[2].Notes[0].Type == SimaiNoteType.Slide, "Firework slide head must preserve slide");
        Assert(hanabi.NoteTimings[3].Notes.Length == 1 && hanabi.NoteTimings[3].Notes[0].StartPosition == 3, "Unsupported touchslide must preserve same-slot tap");
        Console.WriteLine($"PASS: {checks} regression checks. Upstream note timings, lengths, flags and input semantics preserved.");
    }
}
