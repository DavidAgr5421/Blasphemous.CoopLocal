using System;
using System.Collections.Generic;
using Framework.FrameworkCore;
using Framework.Managers;
using Gameplay.GameControllers.Entities;
using UnityEngine;

namespace Blasphemous.CoopLocal;

/// <summary>
/// VFX procedurales de efectos elementales (electricidad, sangrado, veneno).
/// Todo se genera en runtime: sprites Texture2D 32 ppu con FilterMode.Point y materiales Sprites/Default.
/// Ninguna llamada debe romper el juego: cada entrada y cada Update van protegidos con try/catch.
/// </summary>
internal static class CoopElementalVfx
{
    private const int BurstSortingOrder = 100;

    private static readonly Color CrimsonBright = new Color(0.78f, 0.04f, 0.06f, 1f);
    private static readonly Color CrimsonDark = new Color(0.42f, 0.02f, 0.04f, 1f);
    private static readonly Color CrimsonRing = new Color(0.86f, 0.08f, 0.14f, 0.95f);
    private static readonly Color CrimsonFlash = new Color(0.40f, 0.00f, 0.03f, 0.85f);
    private static readonly Color ToxicRing = new Color(0.30f, 1f, 0.35f, 0.60f);
    private static readonly Color ToxicBubble = new Color(0.35f, 1f, 0.40f, 0.90f);
    private static readonly Color ToxicDrip = new Color(0.20f, 0.60f, 0.15f, 0.90f);

    private static Sprite circleSprite;
    private static Sprite squareSprite;
    private static Sprite ringSprite;
    private static Sprite bubbleSprite;
    private static Material lineMaterial;

    internal static Material LineMaterial
    {
        get
        {
            if (lineMaterial == null) lineMaterial = new Material(Shader.Find("Sprites/Default"));
            return lineMaterial;
        }
    }

    // ---------------------------------------------------------------------
    // API publica
    // ---------------------------------------------------------------------

    internal static void PlayElectricStun(Enemy target, float seconds, float topY)
    {
        try
        {
            if (target == null || seconds <= 0f) return;
            // Raíz de escena (no hijo del enemigo): evita herencia de flip y que el controlador de
            // estados (que tiñe todos los SpriteRenderer hijos del enemigo) toque el efecto.
            var go = new GameObject("CoopElectricStunVfx");
            go.transform.position = target.transform.position;
            go.AddComponent<ElectricStunVfx>().Init(target, seconds, topY);
        }
        catch (Exception ex)
        {
            DashParryDebugLog.Log("[ElecVfx] ERROR PlayElectricStun: " + ex);
        }
    }

    // Chispas de relámpago por cada golpe del Corazón del Relámpago (1-2 variantes de la animación).
    private static readonly System.Collections.Generic.Dictionary<int, float> lastSparkTime = new System.Collections.Generic.Dictionary<int, float>();

    internal static void PlayElectricSpark(Enemy target, float topY)
    {
        try
        {
            if (target == null || !ElectricSparkSprites.Ensure()) return;
            int id = target.GetInstanceID();
            float now = Time.time;
            if (lastSparkTime.TryGetValue(id, out float last) && now - last < 0.07f) return;
            lastSparkTime[id] = now;

            float h = Mathf.Clamp(topY, 0.8f, 4f);
            float size = Mathf.Clamp(h / 1.8f, 0.7f, 1.5f) * 0.9f;

            Vector3 o1 = new Vector3(UnityEngine.Random.Range(-0.35f, 0.35f), h * UnityEngine.Random.Range(0.35f, 0.65f), 0f);
            var go1 = new GameObject("CoopElectricSpark");
            go1.AddComponent<ElectricSparkAnim>().Init(target, o1, UnityEngine.Random.Range(0, 3), size);

            if (UnityEngine.Random.value < 0.55f)
            {
                Vector3 o2 = new Vector3(UnityEngine.Random.Range(-0.5f, 0.5f), h * UnityEngine.Random.Range(0.25f, 0.8f), 0f);
                var go2 = new GameObject("CoopElectricSpark");
                go2.AddComponent<ElectricSparkAnim>().Init(target, o2, UnityEngine.Random.Range(0, 3), size * 0.85f);
            }
        }
        catch (Exception ex)
        {
            DashParryDebugLog.Log("[ElecSpark] ERROR PlayElectricSpark: " + ex);
        }
    }

    internal static void PlayBleedBurst(Vector3 center, float scaleHint)
    {
        try
        {
            float k = Mathf.Max(0.1f, scaleHint);
            Sprite sq = SquareSprite();
            for (int i = 0; i < 18; i++)
            {
                float ang = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
                float spd = UnityEngine.Random.Range(2f, 6f) * k;
                Vector3 dir = new Vector3(Mathf.Cos(ang), Mathf.Sin(ang), 0f);
                Color c = (i % 2 == 0) ? CrimsonBright : CrimsonDark;
                VfxParticle p = SpawnParticle(sq, center, c, BurstSortingOrder, UnityEngine.Random.Range(0.5f, 0.8f));
                p.Velocity = dir * spd;
                p.Gravity = 9f;
                p.StartScale = Vector3.one * k;
                p.EndScale = Vector3.one * 0.5f * k;
            }

            VfxParticle ring = SpawnParticle(RingSprite(), center, CrimsonRing, BurstSortingOrder, 0.25f);
            ring.StartScale = Vector3.one * 0.2f * k;
            ring.EndScale = Vector3.one * 1.6f * k;

            VfxParticle disc = SpawnParticle(CircleSprite(), center, CrimsonFlash, BurstSortingOrder - 1, 0.12f);
            disc.StartScale = Vector3.one * 1.0f * k;
            disc.EndScale = Vector3.one * 1.6f * k;
        }
        catch (Exception ex)
        {
            DashParryDebugLog.LogVerbose("[ElecVfx] Error PlayBleedBurst: " + ex.Message);
        }
    }

    internal static void PlayPoisonBurst(Vector3 center, float scaleHint)
    {
        try
        {
            float k = Mathf.Max(0.1f, scaleHint);
            Sprite bubble = BubbleSprite();
            for (int i = 0; i < 14; i++)
            {
                Vector3 offset = new Vector3(UnityEngine.Random.Range(-0.25f, 0.25f), UnityEngine.Random.Range(-0.15f, 0.15f), 0f) * k;
                VfxParticle p = SpawnParticle(bubble, center + offset, ToxicBubble, BurstSortingOrder, UnityEngine.Random.Range(0.7f, 1.1f));
                p.Velocity = new Vector3(UnityEngine.Random.Range(-0.3f, 0.3f), UnityEngine.Random.Range(0.6f, 1.2f), 0f) * k;
                p.Gravity = 0f;
                p.WobbleAmp = UnityEngine.Random.Range(0.15f, 0.3f) * k;
                p.WobbleFreq = UnityEngine.Random.Range(7f, 11f);
                p.WobblePhase = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
                p.StartScale = Vector3.one * 0.8f * k;
                p.EndScale = Vector3.one * 1.1f * k;
            }

            VfxParticle cloud = SpawnParticle(RingSprite(), center, ToxicRing, BurstSortingOrder, 0.35f);
            cloud.StartScale = Vector3.one * 0.3f * k;
            cloud.EndScale = Vector3.one * 2.2f * k;

            Sprite sq = SquareSprite();
            for (int i = 0; i < 6; i++)
            {
                Vector3 offset = new Vector3(UnityEngine.Random.Range(-0.3f, 0.3f), UnityEngine.Random.Range(0.05f, 0.3f), 0f) * k;
                VfxParticle d = SpawnParticle(sq, center + offset, ToxicDrip, BurstSortingOrder, UnityEngine.Random.Range(0.5f, 0.7f));
                d.Velocity = new Vector3(0f, UnityEngine.Random.Range(-0.6f, -0.2f), 0f) * k;
                d.Gravity = 7f;
                d.StartScale = new Vector3(0.7f * k, 1.4f * k, 1f);
                d.EndScale = new Vector3(0.7f * k, 1.4f * k, 1f);
            }
        }
        catch (Exception ex)
        {
            DashParryDebugLog.LogVerbose("[ElecVfx] Error PlayPoisonBurst: " + ex.Message);
        }
    }

    // ---------------------------------------------------------------------
    // Helpers compartidos
    // ---------------------------------------------------------------------

    internal static VfxParticle SpawnParticle(Sprite sprite, Vector3 pos, Color color, int sortingOrder, float life)
    {
        var go = new GameObject("CoopElementalFx");
        go.transform.position = pos;
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        // Misma capa de dibujo que el Penitente (la capa 0 puede quedar oculta tras el escenario).
        var ps = Core.Logic != null && Core.Logic.Penitent != null ? Core.Logic.Penitent.SpriteRenderer : null;
        sr.sortingLayerID = ps != null ? ps.sortingLayerID : 0;
        sr.sortingOrder = (ps != null ? ps.sortingOrder : 0) + sortingOrder;
        sr.color = color;
        VfxParticle p = go.AddComponent<VfxParticle>();
        p.Init(sr, color, life);
        return p;
    }

    private static Sprite MakeSprite(int n, Color[] px)
    {
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp
        };
        tex.SetPixels(px);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0f, 0f, n, n), new Vector2(0.5f, 0.5f), 32f);
    }

    internal static Sprite CircleSprite()
    {
        if (circleSprite == null)
        {
            const int n = 8;
            var px = new Color[n * n];
            float c = (n - 1) * 0.5f;
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                    if (d <= n * 0.5f - 0.5f) px[y * n + x] = Color.white;
                }
            }
            circleSprite = MakeSprite(n, px);
        }
        return circleSprite;
    }

    internal static Sprite SquareSprite()
    {
        if (squareSprite == null)
        {
            const int n = 3;
            var px = new Color[n * n];
            for (int i = 0; i < px.Length; i++) px[i] = Color.white;
            squareSprite = MakeSprite(n, px);
        }
        return squareSprite;
    }

    internal static Sprite RingSprite()
    {
        if (ringSprite == null)
        {
            const int n = 32;
            var px = new Color[n * n];
            float c = (n - 1) * 0.5f;
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                    if (d <= 15.5f && d >= 13.5f) px[y * n + x] = Color.white;
                }
            }
            ringSprite = MakeSprite(n, px);
        }
        return ringSprite;
    }

    // Burbuja pixel-art: cuerpo gris (se tinta en verde) + pixel de brillo blanco arriba-izquierda
    internal static Sprite BubbleSprite()
    {
        if (bubbleSprite == null)
        {
            const int n = 8;
            var px = new Color[n * n];
            float c = (n - 1) * 0.5f;
            var body = new Color(0.85f, 0.85f, 0.85f, 1f);
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                    if (d <= n * 0.5f - 0.5f) px[y * n + x] = body;
                }
            }
            px[5 * n + 2] = Color.white;
            bubbleSprite = MakeSprite(n, px);
        }
        return bubbleSprite;
    }
}


/// <summary>
/// Partícula genérica one-shot: velocidad, gravedad, oscilación lateral opcional, escala y color interpolados.
/// Se autodestruye al expirar su vida.
/// </summary>
internal sealed class VfxParticle : MonoBehaviour
{
    internal Vector3 Velocity;
    internal float Gravity;
    internal float WobbleAmp;
    internal float WobbleFreq;
    internal float WobblePhase;
    internal Vector3 StartScale = Vector3.one;
    internal Vector3 EndScale = Vector3.one;

    private SpriteRenderer sr;
    private Color startColor;
    private float life;
    private float age;

    internal void Init(SpriteRenderer renderer, Color color, float lifetime)
    {
        sr = renderer;
        startColor = color;
        life = Mathf.Max(0.01f, lifetime);
    }

    private void Update()
    {
        try
        {
            float dt = Time.deltaTime;
            age += dt;
            if (age >= life)
            {
                Destroy(gameObject);
                return;
            }

            float t = age / life;
            Velocity.y -= Gravity * dt;
            Vector3 wobble = new Vector3(WobbleAmp * Mathf.Sin(age * WobbleFreq + WobblePhase), 0f, 0f);
            transform.position += (Velocity + wobble) * dt;
            transform.localScale = Vector3.Lerp(StartScale, EndScale, t);

            if (sr != null)
            {
                Color c = startColor;
                c.a = startColor.a * (1f - t);
                sr.color = c;
            }
        }
        catch (Exception)
        {
            Destroy(gameObject);
        }
    }
}
