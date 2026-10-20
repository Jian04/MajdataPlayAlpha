using MajdataPlay.Scenes.Game;
using MajdataPlay.IO;
namespace MajdataPlay.Alpha
{
    internal static class AlphaNoiseInputGate
    {
        internal static bool IsBlocked(SensorArea sensor)
        {
            var controller=Majdata<INoteController>.Instance;
            return controller!=null && controller.IsStart && AlphaSimaiParser.Current.Noise.IsBlocked((int)sensor,controller.ThisFrameSec);
        }
    }
}
