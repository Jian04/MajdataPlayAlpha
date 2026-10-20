using MajdataPlay.Settings;
using MajdataPlay.IO;
using MajdataPlay.Utils;
using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using MajdataPlay.Editor;
using UnityEngine;
using Random = System.Random;
using MajdataPlay.Scenes.View;
using MajdataPlay.Scenes.Game.Notes.Controllers;
using MajdataPlay.Numerics;
#nullable enable
namespace MajdataPlay.Scenes.Game.Notes.Behaviours
{
    internal abstract class NoteDrop : MajComponent, IStatefulNote
    {
        public int StartPos
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _startPos;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set
            {
                if (value.InRange(1, 8))
                    _startPos = value;
                else
                    throw new ArgumentOutOfRangeException("Start position must be between 1 and 8");
            }
        }
        public float Timing
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _timing;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => _timing = value;
        }
        public int SortOrder
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _sortOrder;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => _sortOrder = value;
        }
        public float Speed
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _speed;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => _speed = value;
        }
        public bool IsEach
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _isEach;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => _isEach = value;
        }
        public bool IsBreak
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _isBreak;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => _isBreak = value;
        }
        public bool IsEX
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _isEX;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => _isEX = value;
        }
        public bool IsMine
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _isMine;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => _isMine = value;
        }
        public bool IsFake { get; set; }
        public bool IsInited
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => State >= NoteStatus.Inited;
        }
        public bool IsEnded
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => State == NoteStatus.End;
        }
        public bool IsClassic
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => USERSETTING_SLIDE_JUDGE_MODE == JudgeModeOption.Classic;
        }
        public NoteStatus State
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _state;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            protected set => _state = value;
        }
        public float JudgeTimingWithOffset
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => JudgeTiming + USERSETTING_JUDGE_OFFSET_SEC;
        }
        public float ThisFrameSec
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _noteController.ThisFrameSec;
        }

        protected bool IsUseButtonRingForTouch
        {
            get; private set;
        }
        protected INoteController NoteController
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _noteController;
        }
        protected Material BreakMaterial
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _breakMaterial;
        }
        protected Material DefaultMaterial
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _defaultMaterial;
        }
        protected Material HoldShineMaterial
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _holdShineMaterial;
        }
        protected GameModInfo ModInfo
        {
            get;
            private set;
        }
        protected bool IsAutoplay
        {
            get => AutoplayMode != AutoplayModeOption.Disable;
        }
        protected bool IsJudged = false;
        protected JudgeGrade AutoplayGrade = JudgeGrade.Perfect;
        protected AutoplayModeOption AutoplayMode = AutoplayModeOption.Disable;
        /// <summary>
        /// The answer frame
        /// </summary>
        protected float JudgeTiming;
        protected float JudgeDiff = -1;
        protected Range<float> JudgableRange = new(float.MinValue, float.MinValue + 1, ContainsType.Closed);
        protected JudgeGrade JudgeResult = JudgeGrade.Miss;

        protected SensorArea SensorPos;

        protected ObjectCounter ObjectCounter;
        protected NoteManager NoteManager;
        protected NoteEffectManager EffectManager;
        protected NoteAudioManager AudioEffMana;
        protected GameSetting Settings;
        protected Random Randomizer;

        protected bool USERSETTING_SLIDE_SKIPPING = false;
        protected float USERSETTING_JUDGE_OFFSET_SEC = 0f;
        protected float USERSETTING_TOUCHPANEL_OFFSET_SEC = 0f;
        protected float USERSETTING_TAP_SCALE = 1;
        protected float USERSETTING_HOLD_SCALE = 1;
        protected float USERSETTING_TOUCH_SCALE = 1;
        protected float USERSETTING_SLIDE_SCALE = 1;
        protected bool USERSETTING_DISPLAY_HOLD_HEAD_JUDGE_RESULT = false;
        protected JudgeModeOption USERSETTING_SLIDE_JUDGE_MODE = JudgeModeOption.Modern;
        protected DJAutoPolicyOption USERSETTING_DJAUTO_POLICY = DJAutoPolicyOption.Strict;

        protected const float FRAME_LENGTH_SEC = MajEnv.FRAME_LENGTH_SEC;
        protected const float FRAME_LENGTH_MSEC = MajEnv.FRAME_LENGTH_MSEC;

        protected const float TAP_JUDGE_SEG_1ST_PERFECT_MSEC = 1 * FRAME_LENGTH_MSEC;
        protected const float TAP_JUDGE_SEG_2ND_PERFECT_MSEC = 2 * FRAME_LENGTH_MSEC;
        protected const float TAP_JUDGE_SEG_3RD_PERFECT_MSEC = 3 * FRAME_LENGTH_MSEC;
        protected const float TAP_JUDGE_SEG_1ST_GREAT_MSEC = 4 * FRAME_LENGTH_MSEC;
        protected const float TAP_JUDGE_SEG_2ND_GREAT_MSEC = 5 * FRAME_LENGTH_MSEC;
        protected const float TAP_JUDGE_SEG_3RD_GREAT_MSEC = 6 * FRAME_LENGTH_MSEC;
        protected const float TAP_JUDGE_GOOD_AREA_MSEC = 9 * FRAME_LENGTH_MSEC;

        protected const float HOLD_CLASSIC_END_JUDGE_PERFECT_FAST_AREA_MSEC = 9 * FRAME_LENGTH_MSEC;
        protected const float HOLD_CLASSIC_END_JUDGE_PERFECT_LATE_AREA_MSEC = 12 * FRAME_LENGTH_MSEC;
        
        protected const float TOUCH_JUDGE_SEG_1ST_PERFECT_MSEC = 9 * FRAME_LENGTH_MSEC;
        protected const float TOUCH_JUDGE_SEG_2ND_PERFECT_MSEC = 10.5f * FRAME_LENGTH_MSEC;
        protected const float TOUCH_JUDGE_SEG_3RD_PERFECT_MSEC = 12 * FRAME_LENGTH_MSEC;
        protected const float TOUCH_JUDGE_SEG_1ST_GREAT_MSEC = 13 * FRAME_LENGTH_MSEC;
        protected const float TOUCH_JUDGE_SEG_2ND_GREAT_MSEC = 14 * FRAME_LENGTH_MSEC;
        protected const float TOUCH_JUDGE_SEG_3RD_GREAT_MSEC = 15 * FRAME_LENGTH_MSEC;
        protected const float TOUCH_JUDGE_GOOD_AREA_MSEC = 18 * FRAME_LENGTH_MSEC;

        protected const float TOUCH_DISPLAY_OFFSET_SEC = 0 * FRAME_LENGTH_SEC;
        protected const float TOUCH_HOLD_DISPLAY_OFFSET_SEC = 0 * FRAME_LENGTH_SEC;

        protected const float HOLD_HEAD_IGNORE_LENGTH_SEC = 6 * FRAME_LENGTH_SEC;
        protected const float HOLD_TAIL_IGNORE_LENGTH_SEC = 12 * FRAME_LENGTH_SEC;
        protected const float TOUCH_HOLD_HEAD_IGNORE_LENGTH_SEC = 15 * FRAME_LENGTH_SEC;
        protected const float TOUCH_HOLD_TAIL_IGNORE_LENGTH_SEC = 12 * FRAME_LENGTH_SEC;
        protected const float DELUXE_HOLD_RELEASE_IGNORE_TIME_SEC = 2 * FRAME_LENGTH_SEC;
        protected const float CLASSIC_HOLD_ALLOW_OVER_LENGTH_SEC = 20 * FRAME_LENGTH_SEC;
        protected override void Awake()
        {
            base.Awake();

            var gameInfo = Majdata<GameInfo>.Instance;
            Settings = MajEnv.Settings;
            Randomizer = new();
            ObjectCounter = Majdata<ObjectCounter>.Instance!;
            NoteManager = Majdata<NoteManager>.Instance!;
            EffectManager = Majdata<NoteEffectManager>.Instance!;
            AudioEffMana = Majdata<NoteAudioManager>.Instance!;

            USERSETTING_SLIDE_SKIPPING = gameInfo?.ChartSettings.SlideSkipping ?? Settings.Game.SlideSkipping;
            USERSETTING_JUDGE_OFFSET_SEC = ((MajEnv.Settings?.Judge.JudgeOffset ?? 0) + (MajEnv.Settings?.Debug.DisplayOffset ?? 0)) * ((MajEnv.Settings?.Debug.OffsetUnit ?? OffsetUnitOption.Second) == OffsetUnitOption.Second ? 1 : FRAME_LENGTH_SEC);
            USERSETTING_TOUCHPANEL_OFFSET_SEC = (MajEnv.Settings?.Judge.TouchPanelOffset ?? 0) * ((MajEnv.Settings?.Debug.OffsetUnit ?? OffsetUnitOption.Second) == OffsetUnitOption.Second ? 1 : FRAME_LENGTH_SEC);
            USERSETTING_TAP_SCALE = MajEnv.Settings?.Display.TapScale ?? 1;
            USERSETTING_HOLD_SCALE = MajEnv.Settings?.Display.HoldScale ?? 1;
            USERSETTING_TOUCH_SCALE = MajEnv.Settings?.Display.TouchScale ?? 1;
            USERSETTING_SLIDE_SCALE = MajEnv.Settings?.Display.SlideScale ?? 1;
            USERSETTING_DISPLAY_HOLD_HEAD_JUDGE_RESULT = MajEnv.Settings?.Display.DisplayHoldHeadJudgeResult ?? false;
            USERSETTING_SLIDE_JUDGE_MODE = MajEnv.Settings?.Judge.Mode ?? JudgeModeOption.Modern;
            USERSETTING_DJAUTO_POLICY = MajEnv.Settings?.Debug.DJAutoPolicy ?? DJAutoPolicyOption.Strict;

            _noteController = Majdata<INoteController>.Instance!;
            ModInfo = _noteController.ModInfo;
            _breakMaterial = _noteController.BreakMaterial;
            _defaultMaterial = _noteController.DefaultMaterial;
            _holdShineMaterial = _noteController.HoldShineMaterial;
            AutoplayMode = ModInfo.AutoPlay;
            AutoplayGrade = _noteController.AutoplayGrade;
#if UNITY_ANDROID || UNITY_IOS
            IsUseButtonRingForTouch = false;
#else
            IsUseButtonRingForTouch = ModInfo.ButtonRingForTouch;
#endif
        }
        void OnDestroy()
        {
            Active = false;
        }
        protected abstract void LoadSkin();
        protected abstract void PlaySFX();
        protected abstract void PlayJudgeSFX(in NoteJudgeResult judgeResult);
        protected virtual void Judge(float currentSec)
        {
            if (IsJudged)
                return;

            var diffSec = currentSec - JudgeTimingWithOffset;
            var isFast = diffSec < 0;
            JudgeDiff = diffSec * 1000;
            var diffMSec = MathF.Abs(diffSec * 1000);
            var result = diffMSec switch
            {
                <= TAP_JUDGE_SEG_1ST_PERFECT_MSEC => JudgeGrade.Perfect,
                <= TAP_JUDGE_SEG_2ND_PERFECT_MSEC => isFast ? JudgeGrade.FastPerfect2nd : JudgeGrade.LatePerfect2nd,
                <= TAP_JUDGE_SEG_3RD_PERFECT_MSEC => isFast ? JudgeGrade.FastPerfect3rd : JudgeGrade.LatePerfect3rd,
                <= TAP_JUDGE_SEG_1ST_GREAT_MSEC => isFast ? JudgeGrade.FastGreat : JudgeGrade.LateGreat,
                <= TAP_JUDGE_SEG_2ND_GREAT_MSEC => isFast ? JudgeGrade.FastGreat2nd : JudgeGrade.LateGreat2nd,
                <= TAP_JUDGE_SEG_3RD_GREAT_MSEC => isFast ? JudgeGrade.FastGreat3rd : JudgeGrade.LateGreat3rd,
                _ => isFast ? JudgeGrade.FastGood : JudgeGrade.LateGood
            };

            if (IsEX)
            {
                result = JudgeGrade.Perfect;
            }

            ConvertJudgeGrade(ref result);
            JudgeResult = result;
            IsJudged = true;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected virtual void Autoplay()
        {
            if (IsJudged || !IsAutoplay)
                return;
            if (GetTimeSpanToArriveTiming() >= -0.016667f)
            {
                var autoplayGrade = NoteController.AutoplayGrade;
                if (((int)autoplayGrade).InRange(0, 14))
                {
                    JudgeResult = autoplayGrade;
                }
                else
                {
                    JudgeResult = (JudgeGrade)Randomizer.Next(0, 15);
                }
                ConvertJudgeGrade(ref JudgeResult);
                IsJudged = true;
                JudgeDiff = JudgeResult switch
                {
                    < JudgeGrade.Perfect => 1,
                    > JudgeGrade.Perfect => -1,
                    _ => 0
                };
            }
        }

        /// <summary>
        /// Gets the time offset from the current moment to the judgment line.
        /// </summary>
        /// <returns>
        /// If the current moment is behind the judgment line, the result is a positive number.
        /// <para>If the current moment is ahead of the judgment line, the result is a negative number.</para>
        /// </returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected virtual float GetTimeSpanToArriveTiming() => ThisFrameSec - Timing;
        // Display-only time. Never use this in Autoplay, DJAuto, judge or lifetime checks.
        protected float GetAlphaVisualTime(float target) =>
            NoteTimeline.VisualTime(ThisFrameSec, target, AlphaNoteType, AlphaStateEach, IsBreak, IsMine, Timing);
        protected bool HasAlphaSV =>
            (NoteTimeline.HasSVFor(AlphaNoteType, AlphaStateEach, IsBreak, IsMine) || NoteTimeline.HasMotion(AlphaNoteType, Timing, AlphaStateEach, IsBreak, IsMine));
        protected float AlphaSpawnRadius => NoteTimeline.AnimatedRadius(true, Timing, ThisFrameSec, AlphaNoteType, AlphaStateEach, IsBreak, IsMine);
        protected float AlphaDestroyRadius => NoteTimeline.AnimatedRadius(false, Timing, ThisFrameSec, AlphaNoteType, AlphaStateEach, IsBreak, IsMine);
        protected float AlphaBounceDuration => NoteTimeline.BounceDuration(AlphaNoteType, Timing, AlphaStateEach, IsBreak, IsMine);
        protected bool HasAlphaRingRendering => HasAlphaSV || AlphaBounceDuration > 0 ||
            NoteTimeline.HasHSFor(AlphaNoteType, Timing, AlphaStateEach, IsBreak, IsMine) ||
            NoteTimeline.SpawnMode(AlphaNoteType, Timing, AlphaStateEach, IsBreak, IsMine) == MajdataCore.SpawnVisualMode.Once ||
            NoteTimeline.HasRingEffects(AlphaNoteType, Timing, AlphaStateEach, IsBreak, IsMine);
        MajdataCore.SpawnCrossingMemo _alphaHeadSpawnMemo, _alphaTailSpawnMemo;
        MajdataPlay.Alpha.AlphaVisualTimeline? _alphaBounceTimeline;
        float _alphaBounceTiming = float.NaN, _alphaBounceSpeed;
        double _alphaBounceStart;
        protected float AlphaBounceHSpeed => Speed / (Math.Abs(Majdata<NoteLoader>.Instance?.NoteSpeed ?? Speed) > .0001f ? Math.Abs(Majdata<NoteLoader>.Instance?.NoteSpeed ?? Speed) : 1);
        protected double AlphaBounceStart
        {
            get
            {
                var timeline = NoteTimeline;
                if (_alphaBounceTimeline != timeline || _alphaBounceTiming != Timing || _alphaBounceSpeed != Speed)
                {
                    _alphaBounceTimeline = timeline; _alphaBounceTiming = Timing; _alphaBounceSpeed = Speed;
                    _alphaBounceStart = timeline.BounceStart(Timing, AlphaBounceDuration, AlphaBounceHSpeed, AlphaNoteType, AlphaStateEach, IsBreak, IsMine);
                }
                return _alphaBounceStart;
            }
        }
        protected bool IsAlphaBounceActive => AlphaBounceDuration > 0 && ThisFrameSec >= AlphaBounceStart && ThisFrameSec < Timing &&
            (NoteTimeline.SpawnMode(AlphaNoteType, Timing, AlphaStateEach, IsBreak, IsMine) == MajdataCore.SpawnVisualMode.Once ||
             NoteTimeline.BounceProgress(ThisFrameSec, Timing, AlphaBounceDuration, AlphaBounceHSpeed, AlphaNoteType, AlphaStateEach, IsBreak, IsMine) >= -(float)MajdataCore.AlphaVisualTiming.Epsilon);
        protected bool IsBeforeAlphaBounce => AlphaBounceDuration > 0 && ThisFrameSec < Timing && !IsAlphaBounceActive;
        protected float GetAlphaRingDistance(float target)
        {
            var timeline = NoteTimeline;
            if (target == Timing && IsAlphaBounceActive)
                return timeline.BounceRadius(timeline.BounceProgress(ThisFrameSec, Timing, AlphaBounceDuration, AlphaBounceHSpeed, AlphaNoteType, AlphaStateEach, IsBreak, IsMine),
                    timeline.BounceDirection(Timing, AlphaNoteType, AlphaStateEach, IsBreak, IsMine, AlphaBounceHSpeed), AlphaSpawnRadius, AlphaDestroyRadius);
            return MajdataPlay.Alpha.AlphaRingGeometry.Distance(GetAlphaVisualTime(target), Speed, AlphaSpawnRadius, AlphaDestroyRadius);
        }
        protected float AlphaRingScale(float distance)
        {
            if (IsBeforeAlphaBounce) return 0;
            if (IsAlphaBounceActive) return 1;
            var timeline = NoteTimeline;
            if (timeline.SpawnMode(AlphaNoteType, Timing, AlphaStateEach, IsBreak, IsMine) == MajdataCore.SpawnVisualMode.Once &&
                timeline.CrossedSpawn(ref _alphaHeadSpawnMemo, ThisFrameSec, Timing, AlphaNoteType, AlphaStateEach, IsBreak, IsMine, Speed, AlphaSpawnRadius, AlphaDestroyRadius)) return 1;
            return Mathf.Clamp01(MajdataCore.AlphaVisualTiming.GetSpawnScale(distance, AlphaSpawnRadius, AlphaDestroyRadius));
        }
        protected float AlphaPresentationRadius(float target, float distance)
        {
            if (IsAlphaBounceActive) return distance;
            var timeline = NoteTimeline;
            var past = MajdataCore.AlphaVisualTiming.GetPathPosition(distance, AlphaSpawnRadius, AlphaDestroyRadius) >= 0;
            if (!past && timeline.SpawnMode(AlphaNoteType, Timing, AlphaStateEach, IsBreak, IsMine) == MajdataCore.SpawnVisualMode.Once)
                past = timeline.CrossedSpawn(ref (target == Timing ? ref _alphaHeadSpawnMemo : ref _alphaTailSpawnMemo), ThisFrameSec, target, AlphaNoteType, AlphaStateEach, IsBreak, IsMine, Speed, AlphaSpawnRadius, AlphaDestroyRadius, Timing);
            return past ? distance : AlphaSpawnRadius;
        }
        internal float VisualSpawnRadius => AlphaSpawnRadius;
        internal bool HasVisualRingMotion => HasAlphaRingRendering;
        internal float VisualPresentationRadius => AlphaPresentationRadius(Timing, GetAlphaRingDistance(Timing));
        internal float VisualPresentationScale => AlphaRingScale(GetAlphaRingDistance(Timing));
        string _alphaExpression = "";
        internal bool? AlphaEachInStream { get; set; }
        protected bool AlphaStateEach => AlphaEachInStream ?? IsEach;
        internal MajdataPlay.Alpha.AlphaVisualTimeline AlphaTimeline { get; set; }
        protected MajdataPlay.Alpha.AlphaVisualTimeline NoteTimeline => AlphaTimeline ?? MajdataPlay.Alpha.AlphaSimaiParser.Current;
        internal string AlphaExpression { get => _alphaExpression; set { RestoreAlphaVisualScales(); _alphaExpression = value; _alphaAppliedSkinExpression = null; _alphaBounceTimeline = null; _alphaHeadSpawnMemo = default; _alphaTailSpawnMemo = default; } }
        string _alphaAppliedSkinExpression;
        internal float VisualDestroyRadius => AlphaDestroyRadius;
        protected virtual string AlphaNoteType => this is TapDrop tap ? (tap.IsStar ? "star" : "tap")
            : this is HoldDrop ? "hold" : this is TouchHoldDrop ? "touchhold"
            : this is TouchDrop ? "touch" : this is SlideBase ? "slide" : "each";
        protected virtual System.Collections.Generic.IEnumerable<(SpriteRenderer Renderer, string Type)> GetAlphaRenderers()
        {
            foreach (var renderer in GetComponentsInChildren<SpriteRenderer>(true))
                yield return (renderer, "");
        }
        (SpriteRenderer Renderer, string Type)[] _alphaRenderers;
        MaterialPropertyBlock _alphaProperties;
        bool _alphaWasModified;
        readonly System.Collections.Generic.Dictionary<Transform, Vector3> _alphaUnscaledTransforms = new();
        readonly System.Collections.Generic.Dictionary<Transform, Quaternion> _alphaUnrotatedTransforms = new();
        protected virtual Transform? UnscaledAlphaPoint => null;
        internal void RestoreAlphaVisualScales()
        {
            foreach (var entry in _alphaUnscaledTransforms)
                if (entry.Key) entry.Key.localScale = entry.Value;
            _alphaUnscaledTransforms.Clear();
            foreach (var entry in _alphaUnrotatedTransforms)
                if (entry.Key) entry.Key.localRotation = entry.Value;
            _alphaUnrotatedTransforms.Clear();
        }
        void ApplyAlphaAngle(Transform target, float degrees)
        {
            if (degrees == 0) return;
            _alphaUnrotatedTransforms[target] = target.localRotation;
            target.localRotation *= Quaternion.Euler(0, 0, degrees);
        }
        void ApplyAlphaScale(Transform target, float x, float y)
        {
            if (x == 1 && y == 1) return;
            _alphaUnscaledTransforms[target] = target.localScale;
            target.localScale = Vector3.Scale(target.localScale, new Vector3(x, y, 1));
        }
        internal void ApplyAlphaVisuals()
        {
            RestoreAlphaVisualScales();
            if (IsEnded || !IsInited) return;
            if (_alphaAppliedSkinExpression != AlphaExpression)
            {
                _alphaAppliedSkinExpression = AlphaExpression;
                ApplyInheritedImages();
            }
            var timeline = NoteTimeline;
            if (!timeline.HasSkinEffects && !_alphaWasModified) return;
            _alphaWasModified = timeline.HasSkinEffects;
            if (_alphaRenderers == null)
                _alphaRenderers = new System.Collections.Generic.List<(SpriteRenderer, string)>(GetAlphaRenderers()).ToArray();
            if (this is not SlideBase)
            {
                var size = timeline.Size(AlphaNoteType, Timing, ThisFrameSec, AlphaStateEach, IsBreak, IsMine);
                // Scale the complete note, including Hold tail/fan positions and
                // TouchHold mask. Shader-only vertex scale leaves those behind.
                ApplyAlphaScale(Transform, size.X, size.Y);
                ApplyAlphaAngle(Transform, timeline.Angle(AlphaNoteType, Timing, ThisFrameSec, AlphaStateEach, IsBreak, IsMine));
                if (UnscaledAlphaPoint is { } point && point)
                    ApplyAlphaScale(point, size.X == 0 ? 1 : 1 / Mathf.Abs(size.X), size.Y == 0 ? 1 : 1 / Mathf.Abs(size.Y));
            }
            string sampledType = null;
            uint? color = null;
            float alpha = 1, angle = 0;
            (float X, float Y) arrowSize = (1, 1);
            foreach (var entry in _alphaRenderers)
            {
                var renderer = entry.Renderer;
                if (!renderer) continue;
                var type = entry.Type.Length == 0 ? AlphaNoteType : entry.Type;
                if (type != sampledType)
                {
                    sampledType = type;
                    color = timeline.Color(type, Timing, ThisFrameSec, AlphaStateEach, IsBreak, IsMine);
                    alpha = timeline.Alpha(type, Timing, ThisFrameSec, AlphaStateEach, IsBreak, IsMine);
                    if (this is SlideBase)
                    {
                        arrowSize = timeline.Size(type, Timing, ThisFrameSec, AlphaStateEach, IsBreak, IsMine);
                        angle = timeline.Angle(type, Timing, ThisFrameSec, AlphaStateEach, IsBreak, IsMine);
                    }
                }
                // View scales each arrow/star around its own local pivot. Never
                // scale the path root or its positions/judgment geometry.
                if (this is SlideBase)
                {
                    ApplyAlphaScale(renderer.transform, arrowSize.X, arrowSize.Y);
                    ApplyAlphaAngle(renderer.transform, angle);
                }
                MajdataPlay.Alpha.AlphaNoteMaterial.Apply(renderer, ref _alphaProperties, color, alpha, mine: IsMine);
            }
        }
        void ApplyInheritedImages()
        {
            var expression = MajdataPlay.Alpha.AlphaNoteDecoration.Read(AlphaExpression);
            if (expression == null) return;
            if (expression.position.HasCustomRadius && (this is TouchDrop || this is TouchHoldDrop))
                Transform.localPosition = MajdataPlay.Alpha.AlphaPathMetadata.Position(expression.position.area, expression.position.position).normalized * expression.position.radius;
            var path = this is SlideBase ? expression.path?.segments[expression.path.segments.Count - 1].end.skin : expression.position.skin;
            if (string.IsNullOrEmpty(path) || this is HoldDrop) return;
            var targets = new System.Collections.Generic.List<SpriteRenderer>();
            if (this is TouchDrop touch) targets.AddRange(touch.InheritedSkinRenderers);
            else if (this is TouchHoldDrop touchHold) targets.AddRange(touchHold.InheritedSkinRenderers);
            else foreach (var entry in GetAlphaRenderers())
            {
                if (this is SlideBase && entry.Type != "slide") continue;
                if (entry.Renderer) targets.Add(entry.Renderer);
                if (this is TapDrop || this is HoldDrop) break;
                if ((this is TouchDrop || this is TouchHoldDrop) && targets.Count == 4) break;
            }
            foreach (var renderer in targets)
            {
                var width = this is TapDrop tap
                    ? tap.IsStar && !tap.IsDouble ? MajdataPlay.Alpha.NoteSkinLibrary.ViewStarWidth : MajdataPlay.Alpha.NoteSkinLibrary.ViewTapWidth
                    : 0f;
                var skin = MajdataPlay.Alpha.NoteSkinLibrary.TryCreateSprite(path, renderer.sprite, out var reason, width);
                if (skin)
                {
                    renderer.sprite = skin;
                    // View keeps image Tap heads upright, including D heads. A
                    // custom Star starts upright, then retains native star spin.
                    if (this is TapDrop) Transform.localRotation = Quaternion.identity;
                }
                else Debug.LogWarning("继承图片已跳过：" + path + "；" + reason);
            }
        }
        protected Sprite InheritedHeadSprite(Sprite model)
        {
            var path = MajdataPlay.Alpha.AlphaNoteDecoration.Read(AlphaExpression)?.position.skin;
            if (string.IsNullOrEmpty(path)) return model;
            return MajdataPlay.Alpha.NoteSkinLibrary.TryCreateSprite(path, model, out _) ?? model;
        }
        /// <summary>
        /// Gets the time offset from the current moment to the answer frame.
        /// </summary>
        /// <returns>
        /// If the current moment is behind the answer frame, the result is a positive number.
        /// <para>If the current moment is ahead of the answer frame, the result is a negative number.</para>
        /// </returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected float GetTimeSpanToJudgeTiming() => ThisFrameSec - JudgeTimingWithOffset;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected float GetTimeSpanToJudgeTiming(float baseTiming) => baseTiming - JudgeTimingWithOffset;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected void ConvertJudgeGrade(ref JudgeGrade grade)
        {
            var judgeStyle = ModInfo.JudgeStyle;
            switch (judgeStyle)
            {
                case JudgeStyleOption.MAJI:
                    ConvertToMAJI(ref grade);
                    break;
                case JudgeStyleOption.GACHI:
                    ConvertToGACHI(ref grade);
                    break;
                case JudgeStyleOption.GORI:
                    ConvertToGORI(ref grade);
                    break;
                case JudgeStyleOption.DEFAULT:
                default:
                    return;
            }
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void ConvertToMAJI(ref JudgeGrade judgeType)
        {
            var isFast = (int)judgeType > 7;
            switch (judgeType)
            {
                case JudgeGrade.LateGreat:
                case JudgeGrade.LateGreat2nd:
                case JudgeGrade.LateGreat3rd:
                case JudgeGrade.FastGreat:
                case JudgeGrade.FastGreat2nd:
                case JudgeGrade.FastGreat3rd:
                    if (isFast)
                        judgeType = JudgeGrade.FastGood;
                    else
                        judgeType = JudgeGrade.LateGood;
                    break;
                case JudgeGrade.LatePerfect3rd:
                case JudgeGrade.FastPerfect3rd:
                    if (isFast)
                        judgeType = JudgeGrade.FastGreat;
                    else
                        judgeType = JudgeGrade.LateGreat;
                    break;
                default:
                    if (judgeType > JudgeGrade.Perfect)
                        judgeType = JudgeGrade.TooFast;
                    else if (judgeType < JudgeGrade.Perfect)
                        judgeType = JudgeGrade.Miss;
                    break;
                case JudgeGrade.LatePerfect2nd:
                case JudgeGrade.FastPerfect2nd:
                case JudgeGrade.Perfect:
                case JudgeGrade.Miss:
                case JudgeGrade.TooFast:
                    return;
            }
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void ConvertToGACHI(ref JudgeGrade judgeType)
        {
            var isFast = (int)judgeType > 7;
            switch (judgeType)
            {
                case JudgeGrade.LatePerfect3rd:
                case JudgeGrade.FastPerfect3rd:
                    if (isFast)
                        judgeType = JudgeGrade.FastGood;
                    else
                        judgeType = JudgeGrade.LateGood;
                    break;
                case JudgeGrade.LatePerfect2nd:
                case JudgeGrade.FastPerfect2nd:
                    if (isFast)
                        judgeType = JudgeGrade.FastGreat;
                    else
                        judgeType = JudgeGrade.LateGreat;
                    break;
                default:
                    if (judgeType > JudgeGrade.Perfect)
                        judgeType = JudgeGrade.TooFast;
                    else if (judgeType < JudgeGrade.Perfect)
                        judgeType = JudgeGrade.Miss;
                    break;
                case JudgeGrade.Perfect:
                case JudgeGrade.Miss:
                    return;
            }
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void ConvertToGORI(ref JudgeGrade judgeType)
        {
            switch (judgeType)
            {
                case JudgeGrade.Perfect:
                case JudgeGrade.Miss:
                    return;
                default:
                    if (judgeType > JudgeGrade.Perfect)
                        judgeType = JudgeGrade.TooFast;
                    else if (judgeType < JudgeGrade.Perfect)
                        judgeType = JudgeGrade.Miss;
                    break;
            }
        }
        [ReadOnlyField, SerializeField]
        int _startPos = 1;
        [ReadOnlyField, SerializeField]
        float _timing;
        [ReadOnlyField, SerializeField]
        float _speed = 7;
        [ReadOnlyField, SerializeField]
        int _sortOrder;
        [ReadOnlyField, SerializeField]
        bool _isEach = false;
        [ReadOnlyField, SerializeField]
        bool _isBreak = false;
        [ReadOnlyField, SerializeField]
        bool _isEX = false;
        [ReadOnlyField, SerializeField]
        bool _isMine = false;        
        [ReadOnlyField, SerializeField]
        NoteStatus _state = NoteStatus.Start;

        Material _breakMaterial;
        Material _defaultMaterial;
        Material _holdShineMaterial;

        INoteController _noteController;
    }
}
