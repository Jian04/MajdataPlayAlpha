using System;
using System.Collections.Generic;
using System.Diagnostics;
using MajdataCore;
using MajdataPlay.Alpha;

namespace UnityEngine { static class Mathf { public static float Clamp01(float v) => Math.Clamp(v, 0, 1); } }
static class NewSyntaxRegression
{
    static int checks;
    static void Check(bool ok, string message) { checks++; if (!ok) throw new Exception(message); }
    static void Near(double actual, double expected, string message, double tolerance = .00005) =>
        Check(Math.Abs(actual-expected) <= tolerance, message+": "+actual+" != "+expected);
    static AlphaVisualTimeline Timeline(string command)
    {
        var chart = AlphaSimaiParser.ParseChart("", "", "(120){4}"+command+"1,,,,,,,,,2,");
        Check(AlphaSimaiParser.GetWarnings(chart).Count == 0, "New command rejected: "+command);
        return AlphaSimaiParser.GetTimeline(chart);
    }
    public static void Run()
    {
        foreach (var easing in new[]{"linear", "easein,3", "easeout,3", "easeinout,3", "sigmoid,8"})
        {
            var value="(1,3,2,"+easing+")";
            var timeline=Timeline("<SV*"+value+">");
            AlphaCommandGrammar.TryFind("SV",out var descriptor);
            Check(NoteValueTransition.TryParse(value,120,descriptor.scalar,out var transition),"SV transition parse");
            SvController.Load(new List<SvPoint>{new(){time=0,multiplier=1,transition=transition}});
            for (var i=-20; i<=100; i++) Near(timeline.ScrollAt(i*.05,"tap",false,false,false),SvController.GetCumulativeScroll(i*.05),"SV integral matches View "+easing);
        }
        var resetChart=AlphaSimaiParser.ParseChart("","","(120){4}<SV*(1,3,2,linear)>1,,<SV*tap=(-1,2,1,sigmoid,8)>2,,,,<SV*tap=NULL>3,");
        var reset=AlphaSimaiParser.GetTimeline(resetChart);
        AlphaCommandGrammar.TryFind("SV",out var svSpec);
        NoteValueTransition.TryParse("(1,3,2,linear)",120,svSpec.scalar,out var global);
        NoteValueTransition.TryParse("(-1,2,1,sigmoid,8)",120,svSpec.scalar,out var typed);
        SvController.Load(new List<SvPoint>{new(){time=0,transition=global},new(){time=1,noteType="tap",transition=typed},new(){time=3,noteType="tap",reset=true}});
        for (var i=-10; i<100; i++) Near(reset.ScrollAt(i*.05,"tap",false,false,false),SvController.GetCumulativeScroll(i*.05,"tap"),"Typed SV and reset match View");
        foreach(var type in new[]{"tap","hold","star","touch","touchhold"})
        {
            var timeline=Timeline("<HS*"+type+"=(1,2,1,easeinout,2)>");
            AlphaCommandGrammar.TryFind("HS",out var hsSpec);
            NoteValueTransition.TryParse("(1,2,1,easeinout,2)",120,hsSpec.scalar,out var hs);
            SvController.Load(new List<SvPoint>());
            var distance=type.StartsWith("touch")?AlphaVisualTiming.GetTouchMotionDuration(10):(4.8-1.225+AlphaVisualTiming.SpawnScaleDistance)/10;
            double appeared=4-distance;
            var curve=SvController.CreateBirthSpeedCurve(type,4,distance,hs,6,ref appeared);
            var actual=timeline.AppearTime(4,(float)(4-distance),(float)distance,type,false,false,false,10,1,10);
            Near(actual,appeared,"HS appearance matches View "+type);
            for(var i=0;i<=100;i++) Near(timeline.VisualTime(i*.06,4,type,false,false,false),SvController.GetCumulativeScroll(i*.06,curve)-SvController.GetCumulativeScroll(4,curve),"HS motion matches View "+type,.0002);
        }
        var reversing=Timeline("<SV*(1,-1,1,linear)>");
        Near(reversing.BounceDirection(.75,"tap",false,false,false,1),-1,"BOUNCE follows interpolated negative SV");
        var zeroStart=Timeline("<HS*tap=(0,2,1,linear)>");
        var zeroAppeared=zeroStart.AppearTime(4,float.NegativeInfinity,0,"tap",false,false,false,0,0,10);
        Check(float.IsFinite(zeroAppeared) && zeroAppeared < 4,"HS zero start must solve finite appearance");
        var angle=Timeline("<ANGLE*30><ANGLEV*tap=(0,90,2,linear)>");
        Near(angle.Angle("tap",0,1,false,false,false),45,"ANGLEV linear rotation");
        Near(angle.Angle("hold",0,1,false,false,false),30,"Default ANGLE includes Hold");
        foreach(var type in new[]{"slide","slidestar","blockarea"}) Near(angle.Angle(type,0,1,false,false,false),0,"Default ANGLE exclusion "+type);
        Near(Timeline("<ANGLEV*slidestar=90>").Angle("slidestar",0,1,false,false,false),90,"Explicit guide rotation");
        var color=Timeline("<COLORV*(FF0000,0000FF,2,linear)>");
        Check(color.Color("tap",0,1,false,false,false)==0x800080,"RGB interpolation");
        Check(color.Color("tap",0,1,false,false,true)==null,"Global COLOR must exclude mines");
        Check(color.Color("blockarea",0,1,false,false,false)==null,"Global COLOR must exclude blockarea");
        Check(Timeline("<COLORV*mine=(FF0000,0000FF,2,linear)>").Color("tap",0,1,false,false,true)==0x800080,"Explicit mine RGB curve");
        var birth=Timeline("<ALPHA*(0,1,2,linear)><SIZE*((1,2),(3,4),2,linear)>");
        birth.RememberAppearance("tap",2,false,false,false,1);
        Near(birth.Alpha("tap",2,2,false,false,false),.5,"Birth alpha starts at appearance");
        Near(birth.Size("tap",2,2,false,false,false).X,2,"Birth size X");
        Near(birth.Size("tap",2,2,false,false,false).Y,3,"Birth size Y");
        Near(Timeline("<SPAWN*(1,3,2,linear)>").AnimatedRadius(true,0,1,"tap",false,false,false),2,"Animated SPAWN radius");
        Near(Timeline("<DESTROY*(4.8,0,2,linear)>").AnimatedRadius(false,0,1,"tap",false,false,false),2.4,"Animated DESTROY radius");
        var noise=Timeline("<HS*blockarea=(1,2,1,linear)>XD1[8:1],");
        Check(noise.HasNoise && noise.NoiseEvents[0].WarningStart<noise.NoiseEvents[0].Start,"Blockarea HS warning window");
        Check(AlphaSimaiParser.GetWarnings(AlphaSimaiParser.ParseChart("","","(120){4}<BOUNCE*(1,2,2,linear)>1,")).Count==1,"BOUNCE must not accept a value curve");
        var textEvent=AlphaDisplayParser.Parse("TEXT*(\"RGB\",2,0.1,0.2,30,Allerta-Regular.ttf,0,Fade,0.5,00BBFF)",0,120);
        Check(textEvent.Subtitle.color=="00BBFF" && textEvent.Subtitle.font=="Allerta-Regular.ttf","TEXT positional RGB and file font");
        Check(AlphaDisplayParser.Parse("TEXT*(\"RGB\",2,font=Fonts/a.otf,color=FF8800)",0,120).Subtitle.color=="FF8800","TEXT named RGB");
        foreach (var newline in new[]{"\n", "\r\n"})
        {
            var content = "第一行 > 0" + newline + "第二行 || 1,2";
            var multiline = Timeline("<TEXT*(\"" + content + "\",2)>");
            Check(multiline.DisplayEvents.Length == 1 && multiline.DisplayEvents[0].Subtitle.text == content,
                "Multiline TEXT content must match View without a warning");
        }
        var stress=Timeline("<ANGLEV*tap=(0,90,2,sigmoid,8)><ALPHAV*(0,1,2,linear)><COLORV*(FF0000,0000FF,2,linear)><SIZEV*(1,2,2,linear)>");
        double Sample(int frames)
        {
            double sum=0;
            for(var i=0;i<frames;i++) for(var n=0;n<1000;n++)
            {
                var now=i/120d;
                sum+=stress.Angle("tap",n*.01,now,false,false,false)+stress.Alpha("tap",n*.01,now,false,false,false);
                sum+=stress.Color("tap",n*.01,now,false,false,false)??0;
                sum+=stress.Size("tap",n*.01,now,false,false,false).X;
            }
            return sum;
        }
        Sample(2); var timer=new Stopwatch(); var before=GC.GetAllocatedBytesForCurrentThread();timer.Start();var result=Sample(120);timer.Stop();
        var allocated=GC.GetAllocatedBytesForCurrentThread()-before;
        Check(allocated==0,"Playback property lookups allocated "+allocated+" bytes");
        Console.WriteLine($"NEW SYNTAX PASS: {checks} checks, direct View SV/HS comparison; 120 frames x 1000 notes, {timer.Elapsed.TotalMilliseconds:F2} ms, {allocated} B; checksum={result}");
    }
}
