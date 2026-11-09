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
            sr.material = basePenitent.SpriteRenderer.material;
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

        if (proCamera != null)
        {
            int mySlot = CoopNetworkManager.LocalAssignedSlot;
            if (puppets.TryGetValue(mySlot, out PlayerPuppet myPuppet) && myPuppet.Root != null)
            {
                // Enfocar la cámara del cliente en su propio títere asignado
                bool hasTarget = false;
                foreach (var target in proCamera.CameraTargets)
                {
                    if (target.TargetTransform == myPuppet.Root.transform)
                    {
                        hasTarget = true;
                        break;
                    }
                }

                if (!hasTarget)
                {
                    proCamera.CameraTargets.Clear();
                    proCamera.AddCameraTarget(myPuppet.Root.transform, 1f, 1f, 0f);
                }
            }
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
        catch { }
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
    }
}
