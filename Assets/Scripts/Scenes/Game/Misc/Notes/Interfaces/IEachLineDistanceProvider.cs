using System;
using System.Collections.Generic;
using System.Text;

namespace MajdataPlay.Game.Notes
{
    internal interface IEachLineDistanceProvider
    {
        bool HasAlphaMotion { get; }
        float PresentationRadius { get; }
        float PresentationScale { get; }
        float Distance { get; }
        float VisualSpawnRadius { get; }
        float VisualDestroyRadius { get; }
        bool IsAnyNoteEnded { get; }
    }
}
