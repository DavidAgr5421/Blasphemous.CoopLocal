using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace Blasphemous.CoopLocal;

/// <summary>
/// Calaveras elementales (umbral superado y árbol de habilidades) a partir del cráneo pixel-art de referencia
/// (embebido como recurso: resources/skull_base.png, ya recortado y con alpha). El cráneo se recolorea por
/// elemento con un mapa de degradado según su luminancia y se le añaden efectos propios:
///   Sangrado  : lágrimas y charcos de sangre, pupilas rojas, grietas carmesí.
///   Veneno    : supuración tóxica por cuencas, nariz y dientes, poros, burbujas.
///   Electricidad: cicatrices de rayo con halo, chispas y rayos que irradian del cráneo, pupilas cian.
///   Maldición : puntos blancos brillantes en las cuencas, grietas violetas y aura mística (llamas en nivel 3).
/// El nivel (1-3) añade grietas, goteo, halo y presencia del elemento. Resolución: ~250 px (la versión de
/// icono del árbol se reduce a 128 px).
/// </summary>
internal static class SkullArt
{
    private const int M = 30; // margen para aura / goteo / rayos

    private static Color[] basePx;
    private static int bw, bh;
    private static bool loadFailed;
    private static readonly Dictionary<int, Sprite> cache = new Dictionary<int, Sprite>();

    private static Color C(float r, float g, float b, float a = 1f) { return new Color(r, g, b, a); }

    // ------------------------------------------------------------------
    // API
    // ------------------------------------------------------------------
    /// <summary>Calavera grande (umbral superado). Nivel 2 por defecto.</summary>
    internal static Sprite GetFloating(ElementSkill e, int level = 2)
    {
        int key = 1000 + (int)e * 10 + level;
        if (cache.TryGetValue(key, out Sprite s) && s != null) return s;
        Canvas c = Build(e, level);
        if (c == null) return null;
        var tex = new Texture2D(c.W, c.H, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        tex.SetPixels(c.px);
        tex.Apply();
        s = Sprite.Create(tex, new Rect(0, 0, c.W, c.H), new Vector2(0.5f, 0.5f), Mathf.Max(c.W, c.H));
        Object.DontDestroyOnLoad(tex);
        Object.DontDestroyOnLoad(s);
        cache[key] = s;
        return s;
    }

    /// <summary>Icono del árbol de habilidades (128x128) por nivel.</summary>
    internal static Sprite GetIcon(ElementSkill e, int tier, bool unlocked, Sprite vanillaRef)
    {
        tier = Mathf.Clamp(tier, 1, 3);
        int key = 2000 + ((int)e * 3 + (tier - 1)) * 2 + (unlocked ? 1 : 0);
        if (cache.TryGetValue(key, out Sprite s) && s != null) return s;
        Canvas c = Build(e, tier);
        if (c == null) return null;

        const int OUT = 128;
        Color[] outPx = new Color[OUT * OUT];
        // Reducción por promedio ponderado por alpha, centrando en un lienzo cuadrado
        float scale = Mathf.Max(c.W, c.H) / (float)OUT;
        float ox = (OUT * scale - c.W) * 0.5f, oy = (OUT * scale - c.H) * 0.5f;
        for (int y = 0; y < OUT; y++)
            for (int x = 0; x < OUT; x++)
            {
                float sx0 = x * scale - ox, sy0 = y * scale - oy;
                float r = 0, g = 0, b = 0, a = 0; int n = 0;
                int x0 = Mathf.FloorToInt(sx0), x1 = Mathf.CeilToInt(sx0 + scale);
                int y0 = Mathf.FloorToInt(sy0), y1 = Mathf.CeilToInt(sy0 + scale);
                for (int yy = y0; yy < y1; yy++)
                    for (int xx = x0; xx < x1; xx++)
                    {
                        n++;
                        if (xx < 0 || yy < 0 || xx >= c.W || yy >= c.H) continue;
                        Color p = c.px[yy * c.W + xx];
                        r += p.r * p.a; g += p.g * p.a; b += p.b * p.a; a += p.a;
                    }
                if (n == 0 || a <= 0.0001f) { outPx[y * OUT + x] = Color.clear; continue; }
                outPx[y * OUT + x] = new Color(r / a, g / a, b / a, a / n);
            }
        if (!unlocked)
        {
            for (int i = 0; i < outPx.Length; i++)
            {
                Color p = outPx[i];
                if (p.a <= 0.001f) continue;
                float gr = (p.r + p.g + p.b) / 3f;
                outPx[i] = new Color(Mathf.Lerp(p.r, gr, 0.55f) * 0.55f, Mathf.Lerp(p.g, gr, 0.55f) * 0.55f, Mathf.Lerp(p.b, gr, 0.55f) * 0.55f, p.a * 0.92f);
            }
        }
        var tex = new Texture2D(OUT, OUT, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        tex.SetPixels(outPx);
        tex.Apply();
        float ppu = 32f;
        if (vanillaRef != null && vanillaRef.textureRect.width > 1f)
            ppu = vanillaRef.pixelsPerUnit * OUT / vanillaRef.textureRect.width;
        s = Sprite.Create(tex, new Rect(0, 0, OUT, OUT), new Vector2(0.5f, 0.5f), ppu);
        Object.DontDestroyOnLoad(tex);
        Object.DontDestroyOnLoad(s);
        cache[key] = s;
        return s;
    }

    // ------------------------------------------------------------------
    // Carga del recurso base
    // ------------------------------------------------------------------
    private static bool LoadBase()
    {
        if (basePx != null) return true;
        if (loadFailed) return false;
        try
        {
            using (Stream st = Assembly.GetExecutingAssembly().GetManifestResourceStream("skull_base.png"))
            {
                if (st == null) { loadFailed = true; DashParryDebugLog.Log("[SkullArt] Recurso skull_base.png no encontrado"); return false; }
                byte[] buf = new byte[st.Length];
                int read = 0;
                while (read < buf.Length)
                {
                    int n = st.Read(buf, read, buf.Length - read);
                    if (n <= 0) break;
                    read += n;
                }
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!ImageConversion.LoadImage(tex, buf)) { loadFailed = true; return false; }
                bw = tex.width; bh = tex.height;
                basePx = tex.GetPixels();
                Object.Destroy(tex);
            }
            return true;
        }
        catch (System.Exception ex)
        {
            loadFailed = true;
            DashParryDebugLog.Log("[SkullArt] Error cargando cráneo base: " + ex.Message);
            return false;
        }
    }

    // ------------------------------------------------------------------
    // Lienzo
    // ------------------------------------------------------------------
    private sealed class Canvas
    {
        public int W, H;
        public Color[] px;
        public bool[] bone;        // hueso visible (no cavidad)
        public bool[] cav;         // cavidades (ojos, nariz, boca)
        public List<Vector2> eyes = new List<Vector2>();
        public int topY, botY, minX, maxX;
        public System.Random rnd;

        public bool In(int x, int y) { return x >= 0 && y >= 0 && x < W && y < H; }
        public bool Opaque(int x, int y) { return In(x, y) && px[y * W + x].a > 0.5f; }

        public void Over(int x, int y, Color col, float a)
        {
            if (!In(x, y) || a <= 0f) return;
            a = Mathf.Clamp01(a);
            int i = y * W + x;
            Color old = px[i];
            float oa = a + old.a * (1f - a);
            Color rgb = oa > 0f ? (col * a + old * old.a * (1f - a)) / oa : col;
            px[i] = new Color(rgb.r, rgb.g, rgb.b, oa);
        }
    }

    private static Color Ramp(Color[] stops, float t)
    {
        t = Mathf.Clamp01(t) * (stops.Length - 1);
        int i = Mathf.Min(stops.Length - 2, Mathf.FloorToInt(t));
        return Color.Lerp(stops[i], stops[i + 1], t - i);
    }

    private static Color[] BoneStops(ElementSkill e)
    {
        switch (e)
        {
            case ElementSkill.Poison: return new[] { C(0.04f, 0.09f, 0.04f), C(0.22f, 0.34f, 0.16f), C(0.52f, 0.68f, 0.34f), C(0.88f, 0.98f, 0.66f) };
            case ElementSkill.Electric: return new[] { C(0.03f, 0.06f, 0.14f), C(0.22f, 0.34f, 0.55f), C(0.58f, 0.76f, 0.94f), C(0.95f, 0.99f, 1f) };
            case ElementSkill.Curse: return new[] { C(0.05f, 0.03f, 0.09f), C(0.27f, 0.22f, 0.38f), C(0.62f, 0.56f, 0.76f), C(0.95f, 0.92f, 1f) };
            default: return new[] { C(0.10f, 0.02f, 0.03f), C(0.38f, 0.20f, 0.18f), C(0.76f, 0.58f, 0.50f), C(1f, 0.90f, 0.82f) };
        }
    }

    private static Color Accent(ElementSkill e)
    {
        switch (e)
        {
            case ElementSkill.Poison: return C(0.50f, 1f, 0.28f);
            case ElementSkill.Electric: return C(0.55f, 0.90f, 1f);
            case ElementSkill.Curse: return C(0.78f, 0.38f, 1f);
            default: return C(1f, 0.16f, 0.18f);
        }
    }

    // ------------------------------------------------------------------
    // Construcción
    // ------------------------------------------------------------------
    private static Canvas Build(ElementSkill e, int level)
    {
        if (!LoadBase()) return null;
        level = Mathf.Clamp(level, 1, 3);
        var c = new Canvas { W = bw + M * 2, H = bh + M * 2 };
        c.px = new Color[c.W * c.H];
        c.bone = new bool[c.W * c.H];
        c.cav = new bool[c.W * c.H];
        c.rnd = new System.Random(1000 + (int)e * 37 + level * 7);
        int seed = 500 + (int)e * 13 + level;

        // Mapa de luminancia / cavidades
        float[] lum = new float[bw * bh];
        for (int y = 0; y < bh; y++)
            for (int x = 0; x < bw; x++)
            {
                Color p = basePx[y * bw + x];
                lum[y * bw + x] = p.a > 0.5f ? p.r * 0.3f + p.g * 0.59f + p.b * 0.11f : 0f;
            }
        bool[] dark = new bool[bw * bh];
        for (int y = 3; y < bh - 3; y++)
            for (int x = 3; x < bw - 3; x++)
            {
                if (lum[y * bw + x] >= 0.20f || basePx[y * bw + x].a < 0.5f) continue;
                bool inner = true;
                for (int dy = -3; dy <= 3 && inner; dy++)
                    for (int dx = -3; dx <= 3; dx++)
                        if (basePx[(y + dy) * bw + (x + dx)].a < 0.5f) { inner = false; break; }
                dark[y * bw + x] = inner;
            }

        // Componentes de cavidad (ojos = las 2 mayores más altas de las 3 mayores)
        int[] label = new int[bw * bh];
        var comps = new List<List<int>>();
        for (int i = 0; i < dark.Length; i++)
        {
            if (!dark[i] || label[i] != 0) continue;
            var comp = new List<int>();
            var stack = new Stack<int>();
            stack.Push(i); label[i] = comps.Count + 1;
            while (stack.Count > 0)
            {
                int cur = stack.Pop(); comp.Add(cur);
                int cx = cur % bw, cy = cur / bw;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = cx + dx, ny = cy + dy;
                        if (nx < 0 || ny < 0 || nx >= bw || ny >= bh) continue;
                        int ni = ny * bw + nx;
                        if (dark[ni] && label[ni] == 0) { label[ni] = comps.Count + 1; stack.Push(ni); }
                    }
            }
            comps.Add(comp);
        }
        comps.Sort((a, b) => b.Count.CompareTo(a.Count));
        var eyeComps = new List<List<int>>();
        int take = Mathf.Min(3, comps.Count);
        var top3 = new List<List<int>>();
        for (int i = 0; i < take; i++) top3.Add(comps[i]);
        top3.Sort((a, b) => Centroid(b).y.CompareTo(Centroid(a).y));  // más altas primero (y hacia arriba)
        for (int i = 0; i < Mathf.Min(2, top3.Count); i++) eyeComps.Add(top3[i]);
        foreach (List<int> comp in eyeComps) { Vector2 cen = Centroid(comp); c.eyes.Add(new Vector2(cen.x + M, cen.y + M)); }

        // Recolor por degradado + manchas por elemento
        Color[] stops = BoneStops(e);
        c.topY = 0; c.botY = c.H; c.minX = c.W; c.maxX = 0;
        for (int y = 0; y < bh; y++)
            for (int x = 0; x < bw; x++)
            {
                Color p = basePx[y * bw + x];
                if (p.a < 0.5f) continue;
                int ci = (y + M) * c.W + (x + M);
                float l = lum[y * bw + x];
                Color col = Ramp(stops, l);
                if (dark[y * bw + x])
                {
                    col = Color.Lerp(C(0.01f, 0.005f, 0.02f), col, 0.12f);
                    c.cav[ci] = true;
                }
                else
                {
                    c.bone[ci] = true;
                    if (e == ElementSkill.Bleed)
                    {
                        float n = ElementalArt.Noise(x * 0.07f, y * 0.07f, seed);
                        float thr = 0.66f - 0.07f * level;
                        if (n > thr && l > 0.22f) col = Color.Lerp(col, C(0.62f, 0.04f, 0.08f), Mathf.Clamp01((n - thr) * 4.5f) * 0.85f);
                    }
                    else if (e == ElementSkill.Poison)
                    {
                        float n = ElementalArt.Noise(x * 0.09f, y * 0.09f, seed);
                        if (n > 0.64f - 0.05f * level && l > 0.22f) col = Color.Lerp(col, C(0.35f, 0.85f, 0.15f), Mathf.Clamp01((n - 0.6f) * 3.5f) * 0.6f);
                    }
                }
                c.px[ci] = new Color(col.r, col.g, col.b, 1f);
                int cy2 = y + M, cx2 = x + M;
                if (cy2 > c.topY) c.topY = cy2;
                if (cy2 < c.botY) c.botY = cy2;
                if (cx2 < c.minX) c.minX = cx2;
                if (cx2 > c.maxX) c.maxX = cx2;
            }

        switch (e)
        {
            case ElementSkill.Curse: DecorateCurse(c, level, seed); break;
            case ElementSkill.Electric: DecorateElectric(c, level, seed); break;
            case ElementSkill.Poison: DecoratePoison(c, level, seed); break;
            default: DecorateBleed(c, level, seed); break;
        }
        return c;
    }

    private static Vector2 Centroid(List<int> comp)
    {
        float sx = 0, sy = 0;
        foreach (int i in comp) { sx += i % bw; sy += i / bw; }
        return new Vector2(sx / comp.Count, sy / comp.Count);
    }

    // ------------------------------------------------------------------
    // Utilidades de dibujo
    // ------------------------------------------------------------------
    private sealed class GlowBuf
    {
        public readonly int W, H;
        public readonly float[] I;
        public GlowBuf(int w, int h) { W = w; H = h; I = new float[w * h]; }

        public void Splat(float x, float y, float R, float strength)
        {
            int x0 = Mathf.FloorToInt(x - R), x1 = Mathf.CeilToInt(x + R);
            int y0 = Mathf.FloorToInt(y - R), y1 = Mathf.CeilToInt(y + R);
            for (int py = y0; py <= y1; py++)
                for (int px = x0; px <= x1; px++)
                {
                    if (px < 0 || py < 0 || px >= W || py >= H) continue;
                    float dx = px + 0.5f - x, dy = py + 0.5f - y;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    if (d > R) continue;
                    float k = d < 1.3f ? 1f : Mathf.Exp(-(d - 1.3f) * 0.55f);
                    k *= strength;
                    int idx = py * W + px;
                    if (k > I[idx]) I[idx] = k;
                }
        }

        public void Line(Vector2 a, Vector2 b, float R, float strength)
        {
            float len = (b - a).magnitude;
            int steps = Mathf.Max(1, Mathf.CeilToInt(len * 1.2f));
            for (int i = 0; i <= steps; i++)
            {
                Vector2 p = Vector2.Lerp(a, b, i / (float)steps);
                Splat(p.x, p.y, R, strength);
            }
        }

        public void Bolt(System.Random rnd, Vector2 a, Vector2 b, float jitter, int segs, float R, int branches, float strength)
        {
            Vector2 d = b - a;
            Vector2 perp = new Vector2(-d.y, d.x).normalized;
            Vector2 prev = a;
            var pts = new List<Vector2>();
            pts.Add(a);
            for (int s = 1; s <= segs; s++)
            {
                Vector2 p = a + d * (s / (float)segs);
                if (s < segs) p += perp * (((float)rnd.NextDouble() - 0.5f) * 2f * jitter);
                Line(prev, p, R, strength);
                pts.Add(p); prev = p;
            }
            for (int k = 0; k < branches && pts.Count > 3; k++)
            {
                Vector2 q = pts[rnd.Next(1, pts.Count - 1)];
                float ang = ((float)rnd.NextDouble() * 0.6f + 0.5f) * (rnd.Next(2) == 0 ? 1f : -1f);
                float cs = Mathf.Cos(ang), sn = Mathf.Sin(ang);
                Vector2 dn = d.normalized;
                Vector2 dir = new Vector2(dn.x * cs - dn.y * sn, dn.x * sn + dn.y * cs) * d.magnitude * 0.42f;
                Bolt(rnd, q, q + dir, jitter * 0.7f, Mathf.Max(3, segs / 2), Mathf.Max(2f, R * 0.8f), 0, strength * 0.85f);
            }
        }

        // halo -> blanco según intensidad. mask != null limita a ciertas celdas.
        public void Composite(Canvas c, Color halo, float alphaMul, bool[] mask)
        {
            for (int i = 0; i < I.Length; i++)
            {
                float v = I[i];
                if (v < 0.04f) continue;
                if (mask != null && !mask[i]) continue;
                float t = Mathf.Clamp01((v - 0.55f) / 0.40f);
                t = t * t * (3f - 2f * t);
                Color col = Color.Lerp(halo, Color.white, t);
                c.Over(i % W, i / W, col, Mathf.Clamp01(v * 1.15f) * alphaMul);
            }
        }
    }

    private static float Sm(float a, float b, float x) { float t = Mathf.Clamp01((x - a) / (b - a)); return t * t * (3f - 2f * t); }

    // Aura exterior: distancia al cráneo con ruido
    private static void Aura(Canvas c, Color inner, Color outer, float strength, float R, int seed)
    {
        int n = c.W * c.H;
        float[] dist = new float[n];
        for (int i = 0; i < n; i++) dist[i] = c.px[i].a > 0.5f ? 0f : 9999f;
        for (int y = 0; y < c.H; y++)
            for (int x = 0; x < c.W; x++)
            {
                int i = y * c.W + x; float d = dist[i];
                if (x > 0) d = Mathf.Min(d, dist[i - 1] + 1f);
                if (y > 0) d = Mathf.Min(d, dist[i - c.W] + 1f);
                if (x > 0 && y > 0) d = Mathf.Min(d, dist[i - c.W - 1] + 1.414f);
                if (x < c.W - 1 && y > 0) d = Mathf.Min(d, dist[i - c.W + 1] + 1.414f);
                dist[i] = d;
            }
        for (int y = c.H - 1; y >= 0; y--)
            for (int x = c.W - 1; x >= 0; x--)
            {
                int i = y * c.W + x; float d = dist[i];
                if (x < c.W - 1) d = Mathf.Min(d, dist[i + 1] + 1f);
                if (y < c.H - 1) d = Mathf.Min(d, dist[i + c.W] + 1f);
                if (x < c.W - 1 && y < c.H - 1) d = Mathf.Min(d, dist[i + c.W + 1] + 1.414f);
                if (x > 0 && y < c.H - 1) d = Mathf.Min(d, dist[i + c.W - 1] + 1.414f);
                dist[i] = d;
            }
        for (int y = 0; y < c.H; y++)
            for (int x = 0; x < c.W; x++)
            {
                int i = y * c.W + x;
                float d = dist[i];
                if (c.px[i].a > 0.01f || d > R) continue;
                float nz = ElementalArt.Noise(x * 0.045f, y * 0.045f, seed);
                float a = strength * Mathf.Exp(-d / (R * 0.32f)) * (0.30f + 0.70f * nz);
                if (a < 0.03f) continue;
                Color col = Color.Lerp(outer, inner, Mathf.Exp(-d / (R * 0.22f)));
                c.px[i] = new Color(col.r, col.g, col.b, Mathf.Clamp01(a));
            }
    }

    // Grieta con luz: línea oscura + brillo del acento (sólo sobre hueso)
    private static void Crack(Canvas c, GlowBuf glow, Vector2 start, int steps, float spread)
    {
        Vector2 p = start;
        for (int s = 0; s < steps; s++)
        {
            Vector2 q = new Vector2(p.x + ((float)c.rnd.NextDouble() - 0.5f) * spread, p.y - 2f - (float)c.rnd.NextDouble() * (spread * 0.6f));
            // trazo oscuro de 2 px
            int steps2 = Mathf.CeilToInt((q - p).magnitude);
            for (int i = 0; i <= steps2; i++)
            {
                Vector2 t = Vector2.Lerp(p, q, steps2 == 0 ? 0f : i / (float)steps2);
                int x = Mathf.RoundToInt(t.x), y = Mathf.RoundToInt(t.y);
                for (int dx = 0; dx <= 1; dx++)
                    if (c.In(x + dx, y) && c.bone[y * c.W + x + dx]) c.px[y * c.W + x + dx] = C(0.02f, 0.01f, 0.03f);
            }
            glow.Line(p, new Vector2(q.x + 2f, q.y), 2.5f, 0.85f);
            p = q;
            if (!c.In(Mathf.RoundToInt(p.x), Mathf.RoundToInt(p.y)) || !c.Opaque(Mathf.RoundToInt(p.x), Mathf.RoundToInt(p.y))) break;
        }
    }

    private static Vector2 CrackStart(Canvas c, int k)
    {
        float cx = (c.minX + c.maxX) * 0.5f;
        float w = (c.maxX - c.minX) * 0.32f;
        float x = cx + (((float)c.rnd.NextDouble() - 0.5f) * 2f * w);
        for (int y = c.topY; y > c.botY; y--)
            if (c.In(Mathf.RoundToInt(x), y) && c.bone[y * c.W + Mathf.RoundToInt(x)]) return new Vector2(x, y - 2);
        return new Vector2(cx, c.topY - 8);
    }

    private static void EyeGlowDots(Canvas c, Color tint, bool whiteDots, bool pupilColored)
    {
        foreach (Vector2 e in c.eyes)
        {
            // halo suave dentro de la cavidad
            for (int dy = -22; dy <= 22; dy++)
                for (int dx = -26; dx <= 26; dx++)
                {
                    int x = Mathf.RoundToInt(e.x) + dx, y = Mathf.RoundToInt(e.y) + dy;
                    if (!c.In(x, y) || !c.cav[y * c.W + x]) continue;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    c.Over(x, y, tint, 0.55f * Mathf.Exp(-d / 9f));
                }
            if (whiteDots)
            {
                Dot(c, e.x - 2f, e.y + 3f, 4.6f, Color.white, 1f);
                Dot(c, e.x + 8f, e.y - 5f, 2.4f, C(0.95f, 0.88f, 1f), 1f);
                Dot(c, e.x - 9f, e.y - 3f, 2f, C(0.9f, 0.8f, 1f), 0.95f);
            }
            if (pupilColored)
            {
                Dot(c, e.x, e.y, 4.5f, tint, 1f);
                Dot(c, e.x - 1f, e.y + 1.5f, 2.2f, Color.white, 0.95f);
            }
        }
    }

    private static void Dot(Canvas c, float x, float y, float r, Color col, float a)
    {
        int x0 = Mathf.FloorToInt(x - r), x1 = Mathf.CeilToInt(x + r);
        int y0 = Mathf.FloorToInt(y - r), y1 = Mathf.CeilToInt(y + r);
        for (int py = y0; py <= y1; py++)
            for (int px = x0; px <= x1; px++)
            {
                float dx = px + 0.5f - x, dy = py + 0.5f - y;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                if (d > r) continue;
                c.Over(px, py, col, a * (d > r - 1f ? 0.7f : 1f));
            }
    }

    // Goteo: hilo vertical con gota brillante al final
    private static void Drip(Canvas c, float x, float yTop, int len, Color body, Color hi, int width)
    {
        for (int k = 0; k < len; k++)
        {
            float taper = k > len - 6 ? 0.7f : 1f;
            for (int w = -(width / 2); w <= width / 2; w++)
            {
                Color col = (w < 0 ? hi : body);
                c.Over(Mathf.RoundToInt(x) + w, Mathf.RoundToInt(yTop) - k, col, taper);
            }
        }
        Dot(c, x, yTop - len - 1f, width * 0.95f, body, 1f);
        Dot(c, x - 1f, yTop - len, width * 0.35f, hi, 1f);
    }

    // ------------------------------------------------------------------
    // MALDICIÓN
    // ------------------------------------------------------------------
    private static void DecorateCurse(Canvas c, int level, int seed)
    {
        Color accent = Accent(ElementSkill.Curse);
        EyeGlowDots(c, accent, whiteDots: true, pupilColored: false);

        var glow = new GlowBuf(c.W, c.H);
        int cracks = level == 1 ? 0 : (level == 2 ? 3 : 7);
        for (int k = 0; k < cracks; k++) Crack(c, glow, CrackStart(c, k), 22 + level * 6, 14f);
        glow.Composite(c, C(0.72f, 0.30f, 1f), 0.95f, c.bone);

        float strength = level == 1 ? 0.55f : (level == 2 ? 0.78f : 1f);
        Aura(c, C(0.88f, 0.60f, 1f), C(0.36f, 0.08f, 0.68f), strength, 26f, seed);

        if (level >= 3)
        {
            var flames = new GlowBuf(c.W, c.H);
            for (int k = 0; k < 8; k++)
            {
                float x0 = c.minX + (c.maxX - c.minX) * (0.08f + 0.84f * k / 7f);
                float y0 = c.topY - 12f - (float)c.rnd.NextDouble() * 6f;
                float h = 14f + (float)c.rnd.NextDouble() * 18f;
                flames.Bolt(c.rnd, new Vector2(x0, y0), new Vector2(x0 + ((float)c.rnd.NextDouble() - 0.5f) * 14f, Mathf.Min(c.H - 2f, y0 + h)), 3.5f, 6, 3.5f, 0, 0.8f);
            }
            flames.Composite(c, C(0.62f, 0.25f, 1f), 0.7f, null);
        }
    }

    // ------------------------------------------------------------------
    // ELECTRICIDAD
    // ------------------------------------------------------------------
    private static void DecorateElectric(Canvas c, int level, int seed)
    {
        Color accent = Accent(ElementSkill.Electric);
        EyeGlowDots(c, accent, whiteDots: false, pupilColored: true);

        var glow = new GlowBuf(c.W, c.H);
        int scars = level == 1 ? 1 : (level == 2 ? 2 : 4);
        for (int k = 0; k < scars; k++)
        {
            Vector2 s = CrackStart(c, k);
            Vector2 t = new Vector2(s.x + ((float)c.rnd.NextDouble() - 0.5f) * 40f, s.y - (28f + level * 12f));
            glow.Bolt(c.rnd, s, t, 7f, 9, 3.2f, level >= 2 ? 2 : 1, 0.95f);
        }
        glow.Composite(c, C(0.22f, 0.62f, 1f), 1f, c.bone);

        // Rayos que irradian desde el cráneo
        var outer = new GlowBuf(c.W, c.H);
        int rays = 2 + level * 2;
        for (int k = 0; k < rays; k++)
        {
            float ang = (float)(c.rnd.NextDouble() * Mathf.PI * 2f);
            float cx = (c.minX + c.maxX) * 0.5f, cy = (c.topY + c.botY) * 0.5f;
            float rx = (c.maxX - c.minX) * 0.48f, ry = (c.topY - c.botY) * 0.48f;
            Vector2 a = new Vector2(cx + Mathf.Cos(ang) * rx, cy + Mathf.Sin(ang) * ry);
            float len = 14f + (float)c.rnd.NextDouble() * (8f + level * 6f);
            Vector2 b = new Vector2(Mathf.Clamp(a.x + Mathf.Cos(ang) * len, 2f, c.W - 3f), Mathf.Clamp(a.y + Mathf.Sin(ang) * len, 2f, c.H - 3f));
            outer.Bolt(c.rnd, a, b, 5f, 6, 3f, level >= 2 ? 1 : 0, 0.9f);
        }
        outer.Composite(c, C(0.25f, 0.66f, 1f), 1f, null);

        Aura(c, C(0.65f, 0.92f, 1f), C(0.15f, 0.40f, 0.95f), level == 1 ? 0.35f : (level == 2 ? 0.5f : 0.68f), 20f, seed);
    }

    // ------------------------------------------------------------------
    // VENENO
    // ------------------------------------------------------------------
    private static void DecoratePoison(Canvas c, int level, int seed)
    {
        Color accent = Accent(ElementSkill.Poison);
        EyeGlowDots(c, accent, whiteDots: false, pupilColored: true);
        Color body = C(0.36f, 0.86f, 0.16f), hi = C(0.78f, 1f, 0.55f);

        var glow = new GlowBuf(c.W, c.H);
        int cracks = level == 1 ? 0 : (level == 2 ? 3 : 6);
        for (int k = 0; k < cracks; k++) Crack(c, glow, CrackStart(c, k), 18 + level * 5, 12f);
        glow.Composite(c, C(0.35f, 0.95f, 0.20f), 0.9f, c.bone);

        // Poros que supuran
        int pores = 6 + level * 6;
        for (int k = 0; k < pores; k++)
        {
            int x = c.minX + (int)(c.rnd.NextDouble() * (c.maxX - c.minX));
            int y = c.botY + (int)((0.35 + c.rnd.NextDouble() * 0.6) * (c.topY - c.botY));
            if (!c.In(x, y) || !c.bone[y * c.W + x]) continue;
            Dot(c, x, y, 2.4f, C(0.20f, 0.50f, 0.08f), 1f);
            Dot(c, x, y, 1.2f, accent, 1f);
            if (c.rnd.NextDouble() < 0.5)
                Drip(c, x, y - 2, 5 + c.rnd.Next(0, 5 + level * 3), body, hi, 3);
        }
        // Supuración por cuencas, nariz y dientes
        int eyeDrips = 2 + level;
        foreach (Vector2 e in c.eyes)
            for (int k = 0; k < eyeDrips; k++)
            {
                float dx = (k - (eyeDrips - 1) * 0.5f) * 8f;
                int sy = Mathf.RoundToInt(e.y) - 4;
                // arranca en el borde inferior de la cavidad
                int sx = Mathf.RoundToInt(e.x + dx);
                for (int y = sy; y > sy - 40; y--) { if (!c.In(sx, y) || !c.cav[y * c.W + sx]) { sy = y; break; } }
                Drip(c, sx, sy, 10 + c.rnd.Next(0, 10 + level * 8), body, hi, 3 + (c.rnd.Next(2)));
            }
        for (int k = 0; k < 2 + level * 2; k++)
        {
            int x = c.minX + (int)((0.30 + c.rnd.NextDouble() * 0.45) * (c.maxX - c.minX));
            Drip(c, x, c.botY + 8, 8 + c.rnd.Next(0, 6 + level * 6), body, hi, 3);
        }
        // Burbujas flotantes
        int bubbles = 3 + level * 3;
        for (int k = 0; k < bubbles; k++)
        {
            float x = c.minX - 6f + (float)c.rnd.NextDouble() * (c.maxX - c.minX + 12f);
            float y = c.botY + (float)c.rnd.NextDouble() * (c.topY - c.botY + 20f);
            if (c.Opaque(Mathf.RoundToInt(x), Mathf.RoundToInt(y))) continue;
            float r = 2f + (float)c.rnd.NextDouble() * 3.5f;
            Dot(c, x, y, r, C(0.40f, 0.92f, 0.22f), 0.85f);
            Dot(c, x - r * 0.3f, y + r * 0.3f, r * 0.35f, Color.white, 0.9f);
        }
        Aura(c, C(0.62f, 1f, 0.42f), C(0.12f, 0.55f, 0.10f), level == 1 ? 0.30f : (level == 2 ? 0.45f : 0.62f), 20f, seed);
    }

    // ------------------------------------------------------------------
    // SANGRADO
    // ------------------------------------------------------------------
    private static void DecorateBleed(Canvas c, int level, int seed)
    {
        Color accent = Accent(ElementSkill.Bleed);
        EyeGlowDots(c, accent, whiteDots: false, pupilColored: true);
        Color body = C(0.66f, 0.05f, 0.10f), hi = C(1f, 0.42f, 0.40f);

        var glow = new GlowBuf(c.W, c.H);
        int cracks = level == 1 ? 0 : (level == 2 ? 3 : 7);
        for (int k = 0; k < cracks; k++) Crack(c, glow, CrackStart(c, k), 20 + level * 6, 14f);
        glow.Composite(c, C(1f, 0.15f, 0.18f), 0.9f, c.bone);

        // Lágrimas de sangre
        int tears = 2 + level;
        foreach (Vector2 e in c.eyes)
            for (int k = 0; k < tears; k++)
            {
                float dx = (k - (tears - 1) * 0.5f) * 9f + (float)(c.rnd.NextDouble() - 0.5) * 4f;
                int sx = Mathf.RoundToInt(e.x + dx), sy = Mathf.RoundToInt(e.y) - 4;
                for (int y = sy; y > sy - 40; y--) { if (!c.In(sx, y) || !c.cav[y * c.W + sx]) { sy = y; break; } }
                Drip(c, sx, sy, 14 + c.rnd.Next(0, 14 + level * 10), body, hi, 3 + c.rnd.Next(2));
            }
        // Sangre en los dientes y mandíbula
        for (int k = 0; k < 3 + level * 2; k++)
        {
            int x = c.minX + (int)((0.30 + c.rnd.NextDouble() * 0.45) * (c.maxX - c.minX));
            Drip(c, x, c.botY + 9, 8 + c.rnd.Next(0, 8 + level * 8), body, hi, 3);
        }
        // Salpicaduras
        int splat = 8 + level * 8;
        for (int k = 0; k < splat; k++)
        {
            float x = c.minX - 8f + (float)c.rnd.NextDouble() * (c.maxX - c.minX + 16f);
            float y = c.botY - 6f + (float)c.rnd.NextDouble() * (c.topY - c.botY + 16f);
            if (c.Opaque(Mathf.RoundToInt(x), Mathf.RoundToInt(y))) continue;
            Dot(c, x, y, 1.2f + (float)c.rnd.NextDouble() * 2.4f, body, 0.95f);
        }
        // Charco bajo la mandíbula (nivel 3)
        if (level >= 3)
        {
            float cx = (c.minX + c.maxX) * 0.5f;
            for (int k = 0; k < 7; k++)
                Dot(c, cx + (k - 3) * 14f, c.botY - 10f - (float)c.rnd.NextDouble() * 3f, 5f + (float)c.rnd.NextDouble() * 4f, body, 0.92f);
        }
        Aura(c, C(1f, 0.40f, 0.40f), C(0.55f, 0.02f, 0.08f), level == 1 ? 0.28f : (level == 2 ? 0.42f : 0.60f), 20f, seed);
    }
}
