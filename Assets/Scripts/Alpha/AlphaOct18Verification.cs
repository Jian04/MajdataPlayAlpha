#if UNITY_EDITOR
using System;
using UnityEngine;
namespace MajdataPlay.Alpha
{
    public static class AlphaOct18Verification
    {
        public static void Build()
        {
            var args=Environment.GetCommandLineArgs();
            var at=Array.IndexOf(args,"-alphaFontFixture");
            if(at>=0)
            {
                var font=RuntimeFontFiles.Load(args[at+1]);
                if(!font.TryAddCharacters("RGB字幕",out var missing) || missing.Length>0)
                    throw new Exception("File font failed glyph loading: "+missing);
                Debug.Log("PLAY FILE FONT PASS: RGB and CJK glyphs loaded through TEXT font resolver");
            }
            AlphaOct13Verification.Build();
        }
    }
}
#endif
