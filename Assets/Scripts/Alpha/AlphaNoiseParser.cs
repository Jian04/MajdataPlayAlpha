using System;
using MajdataCore;

namespace MajdataPlay.Alpha
{
    // Environment events are stripped before MajSimai counts/judges notes.
    internal static class AlphaNoiseParser
    {
        internal static bool TryRead(char[] text,int start,out int end,out int sensor,out string duration)
        {
            end=start; sensor=-1; duration="";
            if(start>0 && (char.IsLetterOrDigit(text[start-1]) || text[start-1]=='~')) return false;
            int i=start+1; if(i>=text.Length) return false;
            char area=char.ToUpperInvariant(text[i++]); int position=1;
            if(area!='C') { if(i>=text.Length || text[i]<'1' || text[i]>'8') return false; position=text[i++]-'0'; }
            sensor=NoiseZoneTimeline.SensorIndex(area,position); if(sensor<0) return false;
            if(i<text.Length && text[i]=='[') {
                int closing=Array.IndexOf(text,']',i+1); if(closing<0) return false;
                duration=new string(text,i+1,closing-i-1); i=closing+1;
            }
            if(i<text.Length && !char.IsWhiteSpace(text[i]) && text[i]!=',' && text[i]!='/' && text[i]!='`' && text[i]!='<') return false;
            end=i; return true;
        }
        internal static bool TryDuration(string value,float bpm,out float duration)
        {
            if(value.Length==0) return Default(out duration);
            var valid=SlideSyntaxValidator.TryGetLengthSeconds("["+value+"]",bpm,out var seconds);
            duration=(float)seconds;
            return valid && seconds>=0 && !double.IsInfinity(seconds) && !double.IsNaN(seconds);
        }
        static bool Default(out float duration) { duration=0; return true; }
        internal static float LeadTime(float hs)
        {
            float speed=7.5f;
#if UNITY_5_3_OR_NEWER
            speed=MajEnv.Settings?.Game.TouchSpeed ?? speed;
#endif
            return Math.Abs(speed * hs) <= AlphaVisualTiming.Epsilon ? 0 : AlphaVisualTiming.GetTouchMotionDuration(speed * hs);
        }
    }
}
