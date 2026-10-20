namespace MajdataPlay.Alpha
{
internal sealed class ScreenEffectEvent
{
    public bool rotatePV = false;
    public bool gradient;
    public double time;
    public string effect;
    public float duration;
    public float intensity;
    // Negative attack selects the legacy envelope.
    public float attack = -1f;
    public float holdTime;
    public float release;
    public float paramA;
    public float paramB;
    public bool hasDirection;
    public string color;
    public bool stateful;
    public bool enabled;
    public float transition;
    public ScreenEffectEvent ForPV()
    {
        var result = (ScreenEffectEvent)MemberwiseClone();
        if (!rotatePV) result.intensity = 0;
        return result;
    }
    public ScreenEffectEvent Transform(double scale, double offset)
    {
        var result=(ScreenEffectEvent)MemberwiseClone();
        result.time=time*scale+offset;
        result.duration*= (float)scale; result.transition*= (float)scale;
        if(result.attack>=0) result.attack*= (float)scale;
        result.holdTime*= (float)scale; result.release*= (float)scale;
        return result;
    }
}
}
