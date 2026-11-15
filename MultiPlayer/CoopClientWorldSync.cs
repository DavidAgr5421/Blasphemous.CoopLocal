using System;
using System.Collections.Generic;
using Com.LuisPedroFonseca.ProCamera2D;
using Framework.Managers;
using Gameplay.GameControllers.Entities;
using Gameplay.GameControllers.Penitent;
using UnityEngine;
using UnityEngine.UI;

namespace Blasphemous.CoopLocal.MultiPlayer;

/// <summary>
/// Sincronizador visual del mundo para el Cliente en modo cooperativo en línea.
/// Recibe los paquetes WorldState del Anfitrión y actualiza las posiciones, animaciones,
/// orientación y vitals de todos los penitentes en pantalla, además de enfocar la cámara local.
/// </summary>
public class CoopClientWorldSync : MonoBehaviour
{
    private static CoopClientWorldSync _instance;
    public static CoopClientWorldSync Instance
    {
        get
        {
            if (_instance == null)
            {
                Initialize();
            }
            return _instance;
        }
    }

    public class PlayerPuppet
    {
        public int SlotIndex;
        public GameObject Root;
        public SpriteRenderer Renderer;
        public Animator Anim;
        public TextMesh NameTag;
        public Vector2 TargetPosition;
        public byte LastAnimIndex = 255;
        public string AppliedSkin = "";
    }

    private readonly Dictionary<int, PlayerPuppet> puppets = new Dictionary<int, PlayerPuppet>();
    private ProCamera2D proCamera;

    public static void Initialize()
    {
        if (_instance != null) return;

        GameObject go = new GameObject("CoopClientWorldSync");
        UnityEngine.Object.DontDestroyOnLoad(go);
        _instance = go.AddComponent<CoopClientWorldSync>();
    }

    private void Awake()
    {
        _instance = this;
    }

    private void Update()
    {
        if (!CoopNetworkManager.IsOnlineClient) return;

        // Vanilla re-habilita el SpriteRenderer del penitente local tras cambios de escena;
        // mientras existan títeres, mantenerlo oculto para evitar el duplicado fantasma
        if (puppets.Count > 0)
        {
            Penitent localPenitent = Core.Logic?.Penitent;
            if (localPenitent != null && localPenitent.SpriteRenderer != null)
            {
                localPenitent.SpriteRenderer.enabled = false;
            }
        }

        if (Time.unscaledTime >= nextPrieRetry)
        {
            nextPrieRetry = Time.unscaledTime + 1f;
            CoopOnlineSync.TryApplyPendingPrieDieus();
            CoopSceneStateSync.TryApply();
        }

        // Suavizar movimiento de los títeres visuales hacia sus posiciones objetivo
        foreach (var puppet in puppets.Values)
        {
            if (puppet.Root != null)
            {
                puppet.Root.transform.position = Vector2.Lerp(
                    puppet.Root.transform.position,
                    puppet.TargetPosition,
                    Time.deltaTime * 20f
                );
            }
        }
    }

    /// <summary>
    /// Aplica el estado del mundo recibido del Anfitrión.
    /// </summary>
    public void ApplyWorldState(WorldStateData state)
    {
        if (state == null || state.Players == null) return;

        EnsureCameraTarget();

        for (int i = 0; i < state.Players.Length; i++)
        {
            var p = state.Players[i];
            PlayerPuppet puppet = GetOrCreatePuppet(p.SlotIndex);
            if (puppet == null || puppet.Root == null) continue;

            // 1. Posición objetivo (interpolada en Update)
            puppet.TargetPosition = new Vector2(p.PosX, p.PosY);

            // Si está muy lejos (ej. teletransporte o cambio de zona), colocarlo directamente
            if (Vector2.Distance(puppet.Root.transform.position, puppet.TargetPosition) > 5f)
            {
                puppet.Root.transform.position = puppet.TargetPosition;
            }

            // 2. Orientación (Facing direction)
            if (puppet.Renderer != null)
            {
                puppet.Renderer.flipX = !p.FacingRight;
            }

            // 3. Animación
            if (puppet.Anim != null && p.AnimIndex != puppet.LastAnimIndex)
            {
                puppet.LastAnimIndex = p.AnimIndex;
                CoopAnimationStates.ApplyAnimation(puppet.Anim, p.AnimIndex);
            }

            // 4. Si este slot corresponde al jugador local del cliente, actualizar HUD principal
            if (p.SlotIndex == CoopNetworkManager.LocalAssignedSlot)
            {
                UpdateClientLocalVitals(p);
            }
        }
    }

    private float nextPrieRetry;

    /// <summary>Reaplica nombre, color y skin de cada títere (llegó información nueva del anfitrión).</summary>
    public void RefreshPuppetCosmetics()
    {
        foreach (var puppet in puppets.Values) ApplyPuppetCosmetics(puppet);
    }

    private static void ApplyPuppetCosmetics(PlayerPuppet puppet)
    {
        if (puppet == null || puppet.Root == null) return;
        int slot = puppet.SlotIndex;
        if (slot < 0 || slot >= 4) return;
        var cfg = CoopGameConfig.Slots[slot];

        if (puppet.NameTag != null)
        {
            puppet.NameTag.color = CoopGameConfig.Palette[Mathf.Clamp(cfg.ColorIndex, 0, CoopGameConfig.Palette.Length - 1)].Color;
            puppet.NameTag.text = string.IsNullOrEmpty(cfg.Name) ? ("P" + (slot + 1)) : cfg.Name;
        }

        string skin = cfg.SkinId ?? "";
        if (puppet.Renderer != null && skin.Length > 0 && skin != puppet.AppliedSkin && Core.ColorPaletteManager != null)
        {
            Sprite palette = Core.ColorPaletteManager.GetColorPaletteById(skin);
            if (palette != null && palette.texture != null)
            {
                puppet.Renderer.material.SetTexture("_PaletteTex", palette.texture);
                puppet.AppliedSkin = skin;
            }
        }
    }

    private PlayerPuppet GetOrCreatePuppet(int slotIndex)
    {
        if (puppets.TryGetValue(slotIndex, out PlayerPuppet puppet))
        {
            if (puppet.Root != null) return puppet;
            puppets.Remove(slotIndex);
        }

        // Crear nuevo títere visual
        Penitent basePenitent = Core.Logic?.Penitent;
        if (basePenitent == null) return null;

        GameObject puppetGo = new GameObject($"ClientPuppet_P{slotIndex + 1}");
        UnityEngine.Object.DontDestroyOnLoad(puppetGo);

        SpriteRenderer sr = puppetGo.AddComponent<SpriteRenderer>();
        if (basePenitent.SpriteRenderer != null)
        {
            // Material propio por títere: si se compartiera el del Penitente local, todos tendrían la misma skin
            sr.material = new Material(basePenitent.SpriteRenderer.material);
            sr.sortingLayerName = basePenitent.SpriteRenderer.sortingLayerName;
            sr.sortingOrder = basePenitent.SpriteRenderer.sortingOrder + slotIndex;
        }

        Animator anim = puppetGo.AddComponent<Animator>();
        if (basePenitent.Animator != null)
        {
            anim.runtimeAnimatorController = basePenitent.Animator.runtimeAnimatorController;
        }

        // Etiqueta flotante con nombre
        GameObject tagGo = new GameObject("Nametag");
        tagGo.transform.SetParent(puppetGo.transform, false);
        tagGo.transform.localPosition = new Vector3(0f, 2.2f, 0f);

        TextMesh tm = tagGo.AddComponent<TextMesh>();
        tm.fontSize = 20;
        tm.characterSize = 0.08f;
        tm.anchor = TextAnchor.MiddleCenter;
        tm.alignment = TextAlignment.Center;
        tm.font = Font.CreateDynamicFontFromOSFont("Trajan Pro", 18);
        tm.color = CoopGameConfig.Palette[Mathf.Clamp(CoopGameConfig.Slots[slotIndex].ColorIndex, 0, CoopGameConfig.Palette.Length - 1)].Color;
        tm.text = CoopGameConfig.Slots[slotIndex].Name ?? $"P{slotIndex + 1}";

        MeshRenderer mr = tagGo.GetComponent<MeshRenderer>();
        if (mr != null)
        {
            mr.sortingLayerName = "Overlay";
            mr.sortingOrder = 100;
        }

        puppet = new PlayerPuppet
        {
            SlotIndex = slotIndex,
            Root = puppetGo,
            Renderer = sr,
            Anim = anim,
            NameTag = tm,
            TargetPosition = basePenitent.transform.position
        };

        puppetGo.transform.position = basePenitent.transform.position;
        puppets[slotIndex] = puppet;
        ApplyPuppetCosmetics(puppet);

        // Ocultar el penitente local vanilla de fondo para evitar duplicados en el cliente
        if (basePenitent.SpriteRenderer != null && slotIndex == 0)
        {
            basePenitent.SpriteRenderer.enabled = false;
        }

        return puppet;
    }

    private void EnsureCameraTarget()
    {
        if (proCamera == null)
        {
            proCamera = UnityEngine.Object.FindObjectOfType<ProCamera2D>();
        }
        if (proCamera == null) return;

        // Enfocar la cámara del cliente en los títeres de TODOS sus jugadores locales (uno o varios)
        bool cleared = false;
        int[] mine = CoopNetworkManager.LocalAssignedSlots;
        for (int m = 0; m < mine.Length; m++)
        {
            if (!puppets.TryGetValue(mine[m], out PlayerPuppet myPuppet) || myPuppet.Root == null) continue;

            bool hasTarget = false;
            foreach (var target in proCamera.CameraTargets)
            {
                if (target.TargetTransform == myPuppet.Root.transform) { hasTarget = true; break; }
            }
            if (hasTarget) continue;

            if (!cleared) { proCamera.CameraTargets.Clear(); cleared = true; }
            proCamera.AddCameraTarget(myPuppet.Root.transform, 1f, 1f, 0f);
        }
    }

    private void UpdateClientLocalVitals(PlayerStateSync p)
    {
        try
        {
            Penitent penitent = Core.Logic?.Penitent;
            if (penitent != null && penitent.Stats != null)
            {
                if (penitent.Stats.Life != null)
                {
                    penitent.Stats.Life.Current = p.CurrentHp;
                }
                if (penitent.Stats.Fervour != null)
                {
                    penitent.Stats.Fervour.Current = p.CurrentFervour;
                }
                if (penitent.Stats.Flask != null)
                {
                    penitent.Stats.Flask.Current = p.Flasks;
                }
            }
        }
        catch (Exception ex)
        {
            DashParryDebugLog.LogOnce("CoopClientWorldSync.ApplyState", $"[CoopClientWorldSync] Error aplicando estado remoto: {ex.Message}", isWarning: true);
        }
    }

    public void ClearPuppets()
    {
        foreach (var p in puppets.Values)
        {
            if (p.Root != null)
            {
                UnityEngine.Object.Destroy(p.Root);
            }
        }
        puppets.Clear();
        RestoreLocalPenitentVisibility();
    }

    /// <summary>
    /// Vuelve a mostrar el SpriteRenderer del penitente local (sin títeres que lo reemplacen).
    /// </summary>
    public void RestoreLocalPenitentVisibility()
    {
        Penitent localPenitent = Core.Logic?.Penitent;
        if (localPenitent != null && localPenitent.SpriteRenderer != null)
        {
            localPenitent.SpriteRenderer.enabled = true;
        }
    }
}
