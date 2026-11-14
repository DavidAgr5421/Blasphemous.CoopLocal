using System.Collections.Generic;
using UnityEngine;

namespace Blasphemous.CoopLocal;

/// <summary>
/// Iconos pixel-art (64x64) del árbol de habilidades elemental. Un icono por elemento y por nivel (1-3):
/// cada nivel añade más detalle y presencia del elemento.
///  - Sangrado : hoja superior de Mea Culpa cubierta de sangre, alambre de espinas, goteo (más sangre y grietas en niveles altos).
///  - Veneno   : mano zombi verde con poros que supuran veneno (más carne expuesta, hueso y supuración en niveles altos).
///  - Electricidad: hoja de Mea Culpa con relámpagos irregulares (más rayos, brillo y halo en niveles altos).
///  - Maldición: calavera con puntos blancos en las cuencas y aura mística violeta (grietas, llamas en niveles altos).
/// Todo procedural y cacheado; el tamaño en pantalla replica el del icono vanilla de referencia.
/// </summary>
internal static class ElementalSkillIcons
{
    private const int N = 64;
    private static readonly Dictionary<int, Sprite> cache = new Dictionary<int, Sprite>();

    private static Color C(float r, float g, float b, float a = 1f) => new Color(r, g, b, a);
    private static float Sm(float a, float b, float x) { float t = Mathf.Clamp01((x - a) / (b - a)); return t * t * (3f - 2f * t); }

    internal static Sprite Get(ElementSkill e, int tier, bool unlocked, Sprite vanillaRef)
    {
        tier = Mathf.Clamp(tier, 1, 3);
        int key = ((int)e * 3 + (tier - 1)) * 2 + (unlocked ? 1 : 0);
        if (cache.TryGetValue(key, out Sprite s) && s != null) return s;

        ElementalArt.Buf b;
        switch (e)
        {
            case ElementSkill.Poison: b = BuildHand(tier); break;
            case ElementSkill.Electric: b = BuildLightningBlade(tier); break;
            case ElementSkill.Curse: b = BuildCurseSkull(tier); break;
            default: b = BuildBloodBlade(tier); break;
        }
        if (!unlocked) Dim(b);
        b = ElementalArt.Scale2x(b);   // 128x128: bordes más limpios

        float ppu = 64f;
        if (vanillaRef != null && vanillaRef.textureRect.width > 1f)
            ppu = vanillaRef.pixelsPerUnit * (N * 2) / vanillaRef.textureRect.width;
        Sprite sp = b.ToSprite(ppu);
        Object.DontDestroyOnLoad(sp.texture);
        Object.DontDestroyOnLoad(sp);
        cache[key] = sp;
        return sp;
    }

    private static void Dim(ElementalArt.Buf b)
    {
        for (int i = 0; i < b.P.Length; i++)
        {
            Color c = b.P[i];
            if (c.a <= 0.001f) continue;
            float g = (c.r + c.g + c.b) / 3f;
            c.r = Mathf.Lerp(c.r, g, 0.55f) * 0.55f;
            c.g = Mathf.Lerp(c.g, g, 0.55f) * 0.55f;
            c.b = Mathf.Lerp(c.b, g, 0.55f) * 0.55f;
            c.a *= 0.92f;
            b.P[i] = c;
        }
    }

    // ------------------------------------------------------------------
    // Capa de intensidad (brillo/relámpagos con halo)
    // ------------------------------------------------------------------
    private sealed class Glow
    {
        public readonly float[] I = new float[N * N];

        public void Splat(float x, float y, float R, float strength)
        {
            int x0 = Mathf.FloorToInt(x - R), x1 = Mathf.CeilToInt(x + R);
            int y0 = Mathf.FloorToInt(y - R), y1 = Mathf.CeilToInt(y + R);
            for (int py = y0; py <= y1; py++)
                for (int px = x0; px <= x1; px++)
                {
                    if (px < 0 || py < 0 || px >= N || py >= N) continue;
                    float dx = px + 0.5f - x, dy = py + 0.5f - y;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    if (d > R) continue;
                    float k = d < 0.7f ? 1f : Mathf.Exp(-(d - 0.7f) * 0.8f);
                    k *= strength;
                    int idx = py * N + px;
                    if (k > I[idx]) I[idx] = k;
                }
        }

        public void Line(float x0, float y0, float x1, float y1, float R, float strength)
        {
            float len = Mathf.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0));
            int steps = Mathf.Max(1, Mathf.CeilToInt(len * 2f));
            for (int i = 0; i <= steps; i++)
            {
                float t = i / (float)steps;
                Splat(Mathf.Lerp(x0, x1, t), Mathf.Lerp(y0, y1, t), R, strength);
            }
        }

        public void Bolt(float x0, float y0, float x1, float y1, int seed, float jitter, int segs, float R, int branches, float strength)
        {
            var rnd = new System.Random(seed);
            Vector2 a = new Vector2(x0, y0), d = new Vector2(x1 - x0, y1 - y0);
            Vector2 perp = new Vector2(-d.y, d.x).normalized;
            Vector2 prev = a;
            var pts = new List<Vector2> { a };
            for (int s = 1; s <= segs; s++)
            {
                Vector2 p = a + d * (s / (float)segs);
                if (s < segs) p += perp * (float)((rnd.NextDouble() - 0.5) * 2.0 * jitter);
                Line(prev.x, prev.y, p.x, p.y, R, strength);
                pts.Add(p);
                prev = p;
            }
            for (int k = 0; k < branches && pts.Count > 3; k++)
            {
                Vector2 q = pts[rnd.Next(1, pts.Count - 1)];
                float ang = (float)((rnd.NextDouble() * 0.6 + 0.5) * (rnd.Next(2) == 0 ? 1 : -1));
                Vector2 dir = Rotate(d.normalized, ang) * d.magnitude * 0.42f;
                Bolt(q.x, q.y, q.x + dir.x, q.y + dir.y, seed + 17 + k, jitter * 0.7f, Mathf.Max(3, segs / 2), Mathf.Max(1.5f, R * 0.8f), 0, strength * 0.85f);
            }
        }

        private static Vector2 Rotate(Vector2 v, float a)
        {
            float c = Mathf.Cos(a), s = Mathf.Sin(a);
            return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
        }

        public void Composite(ElementalArt.Buf b, Color halo, float alphaMul)
        {
            for (int i = 0; i < I.Length; i++)
            {
                float v = I[i];
                if (v < 0.04f) continue;
                Color col = Color.Lerp(halo, Color.white, Sm(0.55f, 0.95f, v));
                float a = Mathf.Clamp01(v * 1.15f) * alphaMul;
                Color old = b.P[i];
                float outA = a + old.a * (1f - a);
                Color rgb = outA > 0f ? (col * a + old * old.a * (1f - a)) / outA : col;
                b.P[i] = new Color(rgb.r, rgb.g, rgb.b, outA);
            }
        }
    }

    // ------------------------------------------------------------------
    // Hoja de Mea Culpa (compartida por sangrado y electricidad)
    // ------------------------------------------------------------------
    private static float BladeHalfWidth(float y)
    {
        if (y > 61f || y < 20f) return -1f;
        float fromTip = 61f - y;
        return fromTip < 7f ? fromTip * 0.36f : 2.5f + (fromTip - 7f) * 0.062f;
    }

    private static void DrawBlade(ElementalArt.Buf b, Color[] steel, int seed)
    {
        for (int y = 20; y <= 61; y++)
        {
            float hw = BladeHalfWidth(y + 0.5f);
            if (hw <= 0f) continue;
            for (int x = 0; x < N; x++)
            {
                float dx = x + 0.5f - 32f;
                if (Mathf.Abs(dx) > hw) continue;
                float t = dx / hw;
                int idx = t < 0f ? 3 : 2;
                if (Mathf.Abs(t) > 0.74f) idx = t < 0f ? 4 : 1;
                else if (Mathf.Abs(t) < 0.16f && y < 56) idx = 1;
                if (y > 50 && idx < 4) idx++;
                float n = ElementalArt.Noise(x * 0.5f, y * 0.3f, seed);
                if (n > 0.84f) idx = Mathf.Max(0, idx - 1);
                else if (n < 0.09f) idx = Mathf.Min(4, idx + 1);
                b.Set(x, y, steel[idx]);
            }
        }
    }

    private static void DrawGuardAndGrip(ElementalArt.Buf b, Color[] steel, Color gem)
    {
        // Guarda con puntas curvadas hacia abajo
        for (int x = 14; x <= 50; x++)
        {
            float adx = Mathf.Abs(x + 0.5f - 32f);
            float top = 19f - (adx > 10f ? (adx - 10f) * 0.55f : 0f);
            for (int k = 0; k < 4; k++)
            {
                int y = Mathf.RoundToInt(top) - k;
                b.Set(x, y, steel[k == 0 ? 3 : (k == 1 ? 2 : 1)]);
            }
            if (adx > 15f) b.Set(x, Mathf.RoundToInt(top) - 4, steel[1]);
        }
        // Gema central
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++) b.Set(32 + dx, 17 + dy, Mathf.Abs(dx) + Mathf.Abs(dy) == 2 ? steel[0] : gem);
        b.Set(31, 18, Color.white);
        // Empuñadura envuelta
        for (int y = 3; y <= 13; y++)
            for (int x = 30; x <= 34; x++)
            {
                bool band = ((x + y) % 3) == 0;
                b.Set(x, y, band ? steel[0] : steel[x < 32 ? 2 : 1]);
            }
        // Pomo
        ElementalArt.Disc(b, 32f, 2.5f, 3.4f, steel[2], null);
        ElementalArt.Disc(b, 31f, 3.3f, 1.2f, steel[4], null);
    }

    // ------------------------------------------------------------------
    // SANGRADO: hoja ensangrentada con alambre de espinas
    // ------------------------------------------------------------------
    private static ElementalArt.Buf BuildBloodBlade(int tier)
    {
        var b = new ElementalArt.Buf(N, N);
        Color[] steel = { C(0.06f, 0.06f, 0.08f), C(0.16f, 0.17f, 0.21f), C(0.34f, 0.36f, 0.42f), C(0.58f, 0.61f, 0.68f), C(0.88f, 0.90f, 0.95f) };
        Color[] blood = { C(0.16f, 0.01f, 0.03f), C(0.36f, 0.03f, 0.06f), C(0.58f, 0.05f, 0.09f), C(0.82f, 0.11f, 0.14f), C(1f, 0.46f, 0.42f) };
        int seed = 41 + tier;

        DrawBlade(b, steel, seed);
        DrawGuardAndGrip(b, steel, blood[3]);

        // Cobertura de sangre: crece con el nivel
        float baseY = tier == 1 ? 34f : (tier == 2 ? 44f : 54f);
        for (int y = 20; y <= 61; y++)
        {
            float hw = BladeHalfWidth(y + 0.5f);
            if (hw <= 0f) continue;
            for (int x = 0; x < N; x++)
            {
                float dx = x + 0.5f - 32f;
                if (Mathf.Abs(dx) > hw) continue;
                float bY = baseY + 7f * ElementalArt.Noise(x * 0.22f, 1.7f, seed);
                bool streak = ElementalArt.Hash(x, 7, seed) > 0.55f && y < bY + 6f + ElementalArt.Hash(x, 9, seed) * 5f;
                if (y >= bY && !streak) continue;
                float t = dx / hw;
                int idx = 2;
                if (t < -0.45f) idx = 3; else if (t > 0.45f) idx = 1;
                if (y >= bY - 2f) idx = 4;                         // menisco húmedo
                float n = ElementalArt.Noise(x * 0.6f, y * 0.4f, seed + 3);
                if (n > 0.86f) idx = 4; else if (n < 0.14f) idx = 0;
                b.Set(x, y, blood[idx]);
            }
        }
        // Sangre en el centro de la guarda
        ElementalArt.Disc(b, 32f, 16f, tier == 1 ? 3f : 5f, blood[2], (x, y) => y >= 13 && y <= 19);
        ElementalArt.Disc(b, 30.5f, 17.5f, 1.2f, blood[4], (x, y) => y >= 13 && y <= 19);

        // Alambre de espinas enrollado (1-3 vueltas)
        int wraps = tier;
        float[] wy = { 27f, 37f, 47f };
        Color iron = C(0.08f, 0.08f, 0.10f), ironL = C(0.50f, 0.52f, 0.58f);
        for (int w = 0; w < wraps; w++)
        {
            float y0 = wy[w];
            float hw = BladeHalfWidth(y0 + 0.5f) + 3f;
            int xa = Mathf.RoundToInt(32f - hw), xb = Mathf.RoundToInt(32f + hw);
            ElementalArt.Line(b, xa, Mathf.RoundToInt(y0) - 2, xb, Mathf.RoundToInt(y0) + 2, iron, null);
            ElementalArt.Line(b, xa, Mathf.RoundToInt(y0) - 1, xb, Mathf.RoundToInt(y0) + 3, ironL, null);
            // espinas
            for (int k = -2; k <= 2; k++)
            {
                int sx = 32 + k * 3;
                int sy = Mathf.RoundToInt(y0) + Mathf.RoundToInt((sx - 32) * 0.18f);
                b.Set(sx, sy + 2, iron); b.Set(sx, sy + 3, iron);
                b.Set(sx + 1, sy - 3, iron); b.Set(sx + 1, sy - 4, ironL);
            }
            b.Set(xa - 1, Mathf.RoundToInt(y0) - 3, iron); b.Set(xb + 1, Mathf.RoundToInt(y0) + 3, iron);
        }

        // Goteo desde la guarda hacia la empuñadura
        int drips = 2 + tier * 2;
        for (int i = 0; i < drips; i++)
        {
            int dx = 17 + Mathf.RoundToInt(ElementalArt.Hash(i, 3, seed) * 30f);
            int len = 3 + Mathf.RoundToInt(ElementalArt.Hash(i, 5, seed) * (4 + tier * 3));
            int top = 14;
            if (dx > 29 && dx < 35) continue; // no sobre la empuñadura
            for (int k = 0; k < len; k++) b.Set(dx, top - k, blood[k == 0 ? 2 : 3]);
            b.Set(dx - 1, top - len, blood[2]); b.Set(dx, top - len, blood[3]); b.Set(dx + 1, top - len, blood[2]);
            b.Set(dx, top - len - 1, blood[2]); b.Set(dx, top - 1, blood[4]);
        }
        // Salpicaduras
        int splat = 4 + tier * 5;
        for (int i = 0; i < splat; i++)
        {
            float sy = 18f + ElementalArt.Hash(i, 11, seed) * 40f;
            float hw = Mathf.Max(2f, BladeHalfWidth(sy));
            float side = ElementalArt.Hash(i, 12, seed) > 0.5f ? 1f : -1f;
            float sx = 32f + side * (hw + 2f + ElementalArt.Hash(i, 13, seed) * 9f);
            ElementalArt.Disc(b, sx, sy, 0.7f + ElementalArt.Hash(i, 14, seed) * 0.9f, blood[ElementalArt.Hash(i, 15, seed) > 0.5f ? 3 : 2], null);
        }

        // Nivel 3: la hoja se agrieta con luz carmesí
        if (tier >= 2)
        {
            var g = new Glow();
            g.Bolt(32f, 58f, 30f, 28f, seed + 5, 1.8f, 9, 1.1f, tier == 3 ? 3 : 1, 0.9f);
            g.Composite(b, C(1f, 0.15f, 0.18f), 0.95f);
        }

        ElementalArt.OutlineAndGlow(b, C(0.02f, 0.01f, 0.02f), C(1f, 0.15f, 0.2f), 3 + tier, 0.18f + 0.1f * tier);
        return b;
    }

    // ------------------------------------------------------------------
    // ELECTRICIDAD: hoja con relámpagos irregulares
    // ------------------------------------------------------------------
    private static ElementalArt.Buf BuildLightningBlade(int tier)
    {
        var b = new ElementalArt.Buf(N, N);
        Color[] steel = { C(0.04f, 0.07f, 0.16f), C(0.12f, 0.20f, 0.38f), C(0.28f, 0.44f, 0.70f), C(0.60f, 0.79f, 0.96f), C(0.95f, 1f, 1f) };
        int seed = 71 + tier;
        DrawBlade(b, steel, seed);
        DrawGuardAndGrip(b, steel, C(0.5f, 0.95f, 1f));

        // Hoja al rojo blanco con el nivel
        if (tier >= 3)
        {
            for (int y = 20; y <= 61; y++)
            {
                float hw = BladeHalfWidth(y + 0.5f);
                if (hw <= 0f) continue;
                for (int x = 0; x < N; x++)
                {
                    if (Mathf.Abs(x + 0.5f - 32f) > hw * 0.45f) continue;
                    Color c = b.Get(x, y);
                    if (c.a > 0f) b.Set(x, y, Color.Lerp(c, Color.white, 0.55f));
                }
            }
        }

        var g = new Glow();
        // Rayo a lo largo del canal central
        g.Bolt(32f, 58f, 32f, 24f, seed + 1, 1.1f, 10, 1.4f, 0, 0.85f);

        int boltCount = 3 + tier * 2;
        for (int i = 0; i < boltCount; i++)
        {
            bool right = i % 2 == 0;
            float sy = 24f + ElementalArt.Hash(i, 3, seed) * 32f;
            float hw = Mathf.Max(1.5f, BladeHalfWidth(sy));
            float sx = 32f + (right ? hw : -hw);
            float len = 11f + ElementalArt.Hash(i, 4, seed) * (7f + tier * 3f);
            float ang = (ElementalArt.Hash(i, 5, seed) - 0.5f) * 1.5f;
            float tx = sx + (right ? 1f : -1f) * len * Mathf.Cos(ang);
            float ty = sy + len * Mathf.Sin(ang) + 4f;
            g.Bolt(sx, sy, Mathf.Clamp(tx, 2f, N - 3f), Mathf.Clamp(ty, 2f, N - 3f), seed + 30 + i, 2.2f, 7, 1.5f, tier >= 2 ? 1 : 0, 0.95f);
        }
        // Corona de rayos sobre la punta
        for (int i = 0; i < tier; i++)
        {
            float ox = (i - (tier - 1) * 0.5f) * 8f;
            g.Bolt(32f, 60f, 32f + ox + (i % 2 == 0 ? -3f : 3f), 62.5f, seed + 60 + i, 1.2f, 4, 1.3f, 0, 0.9f);
        }
        // Descarga desde los extremos de la guarda
        g.Bolt(15f, 16f, 6f, 8f + tier, seed + 80, 2f, 5, 1.4f, 0, 0.9f);
        g.Bolt(49f, 16f, 58f, 8f + tier, seed + 81, 2f, 5, 1.4f, 0, 0.9f);
        g.Composite(b, C(0.22f, 0.62f, 1f), 1f);

        ElementalArt.OutlineAndGlow(b, C(0.01f, 0.02f, 0.06f), C(0.3f, 0.75f, 1f), 3 + tier, 0.22f + 0.1f * tier);
        return b;
    }

    // ------------------------------------------------------------------
    // VENENO: mano zombi con poros que supuran
    // ------------------------------------------------------------------
    private sealed class Seg
    {
        public float ax, ay, bx, by, r0, r1;
        public Seg(float ax, float ay, float bx, float by, float r0, float r1) { this.ax = ax; this.ay = ay; this.bx = bx; this.by = by; this.r0 = r0; this.r1 = r1; }
    }

    private static ElementalArt.Buf BuildHand(int tier)
    {
        var b = new ElementalArt.Buf(N, N);
        Color[] flesh = { C(0.06f, 0.12f, 0.06f), C(0.15f, 0.26f, 0.13f), C(0.29f, 0.45f, 0.21f), C(0.49f, 0.66f, 0.30f), C(0.77f, 0.90f, 0.50f) };
        Color[] bone = { C(0.28f, 0.26f, 0.18f), C(0.55f, 0.52f, 0.38f), C(0.80f, 0.77f, 0.60f), C(0.93f, 0.91f, 0.76f), C(1f, 0.99f, 0.90f) };
        Color[] raw = { C(0.22f, 0.04f, 0.07f), C(0.42f, 0.08f, 0.12f), C(0.64f, 0.16f, 0.20f), C(0.82f, 0.30f, 0.32f), C(0.95f, 0.55f, 0.52f) };
        Color toxic = C(0.55f, 1f, 0.28f), toxicDark = C(0.15f, 0.45f, 0.10f);
        int seed = 91 + tier;
        Vector2 light = new Vector2(-0.6f, 0.8f).normalized;

        var segs = new List<Seg>
        {
            new Seg(32, 12, 32, 3, 8.5f, 9.5f),            // muñeca
            new Seg(23, 30, 21, 42, 4.4f, 3.8f), new Seg(21, 42, 21, 53, 3.8f, 3.1f),   // índice
            new Seg(30, 32, 29, 45, 4.5f, 3.9f), new Seg(29, 45, 30, 58, 3.9f, 3.1f),   // corazón
            new Seg(37, 31, 38, 43, 4.4f, 3.8f), new Seg(38, 43, 40, 55, 3.8f, 3.0f),   // anular
            new Seg(44, 27, 46, 37, 3.9f, 3.4f), new Seg(46, 37, 49, 47, 3.4f, 2.8f),   // meñique
            new Seg(19, 22, 13, 29, 4.6f, 4.0f), new Seg(13, 29, 8, 39, 4.0f, 3.2f),    // pulgar
        };
        // Índices de falanges distales (para hueso en nivel 3): 2,4,6,8,10
        int[] distal = { 2, 4, 6, 8, 10 };

        bool[] mask = new bool[N * N];
        float[] dn = new float[N * N];
        Vector2[] dirOut = new Vector2[N * N];
        int[] part = new int[N * N];
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float px = x + 0.5f, py = y + 0.5f;
                float best = 99f; Vector2 bd = Vector2.zero; int bp = -1;
                // palma elíptica
                float ex = (px - 32f) / 14.5f, ey = (py - 23f) / 12f;
                float ed = Mathf.Sqrt(ex * ex + ey * ey);
                if (ed < best) { best = ed; bd = new Vector2(px - 32f, py - 23f); bp = -2; }
                for (int s = 0; s < segs.Count; s++)
                {
                    Seg g = segs[s];
                    float dx = g.bx - g.ax, dy = g.by - g.ay;
                    float l2 = dx * dx + dy * dy;
                    float t = Mathf.Clamp01(((px - g.ax) * dx + (py - g.ay) * dy) / l2);
                    float cx = g.ax + dx * t, cy = g.ay + dy * t;
                    float d = Mathf.Sqrt((px - cx) * (px - cx) + (py - cy) * (py - cy));
                    float r = Mathf.Lerp(g.r0, g.r1, t);
                    float n = d / r;
                    if (n < best) { best = n; bd = new Vector2(px - cx, py - cy); bp = s; }
                }
                if (best <= 1f)
                {
                    int i = y * N + x;
                    mask[i] = true; dn[i] = best; dirOut[i] = bd.sqrMagnitude > 1e-6f ? bd.normalized : Vector2.zero; part[i] = bp;
                }
            }

        // Relleno carne zombi
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                int i = y * N + x;
                if (!mask[i]) continue;
                float lit = Vector2.Dot(dirOut[i], light);
                float bright = 0.44f + 0.30f * (1f - dn[i]) + 0.36f * lit * dn[i];
                bright += (ElementalArt.Noise(x * 0.4f, y * 0.4f, seed) - 0.5f) * 0.22f;
                int idx = Mathf.Clamp(Mathf.FloorToInt(bright * 5f), 0, 4);
                b.Set(x, y, flesh[idx]);
            }

        // Pliegues en articulaciones
        for (int s = 1; s < segs.Count; s += 2)
        {
            Seg g = segs[s];
            Vector2 j = new Vector2(g.bx, g.by);
            Vector2 axis = new Vector2(g.bx - g.ax, g.by - g.ay).normalized;
            Vector2 perp = new Vector2(-axis.y, axis.x);
            for (int k = -4; k <= 4; k++)
            {
                int x = Mathf.RoundToInt(j.x + perp.x * k), y = Mathf.RoundToInt(j.y + perp.y * k);
                if (x >= 0 && y >= 0 && x < N && y < N && mask[y * N + x]) b.Set(x, y, flesh[0]);
            }
        }

        // Hueso a la vista (nivel 2: nudillos / nivel 3: falanges distales + costillas de la palma)
        if (tier >= 2)
        {
            int[] joints = { 1, 3, 5, 7, 9 };
            int count = tier == 2 ? 3 : 5;
            for (int k = 0; k < count; k++)
            {
                Seg g = segs[joints[k]];
                for (int dy = -2; dy <= 2; dy++)
                    for (int dx = -2; dx <= 2; dx++)
                    {
                        int x = Mathf.RoundToInt(g.bx) + dx, y = Mathf.RoundToInt(g.by) + dy;
                        if (dx * dx + dy * dy > 5) continue;
                        if (x >= 0 && y >= 0 && x < N && y < N && mask[y * N + x])
                            b.Set(x, y, bone[dx < 0 || dy > 0 ? 3 : 2]);
                    }
            }
        }
        if (tier >= 3)
        {
            // Falanges distales descarnadas con garra
            for (int d = 0; d < distal.Length; d++)
            {
                Seg g = segs[distal[d]];
                for (int y = 0; y < N; y++)
                    for (int x = 0; x < N; x++)
                    {
                        int i = y * N + x;
                        if (!mask[i] || part[i] != distal[d]) continue;
                        float px = x + 0.5f, py = y + 0.5f;
                        float dx = g.bx - g.ax, dy = g.by - g.ay;
                        float t = Mathf.Clamp01(((px - g.ax) * dx + (py - g.ay) * dy) / (dx * dx + dy * dy));
                        if (t < 0.38f) continue;
                        int idx = dirOut[i].x < 0 ? 3 : (dirOut[i].x > 0.3f ? 1 : 2);
                        if (dn[i] > 0.8f) idx = 1;
                        b.Set(x, y, bone[idx]);
                    }
                Vector2 axis = new Vector2(g.bx - g.ax, g.by - g.ay).normalized;
                for (int k = 1; k <= 3; k++)
                    b.Set(Mathf.RoundToInt(g.bx + axis.x * (g.r1 * 0.2f + k)), Mathf.RoundToInt(g.by + axis.y * (g.r1 * 0.2f + k)), bone[k == 3 ? 4 : 3]);
            }
            // Herida en la palma con huesos metacarpianos
            for (int y = 16; y <= 29; y++)
                for (int x = 22; x <= 42; x++)
                {
                    float wx = (x - 32f) / 9f, wy = (y - 22.5f) / 5.5f;
                    float wd = wx * wx + wy * wy + (ElementalArt.Noise(x * 0.5f, y * 0.5f, seed + 1) - 0.5f) * 0.5f;
                    if (wd < 1f && mask[y * N + x])
                        b.Set(x, y, wd < 0.55f ? raw[1] : raw[2]);
                }
            for (int k = 0; k < 4; k++)
            {
                int bx = 25 + k * 4;
                ElementalArt.Line(b, bx, 28, bx - 1 + (k > 1 ? 1 : 0), 17, bone[k % 2 == 0 ? 3 : 2], (x, y) => mask[y * N + x]);
            }
        }

        // Carne en carne viva (nivel 2+): heridas abiertas
        if (tier >= 2)
        {
            int wounds = tier == 2 ? 3 : 5;
            for (int k = 0; k < wounds; k++)
            {
                float wx = 18f + ElementalArt.Hash(k, 21, seed) * 28f;
                float wy = 14f + ElementalArt.Hash(k, 22, seed) * 34f;
                int px = Mathf.RoundToInt(wx), py = Mathf.RoundToInt(wy);
                if (px < 0 || py < 0 || px >= N || py >= N || !mask[py * N + px]) continue;
                float rad = 1.8f + ElementalArt.Hash(k, 23, seed) * 1.6f;
                ElementalArt.Disc(b, wx, wy, rad, raw[2], (x, y) => mask[y * N + x]);
                ElementalArt.Disc(b, wx, wy - 0.4f, rad * 0.55f, raw[1], (x, y) => mask[y * N + x]);
                b.Set(px - 1, py + 1, raw[4]);
            }
        }

        // Poros que supuran veneno
        int pores = tier == 1 ? 9 : (tier == 2 ? 15 : 22);
        for (int k = 0; k < pores; k++)
        {
            int x = 10 + Mathf.RoundToInt(ElementalArt.Hash(k, 31, seed) * 44f);
            int y = 6 + Mathf.RoundToInt(ElementalArt.Hash(k, 32, seed) * 52f);
            if (x < 0 || y < 0 || x >= N || y >= N || !mask[y * N + x] || dn[y * N + x] > 0.85f) continue;
            b.Set(x, y, toxic);
            b.Set(x - 1, y, toxicDark); b.Set(x + 1, y, toxicDark); b.Set(x, y + 1, toxicDark);
            if (ElementalArt.Hash(k, 33, seed) < 0.35f + 0.15f * tier)
            {
                int len = 2 + Mathf.RoundToInt(ElementalArt.Hash(k, 34, seed) * (3 + tier * 2));
                for (int d = 1; d <= len; d++) b.Set(x, y - d, d == len ? toxic : C(0.38f, 0.85f, 0.20f));
                b.Set(x, y - len - 1, C(0.7f, 1f, 0.5f));
            }
        }
        // Charco y burbujas tóxicas (nivel 2+)
        if (tier >= 2)
        {
            for (int k = 0; k < tier * 3; k++)
            {
                float bx = 12f + ElementalArt.Hash(k, 41, seed) * 40f;
                float by = 2f + ElementalArt.Hash(k, 42, seed) * 12f;
                float r = 1.2f + ElementalArt.Hash(k, 43, seed) * 1.5f;
                ElementalArt.Disc(b, bx, by, r, C(0.40f, 0.90f, 0.22f, 0.9f), (x, y) => !mask[y * N + x]);
                b.Set(Mathf.RoundToInt(bx) - 1, Mathf.RoundToInt(by) + 1, C(0.9f, 1f, 0.8f));
            }
        }
        if (tier >= 3)
        {
            for (int k = 0; k < 6; k++)
            {
                float bx = 14f + k * 7f + ElementalArt.Hash(k, 51, seed) * 4f;
                float by = 56f + ElementalArt.Hash(k, 52, seed) * 6f;
                ElementalArt.Disc(b, bx, Mathf.Min(by, 62f), 1.1f, C(0.5f, 1f, 0.3f, 0.85f), (x, y) => !mask[y * N + x]);
            }
        }

        // Garras negras en las puntas
        for (int d = 0; d < distal.Length; d++)
        {
            Seg g = segs[distal[d]];
            Vector2 axis = new Vector2(g.bx - g.ax, g.by - g.ay).normalized;
            for (int k = 1; k <= 2; k++)
                b.Set(Mathf.RoundToInt(g.bx + axis.x * (g.r1 + k)), Mathf.RoundToInt(g.by + axis.y * (g.r1 + k)), C(0.10f, 0.12f, 0.08f));
        }

        ElementalArt.OutlineAndGlow(b, C(0.02f, 0.04f, 0.02f), C(0.45f, 1f, 0.25f), 3 + tier, 0.18f + 0.1f * tier);
        return b;
    }

    // ------------------------------------------------------------------
    // MALDICIÓN: calavera con cuencas brillantes y aura violeta
    // ------------------------------------------------------------------
    private static ElementalArt.Buf BuildCurseSkull(int tier)
    {
        var b = new ElementalArt.Buf(N, N);
        Color[] ramp = { C(0.04f, 0.03f, 0.06f), C(0.20f, 0.19f, 0.25f), C(0.45f, 0.44f, 0.51f), C(0.74f, 0.73f, 0.79f), C(0.96f, 0.95f, 0.99f) };
        Color cavity = C(0.015f, 0.01f, 0.03f);
        int seed = 111 + tier;
        const float S = 26f, CX = 32f, CY = 31f, tilt = 0.17f;
        float ct = Mathf.Cos(tilt), st = Mathf.Sin(tilt);

        void ToUnit(float x, float y, out float rx, out float ry)
        {
            float ux = (x - CX) / S, uy = (y - CY) / S;
            rx = ux * ct + uy * st;
            ry = -ux * st + uy * ct;
        }
        Vector2 ToPx(float rx, float ry)
        {
            float ux = rx * ct - ry * st, uy = rx * st + ry * ct;
            return new Vector2(CX + ux * S, CY + uy * S);
        }
        bool Cranium(float rx, float ry) { float a = rx / 0.66f, c = (ry - 0.20f) / 0.62f; return a * a + c * c <= 1f; }
        bool Cheek(float rx, float ry) { return ry >= -0.34f && ry <= 0.10f && Mathf.Abs(rx) <= 0.50f - Mathf.Max(0f, -ry - 0.1f) * 0.4f; }
        bool Teeth(float rx, float ry) { return Mathf.Abs(rx) <= 0.34f && ry >= -0.42f && ry <= -0.28f; }
        bool Mouth(float rx, float ry) { return Mathf.Abs(rx) <= 0.30f && ry >= -0.64f && ry < -0.42f; }
        bool Jaw(float rx, float ry) { return (Mathf.Abs(rx) <= 0.37f && ry >= -0.80f && ry < -0.64f) || (Mathf.Abs(rx) >= 0.30f && Mathf.Abs(rx) <= 0.41f && ry >= -0.80f && ry <= -0.34f); }
        bool Eye(float rx, float ry, float side) { float a = (rx - side * 0.28f) / 0.20f, c = (ry - 0.14f) / 0.22f; return a * a + c * c <= 1f; }
        bool Nose(float rx, float ry) { return ry >= -0.22f && ry <= 0.0f && Mathf.Abs(rx) <= 0.03f + (0f - ry) * 0.45f; }

        bool[] sil = new bool[N * N];
        bool[] cav = new bool[N * N];
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                ToUnit(x + 0.5f, y + 0.5f, out float rx, out float ry);
                bool inside = Cranium(rx, ry) || Cheek(rx, ry) || Teeth(rx, ry) || Mouth(rx, ry) || Jaw(rx, ry);
                bool cavity2 = Eye(rx, ry, -1f) || Eye(rx, ry, 1f) || Nose(rx, ry) || Mouth(rx, ry);
                sil[y * N + x] = inside;
                cav[y * N + x] = inside && cavity2;
            }
        bool Bone(int x, int y) { return x >= 0 && y >= 0 && x < N && y < N && sil[y * N + x] && !cav[y * N + x]; }

        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                int i = y * N + x;
                if (!sil[i]) continue;
                if (cav[i]) { b.Set(x, y, cavity); continue; }
                ToUnit(x + 0.5f, y + 0.5f, out float rx, out float ry);
                int ao = 0;
                if (!Bone(x + 2, y)) ao++; if (!Bone(x - 2, y)) ao++; if (!Bone(x, y + 2)) ao++; if (!Bone(x, y - 2)) ao++;
                float bright = 0.58f + 0.34f * (-rx * 0.6f + ry * 0.5f) - 0.12f * ao;
                bright += (ElementalArt.Noise(x * 0.35f, y * 0.35f, seed) - 0.5f) * 0.20f;
                int idx = Mathf.Clamp(Mathf.FloorToInt(bright * 5f), 0, 4);
                b.Set(x, y, ramp[idx]);
            }

        // Dientes (líneas verticales oscuras)
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                if (!Bone(x, y)) continue;
                ToUnit(x + 0.5f, y + 0.5f, out float rx, out float ry);
                bool upper = Teeth(rx, ry);
                bool lower = Mathf.Abs(rx) <= 0.30f && ry >= -0.70f && ry < -0.62f;
                if ((upper || lower) && (x % 3 == 0)) b.Set(x, y, ramp[0]);
            }

        // Grietas (nivel 2+): oscuras con luz violeta
        if (tier >= 2)
        {
            int cracks = tier == 2 ? 3 : 6;
            for (int k = 0; k < cracks; k++)
            {
                var rnd = new System.Random(seed * 31 + k);
                Vector2 p = ToPx(-0.45f + (float)rnd.NextDouble() * 0.9f, 0.72f);
                for (int s = 0; s < 14 + tier * 3; s++)
                {
                    Vector2 q = new Vector2(p.x + ((float)rnd.NextDouble() - 0.5f) * 5f, p.y - 1.5f - (float)rnd.NextDouble() * 2.5f);
                    ElementalArt.Line(b, Mathf.RoundToInt(p.x), Mathf.RoundToInt(p.y), Mathf.RoundToInt(q.x), Mathf.RoundToInt(q.y), ramp[0], (x, y) => Bone(x, y));
                    ElementalArt.Line(b, Mathf.RoundToInt(p.x) + 1, Mathf.RoundToInt(p.y), Mathf.RoundToInt(q.x) + 1, Mathf.RoundToInt(q.y), C(0.80f, 0.40f, 1f), (x, y) => Bone(x, y) && b.Get(x, y).r < 0.9f);
                    p = q;
                }
            }
        }

        // Puntos blancos brillantes en las cuencas
        foreach (float side in new[] { -1f, 1f })
        {
            Vector2 c = ToPx(side * 0.28f, 0.14f);
            ElementalArt.Disc(b, c.x, c.y, 3.6f, C(0.55f, 0.30f, 0.80f, 1f) * 0.35f + new Color(0f, 0f, 0f, 0.65f), (x, y) => cav[y * N + x]);
            ElementalArt.Disc(b, c.x - 0.5f, c.y + 0.5f, 1.7f, Color.white, (x, y) => cav[y * N + x]);
            ElementalArt.Disc(b, c.x + 2.6f, c.y - 2.2f, 0.9f, C(0.9f, 0.8f, 1f), (x, y) => cav[y * N + x]);
            if (tier >= 2) ElementalArt.Disc(b, c.x - 2.4f, c.y - 1.8f, 0.8f, C(0.95f, 0.85f, 1f), (x, y) => cav[y * N + x]);
            if (tier >= 3) b.Set(Mathf.RoundToInt(c.x + 1f), Mathf.RoundToInt(c.y + 3f), Color.white);
        }

        // Contorno negro de 1 px sobre la silueta
        var src = (Color[])b.P.Clone();
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                if (src[y * N + x].a > 0.5f) continue;
                bool nb = (x > 0 && src[y * N + x - 1].a > 0.5f) || (x < N - 1 && src[y * N + x + 1].a > 0.5f)
                       || (y > 0 && src[(y - 1) * N + x].a > 0.5f) || (y < N - 1 && src[(y + 1) * N + x].a > 0.5f);
                if (nb) b.Set(x, y, C(0.01f, 0.01f, 0.02f));
            }

        // Aura mística violeta
        var occupied = (Color[])b.P.Clone();
        float auraStrength = tier == 1 ? 0.60f : (tier == 2 ? 0.80f : 1.0f);
        const int R = 11;
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                if (occupied[y * N + x].a > 0.01f) continue;
                float best = 99f;
                for (int dy = -R; dy <= R; dy++)
                    for (int dx = -R; dx <= R; dx++)
                    {
                        int nx = x + dx, ny = y + dy;
                        if (nx < 0 || ny < 0 || nx >= N || ny >= N || occupied[ny * N + nx].a < 0.5f) continue;
                        float d = Mathf.Sqrt(dx * dx + dy * dy);
                        if (d < best) best = d;
                    }
                if (best > R) continue;
                float n = ElementalArt.Noise(x * 0.17f, y * 0.17f - tier, seed + 9);
                float a = auraStrength * Mathf.Exp(-best / 5.5f) * (0.35f + 0.65f * n);
                if (a < 0.04f) continue;
                Color c = Color.Lerp(C(0.38f, 0.08f, 0.70f), C(0.88f, 0.58f, 1f), Mathf.Exp(-best / 3.5f));
                b.Set(x, y, new Color(c.r, c.g, c.b, Mathf.Clamp01(a)));
            }

        // Llamas violetas ascendentes (nivel 3)
        if (tier >= 3)
        {
            var g = new Glow();
            for (int k = 0; k < 6; k++)
            {
                float x0 = 20f + k * 4.6f;
                float h = 6f + ElementalArt.Hash(k, 5, seed) * 6f;
                g.Bolt(x0, 50f + ElementalArt.Hash(k, 6, seed) * 3f, x0 + (ElementalArt.Hash(k, 7, seed) - 0.5f) * 6f, Mathf.Min(62.5f, 52f + h), seed + k, 1.6f, 5, 1.3f, 0, 0.7f);
            }
            g.Composite(b, C(0.62f, 0.25f, 1f), 0.75f);
        }
        return b;
    }
}
