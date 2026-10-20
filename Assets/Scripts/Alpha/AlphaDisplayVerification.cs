#if UNITY_EDITOR
using System;
using System.Reflection;
using MajdataPlay.Alpha;
using UnityEngine;

namespace MajdataPlay.Editor
{
    internal static class AlphaDisplayVerification
    {
        internal static void Run()
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var root = new GameObject("Play display switches verification");
            try
            {
                var original = root.AddComponent<SpriteRenderer>();
                original.color = Color.white;
                var display = root.AddComponent<AlphaDisplayController>();
                var type = typeof(AlphaDisplayController);
                type.GetField("_judgeLine", flags).SetValue(display, original);
                type.GetField("_judgeLineBaseline", flags).SetValue(display, Color.white);
                var chart = AlphaSimaiParser.ParseChart("", "", "(120){4}<JUDGELINEEXPAND*(True,8:1,CW)>1,<JUDGELINEEXPAND*(False)>2,<JUDGELINEEXPAND*(True,.5,CCW)>3,E");
                if (AlphaSimaiParser.GetWarnings(chart).Count != 0) throw new Exception("New expand states still warn");
                var timeline = AlphaSimaiParser.GetTimeline(chart);
                var apply = type.GetMethod("ApplyJudgeLine", flags);
                void At(float time, bool visible, float angle = 0)
                {
                    apply.Invoke(display, new object[] { timeline, time });
                    var line = (SpriteRenderer)type.GetField("_expandedLine", flags).GetValue(display);
                    if ((line != null && !line.forceRenderingOff) != visible)
                        throw new Exception("Wrong Play expansion switch at " + time);
                    if (visible && Mathf.Abs(Mathf.DeltaAngle(line.transform.localEulerAngles.z, angle)) > .001f)
                        throw new Exception("Wrong Play expansion angle at " + time);
                    if (original.forceRenderingOff || original.color != Color.white || root.transform.rotation != Quaternion.identity)
                        throw new Exception("Expanded switch changed original judge line");
                }
                At(-1, false); At(.125f, true, -11.25f); At(.5f, false);
                At(1.25f, true, 11.25f); At(.75f, false); At(.25f, true, -22.5f);
                At(-1, false); At(1.5f, true, 22.5f);
                Debug.Log("ALPHA DISPLAY SWITCH CHECK PASS: True/False/re-enable, both directions, forward/backward seeking and original line preserved.");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
    }
}
#endif
