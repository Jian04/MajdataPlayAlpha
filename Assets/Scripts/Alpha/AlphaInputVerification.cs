#if UNITY_EDITOR
using System;
using System.Reflection;
using MajdataPlay.IO;
using MajdataPlay.Scenes.Game.Buffers;
using MajdataPlay.Scenes.Game.Notes.Controllers;
using UnityEngine;

namespace MajdataPlay.Editor
{
    internal static class AlphaInputVerification
    {
        static int _checks;
        static void Require(bool condition, string name) { if (!condition) throw new Exception(name); _checks++; }
        static T Field<T>(object obj, string name) => (T)obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(obj);
        internal static void Run()
        {
            _checks = 0;
            var original = MajEnv.Settings;
            typeof(MajEnv).GetProperty("Settings").SetValue(null, new MajdataPlay.Settings.GameSetting());
            var root = new GameObject("Alpha D input verification");
            var manager = root.AddComponent<NoteManager>();
            var buttons = Field<SwitchStatus[]>(manager, "_btnStatusInThisFrame");
            var previous = Field<SwitchStatus[]>(manager, "_btnStatusInPreviousFrame");
            var next = Field<SwitchStatus[]>(manager, "_btnStatusInNextFrame");
            var sensors = Field<SwitchStatus[]>(manager, "_sensorStatusInThisFrame");
            var clicked = Field<bool[]>(manager, "_isBtnClickedInThisFrame");
            var used = Field<bool[]>(manager, "_isBtnUsedInThisFrame");
            try
            {
                for (var d = 1; d <= 8; d++)
                {
                    var neighbor = (d + 6) % 8;
                    var area = SensorArea.D1 + d - 1;
                    Array.Clear(buttons, 0, 8); Array.Clear(previous, 0, 8);
                    Array.Clear(clicked, 0, 8); Array.Clear(used, 0, 8); Array.Clear(sensors, 0, 33);
                    using (manager.AlphaInput(d, false))
                    {
                        foreach (var button in new[] { d - 1, neighbor })
                        {
                            clicked[button] = true;
                            Require(manager.IsButtonClickedInThisFrame((ButtonZone)(d - 1)), $"{d}d rejects outer key {button + 1}");
                            Require(manager.TryUseButtonClickEvent((ButtonZone)(d - 1)), "D key event not consumed");
                            Require(!manager.TryUseButtonClickEvent((ButtonZone)(d - 1)), "D key consumed same event twice");
                            clicked[button] = false; used[button] = false;
                        }
                        clicked[(d + 2) % 8] = true;
                        Require(!manager.IsButtonClickedInThisFrame((ButtonZone)(d - 1)), "Unrelated outer button judged D head");
                        Array.Clear(clicked, 0, 8);
                        buttons[neighbor] = SwitchStatus.On; previous[d - 1] = SwitchStatus.On;
                        Require(manager.CheckButtonStatusInThisFrame((ButtonZone)(d - 1), SwitchStatus.On) && manager.CheckButtonStatusInPreviousFrame((ButtonZone)(d - 1), SwitchStatus.On), "D Hold swapping neighbors became a release");
                        Array.Clear(buttons, 0, 8);
                        sensors[(int)area] = SwitchStatus.On;
                        Require(manager.GetSensorStatusInThisFrame(area) == SwitchStatus.On, "D sensor rejected");
                        sensors[(int)area] = SwitchStatus.Off;
                        sensors[d - 1] = SwitchStatus.On;
                        Require(manager.GetSensorStatusInThisFrame(area) == SwitchStatus.Off, "A touch was treated as D");
                        Array.Clear(next, 0, 8);
                        manager.SimulateButtonClick((ButtonZone)(d - 1));
                        Require(next[d - 1] == SwitchStatus.On && next[neighbor] == SwitchStatus.Off, "D DJAuto used two keys");
                        next[d - 1] = SwitchStatus.Off; buttons[d - 1] = SwitchStatus.On;
                        manager.SimulateButtonClick((ButtonZone)(d - 1));
                        Require(next[neighbor] == SwitchStatus.On, "D DJAuto did not choose the free neighbor");
                        Array.Clear(next, 0, 8);
                        manager.SimulateButtonPress((ButtonZone)(d - 1));
                        Require(next[d - 1] == SwitchStatus.On && next[neighbor] == SwitchStatus.Off, "D Hold DJAuto drifted to a second hand");
                    }
                    Array.Clear(buttons, 0, 8); buttons[neighbor] = SwitchStatus.On;
                    using (manager.AlphaInput(0, true))
                        Require(manager.GetSensorStatusInThisFrame(area) == SwitchStatus.On, "D path rejected adjacent button");
                    Require(manager.GetSensorStatusInThisFrame(area) == SwitchStatus.Off, "D mapping leaked into native SlideCode/ordinary slide");
                    var expected = MajdataPlay.Alpha.AlphaPathMetadata.Position('K', d, true);
                    var actual = Quaternion.Euler(0, 0, 22.5f) * MajdataPlay.Scenes.Game.Utils.NoteHelper.GetTapPosition(d, 4.8f);
                    Require(Vector3.Distance(actual, expected) < .001f, "D head and D path disagree on coordinates");
                }
                var a = new TapQueueInfo { KeyIndex = 1, Index = 0 };
                var dQueue = new TapQueueInfo { KeyIndex = 9, Index = 0 };
                manager.NextNote(dQueue);
                Require(manager.IsCurrentNoteJudgeable(a) && !manager.IsCurrentNoteJudgeable(new TapQueueInfo { KeyIndex = 1, Index = 1 }), "D queue advanced normal A queue");
                Require(manager.IsCurrentNoteJudgeable(new TapQueueInfo { KeyIndex = 9, Index = 1 }), "D queue failed to advance");
                manager.ResetCounter();
                Require(!manager.IsCurrentNoteJudgeable(new TapQueueInfo { KeyIndex = 9, Index = 1 }), "D queue was not reset between charts");
                Debug.Log($"ALPHA INPUT CHECK PASS: {_checks} D input, queues, Hold, DJAuto and native isolation checks.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                typeof(MajEnv).GetProperty("Settings").SetValue(null, original);
            }
        }
    }
}
#endif
