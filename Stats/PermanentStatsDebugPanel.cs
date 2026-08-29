using Framework.FrameworkCore;
using Framework.Managers;
using Gameplay.GameControllers.Entities;
using Gameplay.GameControllers.Penitent;
using UnityEngine;

namespace Blasphemous.CoopLocal;

// Round 57: panel de debug in-game para ver y ajustar PermanetBonus (typo real del juego, sin la
// segunda "n" - confirmado en Framework.FrameworkCore.Attributes.Logic.Attribute, ver el header
// comment de Player2StatsSync.cs) de las 7 stats permanentes: Life, Fervour, Strength, FlaskHealth,
// Flask, BeadSlots, MeaCulpa. Separa P1 y P2, toggleable con F8 - libre, no choca con F9 (toggle de
// modo P2, Input/Player2Input.cs) ni F10 (ciclador de cámara debug, Camera/Camera.cs).
// Arquitectura calcada de CameraTargetDebugToggle/CameraTargetDebugToggleDriver (Camera/Camera.cs):
// una clase estática dueña del GameObject+driver (EnsureCreated con guard anti-duplicado) y un
// MonoBehaviour separado que lee el input. A diferencia de CameraTargetDebugToggle (que dibuja su
// indicador vía un Canvas/TextMeshProUGUI persistente), este panel es un IMGUI real (OnGUI +
// GUILayout.Window) porque necesita botones interactivos, no solo texto.

// ---------------------------------------------------------------------------
// 1. Panel principal - crea el driver y gestiona la visibilidad
// ---------------------------------------------------------------------------
internal static class PermanentStatsDebugPanel
{
    private static GameObject driverObject;

    // Array readonly con las 7 StatsTypes de interés (orden importa para el panel).
    internal static readonly EntityStats.StatsTypes[] StatsTypes =
    {
        EntityStats.StatsTypes.Life,
        EntityStats.StatsTypes.Fervour,
        EntityStats.StatsTypes.Strength,
        EntityStats.StatsTypes.FlaskHealth,
        EntityStats.StatsTypes.Flask,
        EntityStats.StatsTypes.BeadSlots,
        EntityStats.StatsTypes.MeaCulpa
    };

    internal static bool Visible { get; set; }

    internal static void EnsureCreated()
    {
        if (driverObject != null)
        {
            return;
        }
        driverObject = new GameObject("CoopLocalPermanentStatsDebugPanel");
        Object.DontDestroyOnLoad(driverObject);
        driverObject.AddComponent<PermanentStatsDebugPanelDriver>();
    }
}

// ---------------------------------------------------------------------------
// 2. Driver - input (F8) + OnGUI del panel
// ---------------------------------------------------------------------------
internal class PermanentStatsDebugPanelDriver : MonoBehaviour
{
    private static readonly int WindowId = 574821; // arbitrary, unique for GUILayout.Window
    private Rect windowRect = new Rect(20f, 20f, 520f, 820f);

    private void Update()
    {
        // F8 está libre: F9 = toggle modo P2 (Input/Player2Input.cs), F10 = ciclador de cámara
        // debug (Camera/Camera.cs). Leído directo de UnityEngine.Input, igual que esos dos - es una
        // herramienta de dev, no parte del esquema de control de ningún jugador (no pasa por
        // Player2Keys/Player2Pad/PlayerLogicBlocker).
        if (Input.GetKeyDown(KeyCode.F8))
        {
            PermanentStatsDebugPanel.Visible = !PermanentStatsDebugPanel.Visible;
        }
    }

    private void OnGUI()
    {
        if (!PermanentStatsDebugPanel.Visible)
        {
            return;
        }
        windowRect = GUILayout.Window(WindowId, windowRect, DrawWindowContent, "PermanentStats Debug (F8) - P1 | P2");
    }

    private void DrawWindowContent(int id)
    {
        // P1: Core.Logic?.Penitent?.Stats - null antes de que exista una escena/save cargado
        // (ej. en el menú principal).
        Penitent p1 = Core.Logic != null ? Core.Logic.Penitent : null;
        // P2: CoopLocal.Player2?.Stats - null hasta que P2 spawnee (CoopLocal.OnPlayerSpawn).
        Penitent p2 = CoopLocal.Player2;

        GUILayout.BeginHorizontal();
        GUILayout.Label("Stat", GUILayout.Width(90));
        GUILayout.Label("P1", GUILayout.Width(150));
        GUILayout.Label("P2", GUILayout.Width(150));
        GUILayout.EndHorizontal();

        foreach (EntityStats.StatsTypes statType in PermanentStatsDebugPanel.StatsTypes)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(statType.ToString(), GUILayout.Width(90));
            DrawPlayerColumn(p1, statType, "P1 no disponible");
            DrawPlayerColumn(p2, statType, "P2 no spawneado");
            GUILayout.EndHorizontal();
        }

        GUILayout.Space(8f);

        // Guardar baseline P2 - deliberadamente el único punto que llama a
        // Player2StatsSync.PersistPermanentBonus; +1/-1/Reset arriba NUNCA lo llaman
        // automáticamente (mutan Attribute.PermanetBonus en memoria nada más, igual que la propia
        // UI de mejoras del juego hace con Upgrade()/SetPermanentBonus() antes de que algo más
        // decida persistir).
        GUI.enabled = p2 != null;
        if (GUILayout.Button("Guardar baseline P2 ahora"))
        {
            if (p2 != null)
            {
                Player2StatsSync.PersistPermanentBonus(p2);
            }
        }
        GUI.enabled = true;

        GUILayout.Space(8f);
        GUILayout.Label(
            "ADVERTENCIA: P1 no tiene colchon de prueba - los cambios de P1 van directo a\n" +
            "Core.Logic.Penitent.Stats (el mismo EntityStats real del juego) y pueden quedar\n" +
            "escritos en el save en el proximo checkpoint. Probar en un slot descartable.");

        GUILayout.Space(10f);
        // --- Seccion Skill Tree (15 ids) ---
        GUILayout.Label("=== Skill Tree (15) - P1 | P2 ===");
        if (Core.SkillManager == null)
        {
            GUILayout.Label("SkillManager no disponible (menu?)");
        }
        else
        {
            foreach (string skillId in Player2SkillManager.AllIds)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(skillId, GUILayout.Width(90));
                DrawSkillColumn(skillId, isP2: false);
                DrawSkillColumn(skillId, isP2: true);
                GUILayout.EndHorizontal();
            }
            GUILayout.Space(4f);
            if (GUILayout.Button("Guardar skills P2 ahora"))
            {
                Player2SkillManager.Persist();
            }
            GUILayout.Label("P1: muta SkillManager global real. P2: shadow Player2SkillManager (solo Persist guarda).");
            GUILayout.Label("RangeAttack roto por hardcodeo Core.Logic.Penitent (ver NOTES Ronda 60) - tier no afecta aun.");
        }

        GUI.DragWindow(new Rect(0f, 0f, 10000f, 20f));
    }

    private static void DrawSkillColumn(string skillId, bool isP2)
    {
        GUILayout.BeginVertical(GUILayout.Width(150));
        bool unlocked = isP2 ? Player2SkillManager.IsUnlocked(skillId) : Core.SkillManager.IsSkillUnlocked(skillId);
        GUILayout.BeginHorizontal();
        GUILayout.Label(unlocked ? "[X]" : "[ ]", GUILayout.Width(30));
        string label = unlocked ? "ON" : "OFF";
        GUILayout.Label(label, GUILayout.Width(30));
        if (GUILayout.Button(unlocked ? "Lock" : "Unlock", GUILayout.Width(60)))
        {
            if (isP2)
            {
                Player2SkillManager.SetUnlocked(skillId, !unlocked);
            }
            else
            {
                if (unlocked) Core.SkillManager.LockSkill(skillId);
                else Core.SkillManager.UnlockSkill(skillId, ignoreChecks: true);
            }
        }
        GUILayout.EndHorizontal();
        GUILayout.EndVertical();
    }

    // Dibuja el valor + botones (+1/-1/Reset) de un jugador para un stat dado. penitent==null
    // (P1 sin sesion activa, o P2 sin spawnear) se resuelve mostrando un placeholder sin botones -
    // nunca intenta leer .Stats de una referencia nula.
    private static void DrawPlayerColumn(Penitent penitent, EntityStats.StatsTypes statType, string nullLabel)
    {
        GUILayout.BeginVertical(GUILayout.Width(150));
        if (penitent == null)
        {
            GUILayout.Label(nullLabel);
        }
        else
        {
            Framework.FrameworkCore.Attributes.Logic.Attribute attr = penitent.Stats.GetByType(statType);
            if (attr == null)
            {
                GUILayout.Label("(sin attr)");
            }
            else
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(attr.PermanetBonus.ToString("F1"), GUILayout.Width(40));
                if (GUILayout.Button("+1", GUILayout.Width(30)))
                {
                    attr.Upgrade();
                }
                if (GUILayout.Button("-1", GUILayout.Width(30)))
                {
                    attr.SetPermanentBonus(Mathf.Max(0f, attr.PermanetBonus - 1f));
                }
                if (GUILayout.Button("Reset", GUILayout.Width(45)))
                {
                    attr.SetPermanentBonus(0f);
                }
                GUILayout.EndHorizontal();
            }
        }
        GUILayout.EndVertical();
    }
}
