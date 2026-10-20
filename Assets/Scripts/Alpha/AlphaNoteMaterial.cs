using System.Collections.Generic;
using UnityEngine;

namespace MajdataPlay.Alpha
{
    internal static class AlphaNoteMaterial
    {
        static Material _fallback;
        static readonly int SizeId = Shader.PropertyToID("_NoteSize");
        static readonly int ColorId = Shader.PropertyToID("_NoteColor");
        static readonly int OpacityId = Shader.PropertyToID("_NoteAlpha");
        static readonly int TintCoverageId = Shader.PropertyToID("_TintCoverage");
        static readonly Dictionary<SpriteRenderer, Material> Originals = new();
        static readonly Dictionary<SpriteRenderer, (uint? Color, float Alpha, float X, float Y, bool MineTint, Material Material)> Applied = new();
        internal static void Apply(SpriteRenderer renderer, ref MaterialPropertyBlock properties, uint? color, float alpha, float sizeX = 1, float sizeY = 1, bool mine = false)
        {
            if (!renderer.enabled) return;
            var mineTint = mine && color.HasValue;
            // These properties belong to Alpha. Native shine shaders animate
            // independently, so unchanged overrides need no native read/write.
            if (Applied.TryGetValue(renderer, out var previous) && previous.Color == color && previous.Alpha == alpha &&
                previous.X == sizeX && previous.Y == sizeY && previous.MineTint == mineTint && renderer.sharedMaterial == previous.Material)
                return;
            EnsureShader(renderer, color.HasValue || alpha != 1 || sizeX != 1 || sizeY != 1,
                mineTint);
            properties ??= new MaterialPropertyBlock();
            renderer.GetPropertyBlock(properties);
            properties.SetColor(ColorId, color.HasValue
                ? new Color(((color.Value >> 16) & 255) / 255f, ((color.Value >> 8) & 255) / 255f, (color.Value & 255) / 255f, 1)
                : new Color(1, 1, 1, 0));
            properties.SetFloat(OpacityId, alpha);
            properties.SetFloat(TintCoverageId, mine && color.HasValue ? 1f : 0f);
            properties.SetVector(SizeId, new Vector4(sizeX, sizeY, 1, 1));
            renderer.SetPropertyBlock(properties);
            Applied[renderer] = (color, alpha, sizeX, sizeY, mineTint, renderer.sharedMaterial);
        }
        internal static void EnsureShader(SpriteRenderer renderer, bool modified, bool mineTint = false)
        {
            // Native break/hold shaders retain their shine and gain the same display properties.
            if (renderer.sharedMaterial != _fallback && renderer.sharedMaterial &&
                renderer.sharedMaterial.HasProperty("_NoteAlpha") &&
                (!mineTint || renderer.sharedMaterial.HasProperty("_TintCoverage")))
            {
                Originals.Remove(renderer);
                return;
            }
            if (!modified)
            {
                if (Originals.TryGetValue(renderer, out var original))
                {
                    if (renderer.sharedMaterial == _fallback) renderer.sharedMaterial = original;
                    Originals.Remove(renderer);
                }
                return;
            }
            if (!_fallback) _fallback = Resources.Load<Material>("AlphaNote");
            if (!_fallback) return;
            if (renderer.sharedMaterial != _fallback) Originals[renderer] = renderer.sharedMaterial;
            renderer.sharedMaterial = _fallback;
        }
        internal static void Release(SpriteRenderer renderer)
        {
            if (ReferenceEquals(renderer, null)) return;
            Applied.Remove(renderer);
            if (renderer && Originals.TryGetValue(renderer, out var original) && renderer.sharedMaterial == _fallback)
                renderer.sharedMaterial = original;
            Originals.Remove(renderer);
        }
        internal static void Clear()
        {
            foreach (var pair in Originals)
                if (pair.Key && pair.Key.sharedMaterial == _fallback) pair.Key.sharedMaterial = pair.Value;
            Originals.Clear();
            Applied.Clear();
        }
    }
}
