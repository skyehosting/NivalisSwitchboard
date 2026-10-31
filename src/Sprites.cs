using System;
using UnityEngine;

namespace NivalisSwitchboard;

/// <summary>Procedurally drawn, nine-sliced sprites in the look of People of Nivalis's windows (no game assets are changed).</summary>
internal static class Sprites
{
    private static Sprite? _bubble, _pill, _field, _cell, _knob, _keyboard, _outline;

    public static Sprite Bubble => _bubble ??= Rounded(64, 16, 3, new Color32(36, 33, 30, 238), new Color32(196, 160, 98, 255));
    public static Sprite Pill => _pill ??= Rounded(40, 18, 2.5f, new Color32(26, 44, 42, 255), new Color32(196, 160, 98, 255));
    public static Sprite Field => _field ??= Rounded(40, 9, 1.5f, new Color32(22, 20, 18, 246), new Color32(118, 97, 62, 255));
    /// <summary>A small white rounded square (a bar's fill, a divider), tinted where it's shown.</summary>
    public static Sprite Cell => _cell ??= Rounded(16, 3, 0, new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255));
    /// <summary>A key's bold border, white with nothing inside (the on-screen keyboard's chosen key), tinted where it's shown.</summary>
    public static Sprite KeyEdge => _keyEdge ??= Rounded(40, 9, 3f, new Color32(255, 255, 255, 0), new Color32(255, 255, 255, 255));
    private static Sprite? _keyEdge;
    /// <summary>A white round end (switch tracks and knobs), tinted where it's shown.</summary>
    public static Sprite Knob => _knob ??= Rounded(40, 19.5f, 0, new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255));
    /// <summary>A white rounded outline with nothing inside (chips, step rings, a button's edge), tinted where it's shown.</summary>
    public static Sprite Outline => _outline ??= Rounded(40, 19.5f, 2f, new Color32(255, 255, 255, 0), new Color32(255, 255, 255, 255));
    /// <summary>The keyboard icon for the on-screen keyboard button, white: tinted gold.</summary>
    public static Sprite KeyboardIcon => _keyboard ??= MakeIcon(64, KeyboardShape);

    private static Texture2D NewTex(int w, int h) => new(w, h, TextureFormat.RGBA32, false)
    {
        filterMode = FilterMode.Bilinear,
        wrapMode = TextureWrapMode.Clamp,
        hideFlags = HideFlags.HideAndDontSave,
    };

    private static Color32 Mix(Color32 fill, Color32 edge, float edgeAmount, float alpha)
    {
        var c = Color32.Lerp(fill, edge, Mathf.Clamp01(edgeAmount));
        c.a = (byte)Mathf.RoundToInt(c.a * Mathf.Clamp01(alpha));
        return c;
    }

    /// <summary>Rounded rectangle with a border, anti-aliased from a signed distance field.</summary>
    private static Sprite Rounded(int size, float radius, float border, Color32 fill, Color32 edge)
    {
        var tex = NewTex(size, size);
        var px = new Color32[size * size];
        float half = size / 2f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float qx = Mathf.Abs(x + 0.5f - half) - (half - radius);
                float qy = Mathf.Abs(y + 0.5f - half) - (half - radius);
                float outside = new Vector2(Mathf.Max(qx, 0), Mathf.Max(qy, 0)).magnitude + Mathf.Min(Mathf.Max(qx, qy), 0) - radius;
                px[y * size + x] = Mix(fill, edge, outside + border + 0.5f, 0.5f - outside);
            }
        tex.SetPixels32(px);
        tex.Apply(false, true);
        float slice = radius + border + 2;
        var s = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(slice, slice, slice, slice));
        s.hideFlags = HideFlags.HideAndDontSave;
        return s;
    }

    /// <summary>An icon from a distance function (negative inside), anti-aliased, in white.</summary>
    private static Sprite MakeIcon(int size, Func<Vector2, float, float> shape)
    {
        var tex = NewTex(size, size);
        var px = new Color32[size * size];
        float u = size / 64f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = shape(new Vector2(x + 0.5f, y + 0.5f) / u, u) * u;
                px[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(255 * Mathf.Clamp01(0.5f - d)));
            }
        tex.SetPixels32(px);
        tex.Apply(false, true);
        var s = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
        s.hideFlags = HideFlags.HideAndDontSave;
        return s;
    }

    private static float Box(Vector2 p, Vector2 centre, Vector2 half, float r)
    {
        var q = new Vector2(Mathf.Abs(p.x - centre.x), Mathf.Abs(p.y - centre.y)) - half + new Vector2(r, r);
        return new Vector2(Mathf.Max(q.x, 0), Mathf.Max(q.y, 0)).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0) - r;
    }

    /// <summary>A keyboard: a rounded outline, two rows of keys and a space bar (64-unit space).</summary>
    private static float KeyboardShape(Vector2 p, float u)
    {
        float outline = Mathf.Abs(Box(p, new Vector2(32, 32), new Vector2(25, 15), 5)) - 2.2f;
        float keys = 999f;
        for (int i = 0; i < 5; i++)
        {
            keys = Mathf.Min(keys, Box(p, new Vector2(16 + i * 8, 39), new Vector2(2.4f, 2.4f), 0.8f));
            keys = Mathf.Min(keys, Box(p, new Vector2(16 + i * 8, 31), new Vector2(2.4f, 2.4f), 0.8f));
        }
        float space = Box(p, new Vector2(32, 23), new Vector2(11, 2.2f), 1.2f);
        return Mathf.Min(outline, Mathf.Min(keys, space));
    }
}
