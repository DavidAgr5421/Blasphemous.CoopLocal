using UnityEngine;
using UnityEngine.UI;

namespace Blasphemous.CoopLocal;

/// <summary>
/// Estilo visual compartido de los menús del mod (pre-partida, unirse, pausa por jugador, debug F8).
/// Genera en runtime sprites pixel-art con la estética de los menús del juego: fondo oscuro con
/// viñeta y grano, marco dorado doble con esquinas biseladas (9-slice), ornamentos de esquina y
/// divisores con diamante central. Todo procedural y cacheado; los elementos decorativos nunca
/// capturan raycasts ni alteran los colores/outlines dinámicos que cada menú ya gestiona.
/// </summary>
internal static class CoopUiStyle
{
    internal static readonly Color Gold = new Color(0.80f, 0.62f, 0.26f, 1f);
    internal static readonly Color GoldDim = new Color(0.52f, 0.40f, 0.18f, 1f);
    internal static readonly Color GoldBright = new Color(1f, 0.88f, 0.52f, 1f);
    internal static readonly Color Ink = new Color(0.03f, 0.02f, 0.02f, 1f);

    private static Sprite bgPanel, bgOverlay, frame, frameThin, corner, divider, cardBg;

    // ------------------------------------------------------------------
    // API pública
    // ------------------------------------------------------------------

    /// <summary>Fondo a pantalla completa: viñeta oscura con ligera calidez.</summary>
    internal static void StyleOverlay(Image overlay)
    {
        if (overlay == null) return;
        overlay.sprite = BgOverlay();
        overlay.type = Image.Type.Simple;
        overlay.color = Color.white;
        overlay.raycastTarget = true;
    }

    /// <summary>Panel principal: fondo texturizado, marco dorado 9-slice y 4 ornamentos de esquina.</summary>
    internal static void StylePanel(GameObject panel, bool withCorners = true)
    {
        if (panel == null) return;
        Image img = panel.GetComponent<Image>();
        if (img != null)
        {
            img.sprite = BgPanel();
            img.type = Image.Type.Simple;
            img.color = Color.white;
        }
        // El Outline plano anterior queda redundante con el marco: se atenúa.
        Outline old = panel.GetComponent<Outline>();
        if (old != null) old.effectColor = new Color(0f, 0f, 0f, 0.65f);

        AddFrame(panel.transform, "CoopFrame", Frame(), 0f);
        AddFrame(panel.transform, "CoopFrameInner", FrameThin(), 14f);

        if (!withCorners) return;
        AddCorner(panel.transform, new Vector2(0f, 1f), new Vector2(-4f, 4f), new Vector2(1f, 1f));
        AddCorner(panel.transform, new Vector2(1f, 1f), new Vector2(4f, 4f), new Vector2(-1f, 1f));
        AddCorner(panel.transform, new Vector2(0f, 0f), new Vector2(-4f, -4f), new Vector2(1f, -1f));
        AddCorner(panel.transform, new Vector2(1f, 0f), new Vector2(4f, -4f), new Vector2(-1f, -1f));
    }

    /// <summary>Sub-panel (tarjeta, banner, diálogo): degradado + biselado fino; respeta img.color como tinte.</summary>
    internal static void StyleCard(GameObject card)
    {
        if (card == null) return;
        Image img = card.GetComponent<Image>();
        if (img != null)
        {
            img.sprite = CardBg();
            img.type = Image.Type.Sliced;
        }
        AddFrame(card.transform, "CoopCardBevel", FrameThin(), 0f);
    }

    /// <summary>Divisor ornamental horizontal (línea dorada con diamante central).</summary>
    internal static GameObject AddDivider(Transform parent, Vector2 anchoredPos, float width)
    {
        var go = new GameObject("CoopDivider", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = new Vector2(width, 16f);
        var img = go.AddComponent<Image>();
        img.sprite = Divider();
        img.type = Image.Type.Sliced;
        img.raycastTarget = false;
        return go;
    }

    /// <summary>Añade sombra gruesa y contorno dorado oscuro a un título para darle peso.</summary>
    internal static void StyleTitle(Text title)
    {
        if (title == null) return;
        title.color = GoldBright;
        var outline = title.GetComponent<Outline>() ?? title.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(0.25f, 0.12f, 0.02f, 0.95f);
        outline.effectDistance = new Vector2(2f, -2f);
        var shadow = title.gameObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.8f);
        shadow.effectDistance = new Vector2(0f, -4f);
    }

    // ------------------------------------------------------------------
    // Construcción de elementos
    // ------------------------------------------------------------------
    private static void AddFrame(Transform parent, string name, Sprite sprite, float inset)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(inset, inset);
        rt.offsetMax = new Vector2(-inset, -inset);
        var img = go.AddComponent<Image>();
        img.sprite = sprite;
        img.type = Image.Type.Sliced;
        img.raycastTarget = false;
        go.transform.SetAsLastSibling();
    }

    private static void AddCorner(Transform parent, Vector2 anchor, Vector2 offset, Vector2 flip)
    {
        var go = new GameObject("CoopCorner", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = new Vector2(anchor.x, anchor.y);
        rt.anchoredPosition = offset;
        rt.sizeDelta = new Vector2(48f, 48f);
        rt.localScale = new Vector3(flip.x, flip.y, 1f);
        var img = go.AddComponent<Image>();
        img.sprite = Corner();
        img.raycastTarget = false;
    }

    // ------------------------------------------------------------------
    // Sprites procedurales
    // ------------------------------------------------------------------
    private static float Hash(int x, int y)
    {
        unchecked
        {
            int h = x * 374761393 + y * 668265263;
            h = (h ^ (h >> 13)) * 1274126177;
            h ^= h >> 16;
            return (h & 0xFFFF) / 65535f;
        }
    }

    private static Sprite Make(Texture2D tex, Vector4? border = null, float ppu = 100f)
    {
        tex.Apply();
        var sp = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), ppu, 0,
            SpriteMeshType.FullRect, border ?? Vector4.zero);
        Object.DontDestroyOnLoad(tex);
        Object.DontDestroyOnLoad(sp);
        return sp;
    }

    private static Texture2D NewTex(int w, int h, FilterMode mode)
    {
        var t = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = mode, wrapMode = TextureWrapMode.Clamp };
        var clear = new Color[w * h];
        t.SetPixels(clear);
        return t;
    }

    private static Sprite BgPanel()
    {
        if (bgPanel != null) return bgPanel;
        const int W = 256, H = 256;
        var t = NewTex(W, H, FilterMode.Bilinear);
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                float nx = x / (W - 1f) * 2f - 1f, ny = y / (H - 1f) * 2f - 1f;
                float vig = 1f - Mathf.Clamp01((nx * nx + ny * ny) * 0.45f);     // oscurece bordes
                float top = 0.55f + 0.45f * (y / (H - 1f));                      // más claro arriba
                float grain = (Hash(x, y) - 0.5f) * 0.05f;
                float l = (0.060f + 0.060f * vig) * (0.75f + 0.25f * top) + grain * 0.5f;
                t.SetPixel(x, y, new Color(l * 1.05f, l * 0.97f, l * 0.92f, 0.985f));
            }
        return bgPanel = Make(t);
    }

    private static Sprite BgOverlay()
    {
        if (bgOverlay != null) return bgOverlay;
        const int W = 128, H = 128;
        var t = NewTex(W, H, FilterMode.Bilinear);
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                float nx = x / (W - 1f) * 2f - 1f, ny = y / (H - 1f) * 2f - 1f;
                float d = Mathf.Clamp01(Mathf.Sqrt(nx * nx + ny * ny) / 1.35f);
                float a = Mathf.Lerp(0.88f, 0.98f, d * d);
                float tint = Mathf.Lerp(0.045f, 0.010f, d);
                t.SetPixel(x, y, new Color(tint * 1.1f, tint * 0.8f, tint * 0.7f, a));
            }
        return bgOverlay = Make(t);
    }

    private static Sprite CardBg()
    {
        if (cardBg != null) return cardBg;
        const int W = 32, H = 32;
        var t = NewTex(W, H, FilterMode.Bilinear);
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                float g = 0.78f + 0.22f * (y / (H - 1f));
                float edge = Mathf.Min(Mathf.Min(x, W - 1 - x), Mathf.Min(y, H - 1 - y));
                float e = Mathf.Clamp01(edge / 6f);
                float l = g * (0.82f + 0.18f * e);
                t.SetPixel(x, y, new Color(l, l, l, 1f));
            }
        return cardBg = Make(t, new Vector4(8, 8, 8, 8));
    }

    // Marco principal 48x48, borde 16: línea oscura, doble línea dorada, muescas de esquina.
    private static Sprite Frame()
    {
        if (frame != null) return frame;
        const int N = 48;
        var t = NewTex(N, N, FilterMode.Point);
        Color gold = Gold, dim = GoldDim, bright = GoldBright, ink = Ink;

        void P(int x, int y, Color c) { if (x >= 0 && y >= 0 && x < N && y < N) t.SetPixel(x, y, c); }
        // Perímetro con 4 anillos: 0 tinta, 1-2 dorado, 3 tinta, 5 dorado fino
        for (int i = 0; i < N; i++)
        {
            for (int ring = 0; ring < 7; ring++)
            {
                Color c;
                switch (ring)
                {
                    case 0: c = ink; break;
                    case 1: c = bright; break;
                    case 2: c = gold; break;
                    case 3: c = dim; break;
                    case 4: c = ink; break;
                    case 6: c = dim; break;
                    default: continue;
                }
                P(i, ring, c); P(i, N - 1 - ring, c); P(ring, i, c); P(N - 1 - ring, i, c);
            }
        }
        // Escuadras gruesas en esquinas + gema
        int[][] corners = { new[] { 0, 0, 1, 1 }, new[] { N - 1, 0, -1, 1 }, new[] { 0, N - 1, 1, -1 }, new[] { N - 1, N - 1, -1, -1 } };
        for (int k = 5; k < 13; k++)
        {
            foreach (int[] q in corners)
            {
                int cx = q[0], cy = q[1], sx = q[2], sy = q[3];
                P(cx + sx * k, cy + sy * 4, gold); P(cx + sx * 4, cy + sy * k, gold);
                P(cx + sx * k, cy + sy * 5, dim); P(cx + sx * 5, cy + sy * k, dim);
            }
        }
        foreach (int[] q in corners)
        {
            int cx = q[0], cy = q[1], sx = q[2], sy = q[3];
            int gx = cx + sx * 8, gy = cy + sy * 8;
            P(gx, gy, bright);
            P(gx + 1, gy, gold); P(gx - 1, gy, gold); P(gx, gy + 1, gold); P(gx, gy - 1, gold);
        }
        return frame = Make(t, new Vector4(16, 16, 16, 16), 100f);
    }

    // Marco fino 24x24, borde 8: una línea dorada tenue con acabado oscuro.
    private static Sprite FrameThin()
    {
        if (frameThin != null) return frameThin;
        const int N = 24;
        var t = NewTex(N, N, FilterMode.Point);
        void P(int x, int y, Color c) { if (x >= 0 && y >= 0 && x < N && y < N) t.SetPixel(x, y, c); }
        for (int i = 0; i < N; i++)
        {
            Color a = new Color(GoldDim.r, GoldDim.g, GoldDim.b, 0.85f);
            Color d = new Color(0f, 0f, 0f, 0.55f);
            P(i, 0, a); P(i, N - 1, a); P(0, i, a); P(N - 1, i, a);
            P(i, 1, d); P(i, N - 2, d); P(1, i, d); P(N - 2, i, d);
        }
        // pequeña muesca en esquinas
        P(0, 0, Color.clear); P(N - 1, 0, Color.clear); P(0, N - 1, Color.clear); P(N - 1, N - 1, Color.clear);
        return frameThin = Make(t, new Vector4(8, 8, 8, 8), 100f);
    }

    // Ornamento de esquina 24x24 (esquina superior-izquierda; se refleja con localScale).
    private static Sprite Corner()
    {
        if (corner != null) return corner;
        const int N = 24;
        var t = NewTex(N, N, FilterMode.Point);
        void P(int x, int y, Color c) { if (x >= 0 && y >= 0 && x < N && y < N) t.SetPixel(x, N - 1 - y, c); } // y hacia abajo

        // Arco concéntrico con espiral y punta
        for (int a = 0; a <= 90; a += 2)
        {
            float rad = a * Mathf.Deg2Rad;
            int x = Mathf.RoundToInt(2f + Mathf.Cos(rad) * 16f);
            int y = Mathf.RoundToInt(2f + Mathf.Sin(rad) * 16f);
            P(x, y, Gold);
            int x2 = Mathf.RoundToInt(2f + Mathf.Cos(rad) * 12f);
            int y2 = Mathf.RoundToInt(2f + Mathf.Sin(rad) * 12f);
            P(x2, y2, GoldDim);
        }
        for (int i = 0; i < 20; i++) { P(i, 2, i < 14 ? Gold : GoldDim); P(2, i, i < 14 ? Gold : GoldDim); }
        // diamante
        int dx = 7, dy = 7;
        P(dx, dy, GoldBright);
        P(dx + 1, dy, Gold); P(dx - 1, dy, Gold); P(dx, dy + 1, Gold); P(dx, dy - 1, Gold);
        P(dx + 2, dy, GoldDim); P(dx, dy + 2, GoldDim);
        // espinas laterales
        for (int i = 0; i < 4; i++) { P(20 + i % 2, 3 + i, GoldDim); P(3 + i, 20 + i % 2, GoldDim); }
        return corner = Make(t, null, 100f);
    }

    // Divisor 128x16, borde 40 horizontal: líneas que se desvanecen + diamante central.
    private static Sprite Divider()
    {
        if (divider != null) return divider;
        const int W = 128, H = 16;
        var t = NewTex(W, H, FilterMode.Point);
        for (int x = 0; x < W; x++)
        {
            float fade = Mathf.Clamp01(Mathf.Min(x, W - 1 - x) / 28f);
            Color a = new Color(Gold.r, Gold.g, Gold.b, fade);
            Color d = new Color(GoldDim.r, GoldDim.g, GoldDim.b, fade * 0.8f);
            t.SetPixel(x, 8, a); t.SetPixel(x, 7, d);
        }
        int cx = W / 2, cy = 8;
        for (int r = 0; r <= 6; r++)
        {
            for (int k = -r; k <= r; k++)
            {
                int ay = r - Mathf.Abs(k);
                Color c = r >= 5 ? GoldDim : (r >= 3 ? Gold : GoldBright);
                t.SetPixel(cx + k, cy + (6 - r) , Color.clear);
            }
        }
        // diamante sólido
        for (int dy = -6; dy <= 6; dy++)
        {
            int half = 6 - Mathf.Abs(dy);
            for (int dx = -half; dx <= half; dx++)
            {
                int d = Mathf.Abs(dx) + Mathf.Abs(dy);
                Color c = d >= 6 ? Ink : (d >= 4 ? GoldDim : (d >= 2 ? Gold : GoldBright));
                t.SetPixel(cx + dx, cy + dy, c);
            }
        }
        // diamantes pequeños flanqueando
        foreach (int off in new[] { -22, 22 })
        {
            for (int dy = -2; dy <= 2; dy++)
            {
                int half = 2 - Mathf.Abs(dy);
                for (int dx = -half; dx <= half; dx++) t.SetPixel(cx + off + dx, cy + dy, dx == 0 && dy == 0 ? GoldBright : Gold);
            }
        }
        return divider = Make(t, new Vector4(40, 0, 40, 0), 100f);
    }
}
