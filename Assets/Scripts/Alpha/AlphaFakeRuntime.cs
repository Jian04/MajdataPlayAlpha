using MajdataPlay.Alpha;
using UnityEngine;

namespace MajdataPlay.Scenes.Game.Notes.Behaviours
{
    // Separate visual lifetimes: no input simulation, sounds, feedback, score or queue advances.
    internal sealed partial class TapDrop
    {
        void UpdateFakeLifetime()
        {
            if (IsEnded) return;
            // View hides Mine heads at their beat; Fake mines need no later collision window.
            if (ThisFrameSec < Timing + (IsMine ? 0 : TAP_JUDGE_GOOD_AREA_MSEC / 1000)) return;
            State = NoteStatus.End;
            _eachLineBinding?.Unbind(this);
            _eachLineBinding = null;
            SetActive(false);
            RendererState = RendererStatus.Off;
            _notePoolManager.Collect(this);
        }
    }
    internal sealed partial class HoldDrop
    {
        void UpdateFakeLifetime()
        {
            if (IsEnded) return;
            if (ThisFrameSec < Timing + Length + TAP_JUDGE_GOOD_AREA_MSEC / 1000) return;
            State = NoteStatus.End;
            _eachLineBinding?.Unbind(this);
            _eachLineBinding = null;
            SetActive(false);
            RendererState = RendererStatus.Off;
            _lastHeadState = HOLD_HEAD_STATE_NOT_JUDGED;
            _lastHoldState = HOLD_STATE_NONE;
            _thisRenderer.sharedMaterial = DefaultMaterial;
            _poolManager.Collect(this);
        }
    }
    internal sealed partial class TouchDrop
    {
        void UpdateFakeLifetime()
        {
            if (IsEnded) return;
            if (ThisFrameSec < Timing + TOUCH_JUDGE_GOOD_AREA_MSEC / 1000) return;
            State = NoteStatus.End;
            SetActive(false);
            RendererState = RendererStatus.Off;
            _notePoolManager.Collect(this);
        }
    }
    internal sealed partial class TouchHoldDrop
    {
        void UpdateFakeLifetime()
        {
            if (IsEnded) return;
            if (ThisFrameSec < Timing + Length + TOUCH_JUDGE_GOOD_AREA_MSEC / 1000) return;
            State = NoteStatus.End;
            SetActive(false);
            RendererState = RendererStatus.Off;
            _lastHeadState = HOLD_HEAD_STATE_NOT_JUDGED;
            _lastHoldState = HOLD_STATE_NONE;
            _notePoolManager.Collect(this);
        }
    }
    internal abstract partial class SlideBase
    {
        protected void UpdateFakeLifetime()
        {
            if (IsEnded) return;
            SlideBarFadeIn();
            if (ThisFrameSec < ArriveTiming) return;
            HideAllBar();
            ClearAllJudgeQueue();
            SetStarActive(false);
            State = NoteStatus.End;
            SetActive(false);
        }
        protected void UpdateFakeSlideVisuals()
        {
            var progress = NoteTimeline.SlideProgress(ThisFrameSec, StartTiming, Length);
            if (this is WifiDrop)
            {
                if (progress > 0.7f) HideBar(9);
                else if (progress > 0.4f) HideBar(5);
                else if (progress > 0.15f) HideBar(2);
                return;
            }
            var queue = JudgeQueues[0].Span;
            var passed = Mathf.Clamp((int)(progress * JudgeQueueLength), 0, queue.Length);
            if (passed > 0) HideBar(queue[passed - 1].ArrowProgressWhenFinished);
        }
    }
}
