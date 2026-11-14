using System.Collections.Generic;
using UnityEngine;

namespace Blasphemous.CoopLocal;

/// <summary>
/// Arte procedural pixel-art para el mod elemental: corazones de Mea Culpa (64x64) y calaveras
/// (96x96). Todo se genera en runtime con funciones de distancia y rampas de 5 tonos cuantizadas
/// (mismo lenguaje visual que el sprite-work del juego: contorno oscuro, 5 tonos, brillo de acento),
/// sin depender de archivos externos. Los resultados se cachean.
/// </summary>
internal static class ElementalArt
{
    private static readonly Dictionary<ElementSkill, Sprite> hearts = new Dictionary<ElementSkill, Sprite>();
    private static readonly Dictionary<ElementSkill, Sprite> skulls = new Dictionary<ElementSkill, Sprite>();

    // ---- Paletas (oscuro -> claro) ----
    private static Color[] HeartRamp(ElementSkill e)
    {
        switch (e)
        {
            case ElementSkill.Poison:
                return new[] { C(0.04f, 0.11f, 0.06f), C(0.09f, 0.25f, 0.11f), C(0.17f, 0.42f, 0.17f), C(0.32f, 0.66f, 0.27f), C(0.68f, 0.96f, 0.55f) };
            case ElementSkill.Electric:
                return new[] { C(0.03f, 0.06f, 0.15f), C(0.07f, 0.17f, 0.37f), C(0.13f, 0.37f, 0.70f), C(0.30f, 0.70f, 0.95f), C(0.86f, 0.98f, 1f) };
            case ElementSkill.Curse:
                return new[] { C(0.04f, 0.02f, 0.08f), C(0.12f, 0.05f, 0.21f), C(0.23f, 0.09f, 0.37f), C(0.40f, 0.18f, 0.58f), C(0.72f, 0.46f, 0.86f) };
            default: // Bleed
                return new[] { C(0.13f, 0.02f, 0.03f), C(0.31f, 0.04f, 0.07f), C(0.55f, 0.07f, 0.10f), C(0.80f, 0.14f, 0.16f), C(1f, 0.48f, 0.44f) };
        }
    }

    private static Color Accent(ElementSkill e)
    {
        switch (e)
        {
            case ElementSkill.Poison: return C(0.55f, 1f, 0.42f);
            case ElementSkill.Electric: return C(0.65f, 0.95f, 1f);
            case ElementSkill.Curse: return C(0.80f, 0.34f, 1f);
            default: return C(1f, 0.26f, 0.22f);
        }
    }

    private static Color[] BoneRamp(ElementSkill e)
    {
        if (e == ElementSkill.Electric)
            return new[] { C(0.08f, 0.12f, 0.20f), C(0.22f, 0.31f, 0.46f), C(0.48f, 0.61f, 0.77f), C(0.77f, 0.88f, 0.96f), C(0.97f, 1f, 1f) };
        if (e == ElementSkill.Curse)
            return new[] { C(0.06f, 0.04f, 0.10f), C(0.19f, 0.14f, 0.27f), C(0.37f, 0.29f, 0.47f), C(0.60f, 0.52f, 0.70f), C(0.86f, 0.80f, 0.93f) };
        if (e == ElementSkill.Poison)
            return new[] { C(0.07f, 0.12f, 0.07f), C(0.20f, 0.30f, 0.18f), C(0.44f, 0.58f, 0.36f), C(0.72f, 0.84f, 0.60f), C(0.94f, 1f, 0.84f) };
        return new[] { C(0.14f, 0.05f, 0.05f), C(0.33f, 0.15f, 0.13f), C(0.62f, 0.38f, 0.32f), C(0.88f, 0.70f, 0.60f), C(1f, 0.93f, 0.86f) };
    }

    internal static Color C(float r, float g, float b, float a = 1f) => new Color(r, g, b, a);

    internal static Sprite GetHeart(ElementSkill e)
    {
        if (!hearts.TryGetValue(e, out Sprite s) || s == null)
        {
            s = hearts[e] = BuildHeart(e);
        }
        return s;
    }

    internal static Sprite GetSkull(ElementSkill e)
    {
        if (!skulls.TryGetValue(e, out Sprite s) || s == null)
        {
            s = skulls[e] = BuildSkull(e);
        }
        return s;
    }

    // ------------------------------------------------------------------
    // Iconos del árbol: icono vanilla + contorno de color característico
    // ------------------------------------------------------------------
    /// <summary>
    /// Copia un sprite vanilla (aunque su textura no sea legible, vía RenderTexture) y le añade un contorno
    /// grueso del color del elemento (brillante interior, oscuro exterior) conservando el arte original.
    /// </summary>
    internal static Sprite OutlineVanillaIcon(Sprite src, Color outline, bool unlocked)
    {
        if (src == null || src.texture == null) return null;
        try
        {
            Texture2D tex = src.texture;
            Rect r = src.textureRect;
            int w = Mathf.RoundToInt(r.width), h = Mathf.RoundToInt(r.height);
            if (w <= 0 || h <= 0) return null;

            RenderTexture rt = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(tex, rt);
            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = rt;
            var read = new Texture2D(w, h, TextureFormat.RGBA32, false);
            read.ReadPixels(new Rect(r.x, r.y, w, h), 0, 0);
            read.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            Color[] icon = read.GetPixels();
            UnityEngine.Object.Destroy(read);

            const int pad = 3;
            int W = w + pad * 2, H = h + pad * 2;
            var b = new Buf(W, H);
            bool[] mask = new bool[W * H];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    Color c = icon[y * w + x];
                    if (c.a > 0.2f)
                    {
                        mask[(y + pad) * W + (x + pad)] = true;
                        b.Set(x + pad, y + pad, c);
                    }
                }

            Color bright = unlocked ? outline : outline * 0.55f;
            bright.a = 1f;
            Color dark = new Color(bright.r * 0.35f, bright.g * 0.35f, bright.b * 0.35f, 1f);
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    if (mask[y * W + x]) continue;
                    float best = 99f;
                    for (int dy = -pad; dy <= pad; dy++)
                        for (int dx = -pad; dx <= pad; dx++)
                        {
                            int nx = x + dx, ny = y + dy;
                            if (nx < 0 || ny < 0 || nx >= W || ny >= H || !mask[ny * W + nx]) continue;
                            float d = Mathf.Sqrt(dx * dx + dy * dy);
                            if (d < best) best = d;
                        }
                    if (best <= 1.5f) b.Set(x, y, bright);
                    else if (best <= 2.6f) b.Set(x, y, dark);
                }

            var sprite = new Sprite[1];
            var outTex = new Texture2D(W, H, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            outTex.SetPixels(b.P);
            outTex.Apply();
            sprite[0] = Sprite.Create(outTex, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f), src.pixelsPerUnit);
            return sprite[0];
        }
        catch (System.Exception ex)
        {
            DashParryDebugLog.Log("[ElementalArt] OutlineVanillaIcon falló: " + ex.Message);
            return null;
        }
    }

    // ------------------------------------------------------------------
    // Buffer
    // ------------------------------------------------------------------
    internal sealed class Buf
    {
        public readonly int W, H;
        public readonly Color[] P;
        public Buf(int w, int h) { W = w; H = h; P = new Color[w * h]; }
        public bool In(int x, int y) => x >= 0 && y >= 0 && x < W && y < H;
        public Color Get(int x, int y) => In(x, y) ? P[y * W + x] : Color.clear;
        public void Set(int x, int y, Color c) { if (In(x, y)) P[y * W + x] = c; }
        public bool Opaque(int x, int y) => In(x, y) && P[y * W + x].a > 0.5f;
        public Sprite ToSprite(float ppu)
        {
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            tex.SetPixels(P);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f), ppu);
        }
    }

    /// <summary>Duplica la resolución con el algoritmo EPX/Scale2x (bordes de pixel-art más limpios, sin difuminar).</summary>
    internal static Buf Scale2x(Buf src)
    {
        var dst = new Buf(src.W * 2, src.H * 2);
        bool Eq(Color a, Color b) { return Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b) + Mathf.Abs(a.a - b.a) < 0.02f; }
        for (int y = 0; y < src.H; y++)
            for (int x = 0; x < src.W; x++)
            {
                Color P = src.Get(x, y);
                Color A = src.Get(x, y + 1), B = src.Get(x + 1, y), Cc = src.Get(x - 1, y), D = src.Get(x, y - 1);
                Color p1 = P, p2 = P, p3 = P, p4 = P;   // p1 sup-izq, p2 sup-der, p3 inf-izq, p4 inf-der
                if (Eq(Cc, A) && !Eq(Cc, D) && !Eq(A, B)) p1 = A;
                if (Eq(A, B) && !Eq(A, Cc) && !Eq(B, D)) p2 = B;
                if (Eq(D, Cc) && !Eq(D, B) && !Eq(Cc, A)) p3 = Cc;
                if (Eq(B, D) && !Eq(B, A) && !Eq(D, Cc)) p4 = D;
                dst.Set(x * 2, y * 2 + 1, p1);
                dst.Set(x * 2 + 1, y * 2 + 1, p2);
                dst.Set(x * 2, y * 2, p3);
                dst.Set(x * 2 + 1, y * 2, p4);
            }
        return dst;
    }

    internal static float Hash(int x, int y, int seed)
    {
        unchecked
        {
            int h = x * 374761393 + y * 668265263 + seed * 1442695041;
            h = (h ^ (h >> 13)) * 1274126177;
            h ^= h >> 16;
            return (h & 0xFFFF) / 65535f;
        }
    }

    internal static float Noise(float x, float y, int seed)
    {
        int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
        float fx = x - x0, fy = y - y0;
        fx = fx * fx * (3f - 2f * fx);
        fy = fy * fy * (3f - 2f * fy);
        float a = Mathf.Lerp(Hash(x0, y0, seed), Hash(x0 + 1, y0, seed), fx);
        float b = Mathf.Lerp(Hash(x0, y0 + 1, seed), Hash(x0 + 1, y0 + 1, seed), fx);
        return Mathf.Lerp(a, b, fy);
    }

    internal static void Line(Buf b, int x0, int y0, int x1, int y1, Color c, System.Func<int, int, bool> allow)
    {
        int dx = Mathf.Abs(x1 - x0), dy = -Mathf.Abs(y1 - y0);
        int sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1;
        int err = dx + dy;
        int guard = 512;
        while (guard-- > 0)
        {
            if (b.In(x0, y0) && (allow == null || allow(x0, y0))) b.Set(x0, y0, c);
            if (x0 == x1 && y0 == y1) break;
            int e2 = 2 * err;
            if (e2 >= dy) { err += dy; x0 += sx; }
            if (e2 <= dx) { err += dx; y0 += sy; }
        }
    }

    internal static void Disc(Buf b, float cx, float cy, float r, Color c, System.Func<int, int, bool> allow = null)
    {
        int x0 = Mathf.FloorToInt(cx - r), x1 = Mathf.CeilToInt(cx + r);
        int y0 = Mathf.FloorToInt(cy - r), y1 = Mathf.CeilToInt(cy + r);
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                float dx = x + 0.5f - cx, dy = y + 0.5f - cy;
                if (dx * dx + dy * dy <= r * r && b.In(x, y) && (allow == null || allow(x, y))) b.Set(x, y, c);
            }
    }

    // Marca contorno oscuro y halo suave de acento alrededor de lo ya dibujado.
    internal static void OutlineAndGlow(Buf b, Color outline, Color glow, int glowRadius, float glowAlpha)
    {
        var src = (Color[])b.P.Clone();
        bool Op(int x, int y) => x >= 0 && y >= 0 && x < b.W && y < b.H && src[y * b.W + x].a > 0.5f;

        // Halo
        for (int y = 0; y < b.H; y++)
            for (int x = 0; x < b.W; x++)
            {
                if (src[y * b.W + x].a > 0.01f) continue;
                float best = 999f;
                for (int dy = -glowRadius; dy <= glowRadius; dy++)
                    for (int dx = -glowRadius; dx <= glowRadius; dx++)
                    {
                        if (!Op(x + dx, y + dy)) continue;
                        float d = Mathf.Sqrt(dx * dx + dy * dy);
                        if (d < best) best = d;
                    }
                if (best <= glowRadius)
                {
                    float t = 1f - best / (glowRadius + 0.01f);
                    b.Set(x, y, new Color(glow.r, glow.g, glow.b, glowAlpha * t * t));
                }
            }

        // Contorno 1px
        for (int y = 0; y < b.H; y++)
            for (int x = 0; x < b.W; x++)
            {
                if (src[y * b.W + x].a > 0.5f) continue;
                if (Op(x + 1, y) || Op(x - 1, y) || Op(x, y + 1) || Op(x, y - 1))
                    b.Set(x, y, outline);
            }
    }

    // ------------------------------------------------------------------
    // CORAZÓN (64x64)
    // ------------------------------------------------------------------
    private static float HeartF(float u, float v)
    {
        float a = u * u + v * v - 1f;
        return a * a * a - u * u * v * v * v;
    }

    private static Sprite BuildHeart(ElementSkill e)
    {
        const int W = 64, H = 64;
        const float S = 19f, CX = 32f, CY = 33f;
        var b = new Buf(W, H);
        Color[] ramp = HeartRamp(e);
        Color accent = Accent(e);
        int seed = 11 + (int)e * 7;

        bool[] inside = new bool[W * H];
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                float u = (x + 0.5f - CX) / S, v = (y + 0.5f - CY) / S;
                inside[y * W + x] = HeartF(u, v) <= 0f;
            }
        bool In(int x, int y) => x >= 0 && y >= 0 && x < W && y < H && inside[y * W + x];

        // Relleno con sombreado cúbico de 5 tonos
        Vector2 light = new Vector2(-0.55f, 0.75f).normalized;
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                if (!In(x, y)) continue;
                float u = (x + 0.5f - CX) / S, v = (y + 0.5f - CY) / S;
                float f = HeartF(u, v);
                float depth = Mathf.Clamp01(-f);
                const float eps = 0.02f;
                float gx = HeartF(u + eps, v) - HeartF(u - eps, v);
                float gy = HeartF(u, v + eps) - HeartF(u, v - eps);
                Vector2 g = new Vector2(gx, gy);
                float lit = g.sqrMagnitude > 1e-8f ? Vector2.Dot(g.normalized, light) : 0f;
                float rim = 1f - depth;                 // más cerca del borde -> más influencia direccional
                float bright = 0.42f + 0.30f * depth + 0.38f * lit * rim;
                // lóbulos: abultamiento superior iluminado
                bright += 0.12f * Mathf.Clamp01(1.2f - Mathf.Abs(u) * 1.1f) * Mathf.Clamp01(v);
                // textura orgánica sutil
                bright += (Noise(x * 0.35f, y * 0.35f, seed) - 0.5f) * 0.16f;
                int idx = Mathf.Clamp(Mathf.FloorToInt(bright * 5f), 0, 4);
                b.Set(x, y, ramp[idx]);
            }

        // Venas / grietas irregulares con brillo interior
        var rnd = new System.Random(seed);
        for (int k = 0; k < 3; k++)
        {
            float sx = 18 + rnd.Next(0, 28);
            float sy = 48 - rnd.Next(0, 5);
            float x = sx, y = sy;
            int steps = 20 + rnd.Next(0, 8);
            for (int i = 0; i < steps; i++)
            {
                float nx = x + (float)(rnd.NextDouble() - 0.5) * 5f;
                float ny = y - 1.6f - (float)rnd.NextDouble() * 1.6f;
                Line(b, Mathf.RoundToInt(x), Mathf.RoundToInt(y), Mathf.RoundToInt(nx), Mathf.RoundToInt(ny), ramp[0], In);
                Line(b, Mathf.RoundToInt(x) + 1, Mathf.RoundToInt(y), Mathf.RoundToInt(nx) + 1, Mathf.RoundToInt(ny),
                    new Color(accent.r, accent.g, accent.b, 1f) * 0.85f + new Color(0, 0, 0, 0.15f), In);
                x = nx; y = ny;
            }
        }

        switch (e)
        {
            case ElementSkill.Electric:
                // Rayo central en zigzag
                int bx = 32, by = 52;
                while (by > 14)
                {
                    int nx = bx + (by % 2 == 0 ? 6 : -6) + rnd.Next(-1, 2);
                    int ny = by - 7;
                    Line(b, bx, by, nx, ny, ramp[4], In);
                    Line(b, bx + 1, by, nx + 1, ny, accent, In);
                    bx = nx; by = ny;
                }
                break;
            case ElementSkill.Curse:
            {
                // Ojo vertical con pupila rajada
                Disc(b, 32, 33, 9.5f, ramp[0], In);
                Disc(b, 32, 33, 7.5f, new Color(accent.r * 0.35f, accent.g * 0.2f, accent.b * 0.55f, 1f), In);
                for (int yy = 25; yy <= 41; yy++)
                {
                    float t = 1f - Mathf.Abs(yy - 33f) / 9f;
                    int hw = t > 0.55f ? 1 : 0;
                    for (int xx = 32 - hw; xx <= 32 + hw; xx++) b.Set(xx, yy, accent);
                }
                b.Set(31, 33, ramp[4]); b.Set(32, 34, ramp[4]);
                break;
            }
            case ElementSkill.Poison:
                // Burbujas tóxicas
                Disc(b, 24, 40, 3.2f, ramp[4], In); Disc(b, 24, 40, 2.2f, ramp[2], In); b.Set(23, 41, Color.white);
                Disc(b, 40, 30, 2.6f, ramp[4], In); Disc(b, 40, 30, 1.7f, ramp[2], In); b.Set(39, 31, Color.white);
                Disc(b, 33, 22, 2f, ramp[4], In); Disc(b, 33, 22, 1.1f, ramp[2], In);
                break;
        }

        // Espinas de metal oscuro alrededor
        float[] angles = { 12f, 48f, 132f, 168f, 198f, 228f, 312f, 342f };
        Color steelD = C(0.10f, 0.10f, 0.13f), steelL = C(0.50f, 0.52f, 0.58f);
        foreach (float deg in angles)
        {
            float rad = deg * Mathf.Deg2Rad;
            Vector2 dir = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
            Vector2 p = new Vector2(CX, CY);
            int safety = 0;
            while (safety++ < 80 && In(Mathf.RoundToInt(p.x), Mathf.RoundToInt(p.y))) p += dir * 0.5f;
            Vector2 baseP = p - dir * 1.5f;
            Vector2 tip = p + dir * 7.5f;
            Vector2 perp = new Vector2(-dir.y, dir.x);
            for (float t = 0f; t <= 1f; t += 0.04f)
            {
                Vector2 c = Vector2.Lerp(baseP, tip, t);
                float half = Mathf.Lerp(2.6f, 0f, t);
                for (float w = -half; w <= half; w += 0.5f)
                {
                    Vector2 q = c + perp * w;
                    bool litSide = Vector2.Dot(perp * w, light) > 0f;
                    b.Set(Mathf.RoundToInt(q.x), Mathf.RoundToInt(q.y), litSide && Mathf.Abs(w) > half * 0.4f ? steelL : steelD);
                }
            }
        }

        // Gotas bajo el corazón (sangre / veneno)
        if (e == ElementSkill.Bleed || e == ElementSkill.Poison)
        {
            int[] dx = { 26, 33, 39 };
            int[] len = { 6, 9, 5 };
            for (int i = 0; i < dx.Length; i++)
            {
                int top = 15;
                for (int k = 0; k < len[i]; k++) b.Set(dx[i], top - k, ramp[3]);
                b.Set(dx[i] - 1, top - len[i], ramp[2]); b.Set(dx[i], top - len[i], ramp[3]);
                b.Set(dx[i] + 1, top - len[i], ramp[2]); b.Set(dx[i], top - len[i] - 1, ramp[2]);
                b.Set(dx[i], top - 1, ramp[4]);
            }
        }

        // Chispas exteriores para el eléctrico
        if (e == ElementSkill.Electric)
        {
            int[,] sp = { { 5, 50 }, { 58, 44 }, { 8, 20 }, { 56, 14 }, { 31, 61 } };
            for (int i = 0; i < sp.GetLength(0); i++)
            {
                int x = sp[i, 0], y = sp[i, 1];
                b.Set(x, y, ramp[4]); b.Set(x - 1, y, accent); b.Set(x + 1, y, accent); b.Set(x, y - 1, accent); b.Set(x, y + 1, accent);
            }
        }

        // Brillo especular en el lóbulo superior izquierdo
        for (int i = 0; i < 4; i++) b.Set(21 + i, 47 - (i > 1 ? 1 : 0), ramp[4]);
        b.Set(20, 46, ramp[4]); b.Set(20, 45, ramp[3]);

        OutlineAndGlow(b, C(0.02f, 0.02f, 0.03f), accent, 4, 0.30f);
        return Scale2x(b).ToSprite(64f);
    }

    // ------------------------------------------------------------------
    // CALAVERA (96x96)
    // ------------------------------------------------------------------
    private static Sprite BuildSkull(ElementSkill e)
    {
        const int W = 96, H = 96;
        var b = new Buf(W, H);
        Color[] ramp = BoneRamp(e);
        Color accent = Accent(e);
        int seed = 31 + (int)e * 5;

        bool Cranium(float x, float y) { float dx = (x - 48f) / 29f, dy = (y - 57f) / 28f; return dx * dx + dy * dy <= 1f; }
        bool Cheeks(float x, float y) { float dx = (x - 48f) / 24f, dy = (y - 43f) / 10f; return dx * dx + dy * dy <= 1f; }
        bool Jaw(float x, float y) { return y >= 20f && y <= 46f && Mathf.Abs(x - 48f) <= 13.5f + (y - 20f) * 0.18f; }
        bool Mask(float x, float y) => Cranium(x, y) || Cheeks(x, y) || Jaw(x, y);

        bool Eye(float x, float y, float cx)
        {
            float sign = cx < 48f ? 1f : -1f;
            float dx = (x - cx) + (y - 52f) * 0.28f * sign;
            float dy = (y - 52f);
            return (dx * dx) / (9.5f * 9.5f) + (dy * dy) / (10.5f * 10.5f) <= 1f;
        }
        bool Nose(float x, float y) { return y >= 31f && y <= 44f && Mathf.Abs(x - 48f) <= 0.6f + (44f - y) * 0.36f; }
        bool Hole(float x, float y) => Eye(x, y, 35f) || Eye(x, y, 61f) || Nose(x, y);

        Vector2 light = new Vector2(-0.5f, 0.75f).normalized;
        bool[] bone = new bool[W * H];
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
                bone[y * W + x] = Mask(x + 0.5f, y + 0.5f) && !Hole(x + 0.5f, y + 0.5f);
        bool BoneAt(int x, int y) => x >= 0 && y >= 0 && x < W && y < H && bone[y * W + x];

        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                if (!BoneAt(x, y)) continue;
                float fx = x + 0.5f, fy = y + 0.5f;
                // Profundidad aprox.: distancia al borde por muestreo
                int near = 99;
                for (int r = 1; r <= 4 && near == 99; r++)
                    if (!BoneAt(x + r, y) || !BoneAt(x - r, y) || !BoneAt(x, y + r) || !BoneAt(x, y - r)) near = r;
                float edge = near == 99 ? 1f : near / 5f;
                float nx = (fx - 48f) / 30f, ny = (fy - 54f) / 30f;
                float lit = -nx * (-light.x) * 0.0f + (nx * light.x + ny * light.y);
                float bright = 0.40f + 0.34f * edge + 0.30f * lit;
                bright += (Noise(x * 0.25f, y * 0.25f, seed) - 0.5f) * 0.18f;
                int idx = Mathf.Clamp(Mathf.FloorToInt(bright * 5f), 0, 4);
                b.Set(x, y, ramp[idx]);
            }

        // Cuencas: fondo casi negro con resplandor de acento
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                float fx = x + 0.5f, fy = y + 0.5f;
                if (Eye(fx, fy, 35f) || Eye(fx, fy, 61f))
                {
                    b.Set(x, y, new Color(0.02f, 0.01f, 0.03f, 1f));
                }
                else if (Nose(fx, fy))
                {
                    b.Set(x, y, new Color(0.03f, 0.02f, 0.04f, 1f));
                }
            }
        foreach (float cx in new[] { 35f, 61f })
        {
            Disc(b, cx, 51f, 6f, new Color(accent.r, accent.g, accent.b, 1f) * 0.22f + new Color(0, 0, 0, 0.78f),
                (x, y) => Eye(x + 0.5f, y + 0.5f, cx));
            Disc(b, cx, 51f, 3.6f, accent, (x, y) => Eye(x + 0.5f, y + 0.5f, cx));
            Disc(b, cx, 52f, 1.6f, Color.white, (x, y) => Eye(x + 0.5f, y + 0.5f, cx));
        }

        // Dientes
        Color toothLine = ramp[0];
        for (int x = 38; x <= 58; x += 4)
            Line(b, x, 21, x, 30, toothLine, (px, py) => BoneAt(px, py));
        Line(b, 36, 30, 60, 30, toothLine, (px, py) => BoneAt(px, py));
        Line(b, 38, 21, 58, 21, toothLine, (px, py) => BoneAt(px, py));

        // Grietas
        var rnd = new System.Random(seed);
        bool crackAllow(int x, int y) => BoneAt(x, y);
        if (e == ElementSkill.Electric)
        {
            // Cicatriz de rayo desde la frente
            int bx = 50, by = 84;
            while (by > 62)
            {
                int nx = bx + (by % 2 == 0 ? 5 : -5) + rnd.Next(-1, 2);
                int ny = by - 5;
                Line(b, bx, by, nx, ny, ramp[0], crackAllow);
                Line(b, bx + 1, by, nx + 1, ny, accent, crackAllow);
                Line(b, bx + 2, by, nx + 2, ny, ramp[4], crackAllow);
                bx = nx; by = ny;
            }
        }
        else
        {
            for (int k = 0; k < 3; k++)
            {
                float x = 28 + rnd.Next(0, 40), y = 84 - rnd.Next(0, 6);
                for (int i = 0; i < 9; i++)
                {
                    float nx = x + (float)(rnd.NextDouble() - 0.5) * 6f;
                    float ny = y - 2f - (float)rnd.NextDouble() * 3f;
                    Line(b, Mathf.RoundToInt(x), Mathf.RoundToInt(y), Mathf.RoundToInt(nx), Mathf.RoundToInt(ny), ramp[0], crackAllow);
                    x = nx; y = ny;
                }
            }
        }

        if (e == ElementSkill.Curse)
        {
            // Cuernos curvos de cera negra
            Color horn = C(0.08f, 0.04f, 0.14f), hornL = C(0.30f, 0.16f, 0.46f);
            for (int side = -1; side <= 1; side += 2)
            {
                for (float t = 0f; t <= 1f; t += 0.02f)
                {
                    float cx = 48f + side * (18f + 14f * t + 8f * t * t);
                    float cy = 78f + 16f * t - 6f * t * t;
                    float half = Mathf.Lerp(4.2f, 0f, t);
                    for (float w = -half; w <= half; w += 0.5f)
                        b.Set(Mathf.RoundToInt(cx + w * 0.7f), Mathf.RoundToInt(cy + w * 0.7f * (side == 1 ? -1 : 1)), (w * side) > half * 0.2f ? hornL : horn);
                }
            }
            // Cinta de humo (aura)
            var src = (Color[])b.P.Clone();
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    if (src[y * W + x].a > 0.01f) continue;
                    float n = Noise(x * 0.12f, y * 0.12f - 3f, seed + 9);
                    float d = Mathf.Sqrt((x - 48f) * (x - 48f) + (y - 54f) * (y - 54f));
                    float fall = Mathf.Clamp01(1f - (d - 30f) / 18f);
                    float a = Mathf.Clamp01((n - 0.42f) * 2.2f) * fall * 0.55f;
                    if (a > 0.05f) b.Set(x, y, new Color(accent.r * 0.7f, accent.g * 0.35f, accent.b, a));
                }
        }
        else if (e == ElementSkill.Electric)
        {
            // Arcos eléctricos a los lados
            for (int side = -1; side <= 1; side += 2)
            {
                int x = 48 + side * 34, y = 40;
                for (int i = 0; i < 6; i++)
                {
                    int nx = x + side * (i % 2 == 0 ? 3 : -2);
                    int ny = y + 7;
                    Line(b, x, y, nx, ny, accent, null);
                    b.Set(x, y, Color.white);
                    x = nx; y = ny;
                }
            }
        }

        OutlineAndGlow(b, C(0.02f, 0.02f, 0.03f), accent, 5, e == ElementSkill.Electric ? 0.42f : 0.34f);
        return b.ToSprite(96f);
    }
}
