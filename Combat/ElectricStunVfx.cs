using System;
using System.Collections.Generic;
using Gameplay.GameControllers.Entities;
using UnityEngine;

namespace Blasphemous.CoopLocal;

/// <summary>
/// Aturdimiento eléctrico: relámpagos irregulares azul/blanco con halo suave que RECORREN LA SILUETA del
/// enemigo (se calcula la máscara de su sprite actual y los rayos "trepan" por el contorno).
/// - Aparece al 100% de opacidad y cae rápido hacia un brillo tenue para no tapar la vista del enemigo.
/// - Re-descargas ocasionales y más suaves mientras dure el aturdimiento.
/// - Es un objeto de escena propio (no hijo del enemigo): no hereda flips ni lo toca el controlador de estados.
/// - Un único SpriteRenderer cuya textura se regenera en cada descarga (sin Shader.Find ni LineRenderer).
/// </summary>
internal sealed class ElectricStunVfx : MonoBehaviour
{
    private const int Pad = 8;
    private const float FadeOutTime = 0.25f;
    private const float StartAlpha = 1f;
    private const float RestAlpha = 0.18f;
    private const float HoldTime = 1.0f;          // opacidad total sostenida (con parpadeo de descargas) antes de atenuar
    private const float DecayRate = 3.0f;         // atenuación suave posterior (~1 s hasta el brillo tenue)
    private const float MinLifetime = 1.8f;       // el efecto dura al menos HoldTime + atenuación

    private static readonly Color Halo = new Color(0.30f, 0.72f, 1f, 1f);

    private Enemy target;
    private SpriteRenderer bodySr;
    private Sprite srcSprite;
    private float duration;
    private float age;

    private int W, H, scale, TW, TH;
    private bool[] mask;
    private List<int> edge;
    private Texture2D tex;
    private Sprite boltSprite;
    private SpriteRenderer boltSr;
    private float[] inten;
    private Color32[] pixels;

    private float sinceStrike;
    private float nextStrikeIn;
    private float strikePeak = StartAlpha;
    private System.Random rnd = new System.Random();

    internal void Init(Enemy t, float seconds, float topHeight)
    {
        target = t;
        duration = Mathf.Max(seconds, MinLifetime);
        bodySr = t.SpriteRenderer;
        if (bodySr == null || bodySr.sprite == null)
        {
            DashParryDebugLog.Log("[ElecVfx] Enemigo sin sprite; no se crea la animación");
            Destroy(gameObject);
            return;
        }

        var go = new GameObject("CoopBolts");
        go.transform.SetParent(transform, false);
        boltSr = go.AddComponent<SpriteRenderer>();

        Rebuild(bodySr.sprite);
        Strike(StartAlpha);
        ApplyTransformAndColor();
        DashParryDebugLog.Log($"[ElecVfx] Aturdimiento eléctrico iniciado en {target.name} ({duration:0.0}s, sprite {W - 2 * Pad}x{H - 2 * Pad}, borde {edge.Count}px)");
    }

    // ---- Máscara de silueta del sprite actual ----
    private void Rebuild(Sprite sp)
    {
        srcSprite = sp;
        Rect r = sp.textureRect;
        int w = Mathf.Max(1, Mathf.RoundToInt(r.width)), h = Mathf.Max(1, Mathf.RoundToInt(r.height));
        W = w + Pad * 2; H = h + Pad * 2;
        mask = new bool[W * H];

        bool ok = false;
        try
        {
            Texture2D src = sp.texture;
            RenderTexture rt = RenderTexture.GetTemporary(src.width, src.height, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(src, rt);
            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = rt;
            var read = new Texture2D(w, h, TextureFormat.RGBA32, false);
            read.ReadPixels(new Rect(r.x, r.y, w, h), 0, 0);
            read.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            Color[] px = read.GetPixels();
            Destroy(read);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    if (px[y * w + x].a > 0.3f) mask[(y + Pad) * W + (x + Pad)] = true;
            ok = true;
        }
        catch (Exception ex)
        {
            DashParryDebugLog.Log("[ElecVfx] No se pudo leer la silueta, se usa elipse: " + ex.Message);
        }
        if (!ok)
        {
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float ex = (x - w * 0.5f) / (w * 0.5f), ey = (y - h * 0.5f) / (h * 0.5f);
                    if (ex * ex + ey * ey <= 1f) mask[(y + Pad) * W + (x + Pad)] = true;
                }
        }

        edge = new List<int>();
        for (int y = 1; y < H - 1; y++)
            for (int x = 1; x < W - 1; x++)
            {
                int i = y * W + x;
                if (!mask[i]) continue;
                if (!mask[i - 1] || !mask[i + 1] || !mask[i - W] || !mask[i + W]) edge.Add(i);
            }
        if (edge.Count == 0) edge.Add((H / 2) * W + W / 2);

        scale = Mathf.Max(W, H) > 200 ? 1 : 2;
        TW = W * scale; TH = H * scale;
        if (tex != null) Destroy(tex);
        if (boltSprite != null) Destroy(boltSprite);
        tex = new Texture2D(TW, TH, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        pixels = new Color32[TW * TH];
        inten = new float[TW * TH];
        Vector2 pivot = new Vector2((sp.pivot.x + Pad) / W, (sp.pivot.y + Pad) / H);
        boltSprite = Sprite.Create(tex, new Rect(0, 0, TW, TH), pivot, sp.pixelsPerUnit * scale);
        boltSr.sprite = boltSprite;
    }

    // ---- Descarga: rayos que trepan por el contorno ----
    private void Strike(float peak)
    {
        strikePeak = peak;
        sinceStrike = 0f;
        nextStrikeIn = age < HoldTime ? 0.12f : 0.55f + (float)rnd.NextDouble() * 0.5f;
        Array.Clear(inten, 0, inten.Length);

        int bolts = 3 + rnd.Next(0, 2);
        for (int k = 0; k < bolts; k++)
        {
            // Arranque en el contorno, preferentemente en la parte alta
            int best = edge[rnd.Next(edge.Count)];
            for (int t = 0; t < 6; t++)
            {
                int c = edge[rnd.Next(edge.Count)];
                if (c / W > best / W) best = c;
            }
            Vector2 cur = new Vector2(best % W + 0.5f, best / W + 0.5f);
            Vector2 heading = new Vector2((rnd.Next(2) == 0 ? -1f : 1f) * (0.25f + (float)rnd.NextDouble() * 0.5f), -1f);
            Crawl(cur, heading, 26 + rnd.Next(0, 16), 1f, 1);
        }
        // Un arco cruzando el cuerpo para dar volumen
        if (edge.Count > 8)
        {
            Vector2 a = EdgePoint(edge[rnd.Next(edge.Count)]);
            Vector2 b2 = EdgePoint(edge[rnd.Next(edge.Count)]);
            ArcLine(a, b2, 0.8f);
        }
        Upload();
    }

    private Vector2 EdgePoint(int i) { return new Vector2(i % W + 0.5f, i / W + 0.5f); }

    private void Crawl(Vector2 cur, Vector2 heading, int steps, float strength, int branchesLeft)
    {
        const float stepLen = 4f;
        for (int s = 0; s < steps; s++)
        {
            Vector2 dir = (heading + new Vector2(((float)rnd.NextDouble() - 0.5f) * 0.9f, ((float)rnd.NextDouble() - 0.5f) * 0.5f)).normalized;
            Vector2 target2 = cur + dir * stepLen;

            // Imán a la silueta: el punto de contorno más cercano al destino
            int bi = -1; float bd = stepLen * stepLen * 3.2f;
            for (int e = 0; e < edge.Count; e += 1)
            {
                Vector2 p = EdgePoint(edge[e]);
                float d = (p - target2).sqrMagnitude;
                if (d < bd) { bd = d; bi = edge[e]; }
            }
            Vector2 next = bi >= 0 ? EdgePoint(bi) : target2;
            // irregularidad
            Vector2 perp = new Vector2(-dir.y, dir.x);
            next += perp * (((float)rnd.NextDouble() - 0.5f) * 2.2f);

            DrawLine(cur, next, strength);

            if (branchesLeft > 0 && rnd.NextDouble() < 0.10)
            {
                float ang = ((float)rnd.NextDouble() * 0.8f + 0.5f) * (rnd.Next(2) == 0 ? 1f : -1f);
                Vector2 bh = new Vector2(heading.x * Mathf.Cos(ang) - heading.y * Mathf.Sin(ang), heading.x * Mathf.Sin(ang) + heading.y * Mathf.Cos(ang));
                Crawl(next, bh, 3 + rnd.Next(0, 4), strength * 0.8f, 0);
            }

            cur = next;
            if (cur.y < Pad + 1f) break;
        }
    }

    private void ArcLine(Vector2 a, Vector2 b, float strength)
    {
        int segs = 7;
        Vector2 prev = a;
        Vector2 d = b - a;
        Vector2 perp = new Vector2(-d.y, d.x).normalized;
        for (int s = 1; s <= segs; s++)
        {
            Vector2 p = a + d * (s / (float)segs);
            if (s < segs) p += perp * (((float)rnd.NextDouble() - 0.5f) * 5f);
            DrawLine(prev, p, strength);
            prev = p;
        }
    }

    // ---- Dibujo en el buffer de intensidad (resolución scale x) ----
    private void DrawLine(Vector2 a, Vector2 b, float strength)
    {
        float len = (b - a).magnitude * scale;
        int steps = Mathf.Max(1, Mathf.CeilToInt(len * 1.5f));
        float R = scale == 2 ? 4.2f : 3.0f;
        for (int i = 0; i <= steps; i++)
        {
            Vector2 p = Vector2.Lerp(a, b, i / (float)steps) * scale;
            Splat(p.x, p.y, R, strength);
        }
    }

    private void Splat(float x, float y, float R, float strength)
    {
        int x0 = Mathf.FloorToInt(x - R), x1 = Mathf.CeilToInt(x + R);
        int y0 = Mathf.FloorToInt(y - R), y1 = Mathf.CeilToInt(y + R);
        float coreR = scale == 2 ? 1.3f : 0.95f;
        for (int py = y0; py <= y1; py++)
            for (int px = x0; px <= x1; px++)
            {
                if (px < 0 || py < 0 || px >= TW || py >= TH) continue;
                float dx = px + 0.5f - x, dy = py + 0.5f - y;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                if (d > R) continue;
                float k = d < coreR ? 1f : Mathf.Exp(-(d - coreR) * (scale == 2 ? 0.7f : 0.9f));
                k *= strength;
                int idx = py * TW + px;
                if (k > inten[idx]) inten[idx] = k;
            }
    }

    private void Upload()
    {
        for (int i = 0; i < inten.Length; i++)
        {
            float v = inten[i];
            if (v < 0.04f) { pixels[i] = new Color32(0, 0, 0, 0); continue; }
            float t = Mathf.Clamp01((v - 0.55f) / 0.40f);
            t = t * t * (3f - 2f * t);
            Color c = Color.Lerp(Halo, Color.white, t);
            byte a = (byte)(Mathf.Clamp01(v * 1.15f) * 255f);
            pixels[i] = new Color32((byte)(c.r * 255f), (byte)(c.g * 255f), (byte)(c.b * 255f), a);
        }
        tex.SetPixels32(pixels);
        tex.Apply(false);
    }

    private void Update()
    {
        try { Tick(Time.deltaTime); }
        catch (Exception ex)
        {
            DashParryDebugLog.Log("[ElecVfx] Error ElectricStunVfx: " + ex);
            Destroy(gameObject);
        }
    }

    private void Tick(float dt)
    {
        if (target == null || target.Status == null || target.Status.Dead || bodySr == null
            || Execution_OnUse_Patch.IsEnemyBeingExecuted(target))
        {
            Destroy(gameObject);
            return;
        }
        age += dt;
        if (age >= duration)
        {
            Destroy(gameObject);
            return;
        }

        if (bodySr.sprite != null && bodySr.sprite != srcSprite)
        {
            Rebuild(bodySr.sprite);
            Strike(strikePeak * 0.8f);
        }

        sinceStrike += dt;
        nextStrikeIn -= dt;
        if (age < HoldTime)
        {
            // Fase visible: las descargas se renuevan rápido (parpadeo vivo) a opacidad total
            if (nextStrikeIn <= 0f) { Strike(StartAlpha); nextStrikeIn = 0.11f + (float)rnd.NextDouble() * 0.09f; }
        }
        else if (nextStrikeIn <= 0f && age < duration - 0.4f)
        {
            Strike(0.5f);   // re-descargas más suaves tras la fase visible
        }
        ApplyTransformAndColor();
    }

    private void ApplyTransformAndColor()
    {
        Transform bt = bodySr.transform;
        boltSr.transform.SetPositionAndRotation(bt.position, bt.rotation);
        boltSr.transform.localScale = bt.lossyScale;
        boltSr.flipX = bodySr.flipX;
        boltSr.flipY = bodySr.flipY;
        boltSr.sortingLayerID = bodySr.sortingLayerID;
        boltSr.sortingOrder = bodySr.sortingOrder + 12;

        // 100% al descargar y caída rápida a un brillo tenue que no estorba la visión
        float a;
        if (age < HoldTime)
        {
            a = StartAlpha;                                   // 100% durante al menos 1 s
        }
        else
        {
            // atenuación suave desde 100% hacia el brillo tenue; las re-descargas la elevan brevemente
            float baseA = Mathf.Lerp(RestAlpha, StartAlpha, Mathf.Exp(-(age - HoldTime) * DecayRate));
            float strikeA = Mathf.Lerp(RestAlpha, strikePeak, Mathf.Exp(-sinceStrike * 9f));
            a = Mathf.Max(baseA, strikeA);
        }
        float fadeOut = age > duration - FadeOutTime ? Mathf.Clamp01((duration - age) / FadeOutTime) : 1f;
        boltSr.color = new Color(1f, 1f, 1f, a * fadeOut);
    }

    private void OnDestroy()
    {
        if (tex != null) Destroy(tex);
        if (boltSprite != null) Destroy(boltSprite);
    }
}
