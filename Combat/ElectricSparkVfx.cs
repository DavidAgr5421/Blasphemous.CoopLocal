using System;
using System.IO;
using System.Reflection;
using Gameplay.GameControllers.Entities;
using UnityEngine;

namespace Blasphemous.CoopLocal;

/// <summary>
/// Chispas de relámpago en cada golpe del Corazón del Relámpago. Usa la hoja de 8 fotogramas del usuario
/// (embebida: lightning_frames.png, celdas de 512x112, primera celda arriba) como una animación corta
/// (~0.36 s) con 3 variaciones (normal, espejo+inclinado, casi vertical) sobre el enemigo golpeado.
/// Más pequeña y breve que la animación de aturdimiento.
/// </summary>
internal static class ElectricSparkSprites
{
    internal const int Frames = 8;
    private const int CellW = 512, CellH = 112;
    internal static Sprite[] Sheet;
    private static bool failed;

    internal static bool Ensure()
    {
        if (Sheet != null) return true;
        if (failed) return false;
        try
        {
            using (Stream st = Assembly.GetExecutingAssembly().GetManifestResourceStream("lightning_frames.png"))
            {
                if (st == null) { failed = true; DashParryDebugLog.Log("[ElecSpark] Recurso lightning_frames.png no encontrado"); return false; }
                byte[] buf = new byte[st.Length];
                int read = 0;
                while (read < buf.Length)
                {
                    int n = st.Read(buf, read, buf.Length - read);
                    if (n <= 0) break;
                    read += n;
                }
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                if (!ImageConversion.LoadImage(tex, buf)) { failed = true; return false; }
                UnityEngine.Object.DontDestroyOnLoad(tex);
                var sprites = new Sprite[Frames];
                for (int i = 0; i < Frames; i++)
                {
                    // Texture2D tiene el origen abajo: el fotograma 0 (arriba en el PNG) es la última fila.
                    Rect r = new Rect(0, tex.height - (i + 1) * CellH, CellW, CellH);
                    sprites[i] = Sprite.Create(tex, r, new Vector2(0.5f, 0.5f), CellW / 1.3f);   // ~1.3 unidades de ancho
                    UnityEngine.Object.DontDestroyOnLoad(sprites[i]);
                }
                Sheet = sprites;
            }
            return true;
        }
        catch (Exception ex)
        {
            failed = true;
            DashParryDebugLog.Log("[ElecSpark] Error cargando hoja de relámpagos: " + ex.Message);
            return false;
        }
    }
}

internal sealed class ElectricSparkAnim : MonoBehaviour
{
    private const float Fps = 22f;

    private Enemy target;
    private SpriteRenderer sr;
    private SpriteRenderer bodySr;
    private Vector3 offset;
    private float age;

    internal void Init(Enemy t, Vector3 offsetFromEnemy, int variant, float size)
    {
        target = t;
        offset = offsetFromEnemy;
        bodySr = t.SpriteRenderer;

        sr = gameObject.AddComponent<SpriteRenderer>();
        sr.sprite = ElectricSparkSprites.Sheet[0];
        sr.sortingLayerID = bodySr != null ? bodySr.sortingLayerID : 0;
        sr.sortingOrder = (bodySr != null ? bodySr.sortingOrder : 0) + 14;

        float rot;
        switch (variant)
        {
            case 1: sr.flipX = true; rot = UnityEngine.Random.Range(-35f, 35f); break;
            case 2: rot = UnityEngine.Random.Range(55f, 100f) * (UnityEngine.Random.value < 0.5f ? 1f : -1f); size *= 0.8f; break;
            default: rot = UnityEngine.Random.Range(-20f, 20f); break;
        }
        transform.rotation = Quaternion.Euler(0f, 0f, rot);
        transform.localScale = Vector3.one * size;
        Follow();
    }

    private void Follow()
    {
        if (target != null) transform.position = target.transform.position + offset;
    }

    private void Update()
    {
        try
        {
            age += Time.deltaTime;
            int frame = Mathf.FloorToInt(age * Fps);
            if (frame >= ElectricSparkSprites.Frames || target == null) { Destroy(gameObject); return; }
            sr.sprite = ElectricSparkSprites.Sheet[frame];
            Follow();
        }
        catch (Exception ex)
        {
            DashParryDebugLog.Log("[ElecSpark] Error animando chispa: " + ex.Message);
            Destroy(gameObject);
        }
    }
}
