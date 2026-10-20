using System;
using MajdataPlay.IO;

namespace MajdataPlay.Scenes.Game.Notes.Controllers
{
    internal partial class NoteManager
    {
        readonly int[] _alphaDCurrentIndex = new int[8];
        int _alphaDHead;
        bool _alphaDPath;
        internal int AlphaDHead => _alphaDHead;
        internal AlphaInputScope AlphaInput(int head, bool path) => new(this, head, path);
        internal readonly struct AlphaInputScope : IDisposable
        {
            readonly NoteManager _manager;
            readonly int _head;
            readonly bool _path;
            public AlphaInputScope(NoteManager manager, int head, bool path)
            { _manager = manager; _head = manager._alphaDHead; _path = manager._alphaDPath; manager._alphaDHead = head; manager._alphaDPath = path; }
            public void Dispose() { _manager._alphaDHead = _head; _manager._alphaDPath = _path; }
        }
        static int PreviousButton(int index) => (index + 7) % 8;
        SwitchStatus AlphaButtonState(ButtonZone zone, SwitchStatus[] states)
        {
            if(MajdataPlay.Alpha.AlphaNoiseInputGate.IsBlocked(_alphaDHead>0 ? SensorArea.D1+_alphaDHead-1 : (SensorArea)zone)) return SwitchStatus.Off;
            if (_alphaDHead == 0) return states[(int)zone];
            var index = _alphaDHead - 1;
            return states[index] | states[PreviousButton(index)];
        }
        SwitchStatus AlphaSensorState(SensorArea area, SwitchStatus[] sensors, SwitchStatus[] buttons)
        {
            if(MajdataPlay.Alpha.AlphaNoiseInputGate.IsBlocked(area)) return SwitchStatus.Off;
            var state = sensors[(int)area];
            if (!_alphaDPath || area < SensorArea.D1 || area > SensorArea.D8) return state;
            var index = area - SensorArea.D1;
            return state | buttons[index] | buttons[PreviousButton(index)];
        }
        bool AlphaButtonUnused(int index)
        {
#if UNITY_ANDROID || UNITY_IOS
            return _btnUsedCountInThisFrame[index] < _btnClickedCountInThisFrame[index];
#else
            return !_isBtnUsedInThisFrame[index];
#endif
        }
        ButtonZone? AlphaClickedButton()
        {
            var index = _alphaDHead - 1;
            if (_isBtnClickedInThisFrame[index] && AlphaButtonUnused(index)) return (ButtonZone)index;
            index = PreviousButton(index);
            if (_isBtnClickedInThisFrame[index] && AlphaButtonUnused(index)) return (ButtonZone)index;
            return null;
        }
        ButtonZone AlphaSimulationButton(bool press = false)
        {
            var index = _alphaDHead - 1;
            if (press)
            {
                if (_btnStatusInThisFrame[index] == SwitchStatus.On || _btnStatusInNextFrame[index] == SwitchStatus.On) return (ButtonZone)index;
                var previous = PreviousButton(index);
                if (_btnStatusInThisFrame[previous] == SwitchStatus.On || _btnStatusInNextFrame[previous] == SwitchStatus.On) return (ButtonZone)previous;
                return (ButtonZone)index;
            }
            if (_btnStatusInThisFrame[index] == SwitchStatus.On || _btnStatusInNextFrame[index] == SwitchStatus.On) index = PreviousButton(index);
            return (ButtonZone)index;
        }
    }
}
