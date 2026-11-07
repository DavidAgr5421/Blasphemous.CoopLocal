using System.Collections.Generic;
using CreativeSpore.SmartColliders;
using Framework.FrameworkCore;
using Framework.Managers;
using Gameplay.GameControllers.Camera;
using Gameplay.GameControllers.Penitent;
using Blasphemous.ModdingAPI;
using UnityEngine;

namespace Blasphemous.CoopLocal;

public class CoopLocal : BlasMod
{
    // Round 48: was (1.5f, 0f, 0f) - user reported P2 "sometimes goes back right at a room
    // crossing" after a room transition, i.e. right when P2 gets destroyed and recreated at
    // `p1.transform.position + P2SpawnOffset`. A *fixed* nonzero offset has no way to know
    // whether that point is actually safe in the new room - if the door/spawn point the new room
    // places P1 at happens to be narrow, right against a wall, or near a ledge, +1.5 on the X axis
    // can land P2 inside geometry or over a drop, and Unity's own physics resolution shoving P2
    // back out could easily look like "P2 walked back through the door". Zero offset can never be
    // wrong in that specific way - P1's own position is guaranteed valid (the game just placed
    // P1 there), so spawning P2 at the exact same point removes that failure mode entirely; the
    // two visually separate again within a frame or two once movement resumes.
    private static readonly Vector3 P2SpawnOffset = Vector3.zero;

    // Instancias de jugadores cooperativos
    internal static Penitent Player2 { get; private set; }
    internal static Penitent Player3 { get; private set; }
    internal static Penitent Player4 { get; private set; }

    internal static Penitent GetPlayer(int index)
    {
        switch (index)
        {
            case 0: return Core.Logic != null ? Core.Logic.Penitent : null;
            case 1: return Player2;
            case 2: return Player3;
            case 3: return Player4;
            default: return null;
        }
    }

    internal static IEnumerable<Penitent> GetAllExtraPlayers()
    {
        if (Player2 != null) yield return Player2;
        if (Player3 != null) yield return Player3;
        if (Player4 != null) yield return Player4;
    }

    internal static bool IsGuestPlayer(Penitent penitent, out int slotIndex)
    {
        slotIndex = -1;
        if (penitent == null) return false;
        if (penitent == Player2) { slotIndex = 1; return true; }
        if (penitent == Player3) { slotIndex = 2; return true; }
        if (penitent == Player4) { slotIndex = 3; return true; }
        return false;
    }

    internal CoopLocal() : base(ModInfo.MOD_ID, ModInfo.MOD_NAME, ModInfo.MOD_AUTHOR, ModInfo.MOD_VERSION) { }

    protected override void OnInitialize()
    {
        CoopGameConfig.Load();
        CoopGameConfig.ApplyToRegistry();
        if (CoopGameConfig.IsCoopEnabled && Combat.CoopEnemyScaling.ActiveEnemyMultiplier <= 1.01f && Combat.CoopEnemyScaling.ActiveHealthMultiplier <= 1.01f)
        {
            Combat.CoopEnemyScaling.SetDefaultsForPlayerCount(Mathf.Clamp(CoopGameConfig.PlayerCount, 2, 4));
        }

        SpawnManager.OnPlayerSpawn += OnPlayerSpawn;
        LevelManager.OnLevelLoaded += OnLevelLoaded;
        Player2HudFadeSync.Initialize();

        // Round 52 - debug tool: F10 camera-target cycler (Coop/P1 only/P2 only). Its own
        // driver MonoBehaviour just needs to exist once, independent of P1/P2's own lifecycle -
        // see Camera/Camera.cs for the actual toggle logic.
        CameraTargetDebugToggle.EnsureCreated();

        // Round 57 - panel de debug in-game para ver/ajustar PermanentBonus P1|P2 (F8).
        // Ve Stats/PermanentStatsDebugPanel.cs para la arquitectura y keybindings.
        PermanentStatsDebugPanel.EnsureCreated();
        CameraTargetModeIndicator.Show(CameraTargetDebugToggle.Mode);

        // Menú de depuración y teletransporte a jefes (F2)
        Combat.BossDebugTeleportManager.EnsureCreated();
    }

    protected override void OnDispose()
    {
        SpawnManager.OnPlayerSpawn -= OnPlayerSpawn;
        LevelManager.OnLevelLoaded -= OnLevelLoaded;
        Player2HudFadeSync.Dispose();
        Combat.BossDebugTeleportManager.Dispose();
    }

    // Round 47: the wall cliff-lede fix (SetLayerRecursively, round 46) copies P1's layer onto P2
    // right when OnPlayerSpawn fires - but the user reported walls still not working afterward.
    // GravityScale already proved this exact "P1's own value isn't final until the level actually
    // finishes loading" pattern is real for this game (LevelManager.LoadLevelRoutine only sets
    // GravityScale to 3 on Core.Logic.Penitent *after* load completes, which is why P2 sets its
    // own GravityScale manually above instead of trusting a copy) - P1's layer may well be the
    // same story, just not previously verified. Re-applying the layer copy again once the level
    // has genuinely finished loading (LevelManager.OnLevelLoaded, the real "level fully ready"
    // signal, not just "P1 object exists") costs nothing if the first copy was already correct,
    // and fixes it if it wasn't.
    //
    // Round 54: this used to be SetLayerRecursively(Player2.transform, Core.Logic.Penitent
    // .gameObject.layer) - a single *root* layer stamped onto P2's *entire* hierarchy, including
    // children that are deliberately on a different layer than the root in the source prefab (P1's
    // own "Attack Area" child lives on "Water", not P1's own root "Penitent" layer - confirmed via
    // Combat/Damage.cs's own overlap diagnostic, comparing a real P1 hit's log line against a
    // "phantom" P2 one side by side). Stamping every child to the root layer silently moved P2's
    // Attack Area onto the same layer as its own real Body/DamageArea collider - so
    // ContactDamage.OnTriggerEnter2D (which resolves a touch via whichever collider entered,
    // GetComponentInParent<IDamageable>() - see Combat/ContactDamage.cs) started treating P2's
    // offensive weapon-reach hitbox brushing an enemy as a genuine body touch, applying real
    // contact damage while P2's actual body/DamageArea was nowhere near it - the root cause of the
    // "P2 takes damage from an enemy it's not really touching" reports from rondas 50/53 (that
    // whole earlier investigation, including its diagnostics, is now superseded by this - see
    // ronda 54 below). Fixed by copying every node's layer individually, matching P1's live
    // hierarchy child-for-child (same "Core/Penitent" prefab structure for both, so child order is
    // stable) instead of a single blanket value - this also transparently fixes any other child
    // that's deliberately off the root layer (P1's own "#Abilities" collider is on "Default", also
    // confirmed via the same log comparison) without needing to special-case any of them by name.
    private void OnLevelLoaded(Level oldLevel, Level newLevel)
    {
        Combat.CoopEnemyScaling.OnLevelLoaded();
        Combat.BossDebugTeleportManager.OnLevelLoaded(oldLevel, newLevel);
        if (Core.Logic == null || Core.Logic.Penitent == null)
        {
            return;
        }
        PrieDieuCoopSync.Reset();
        foreach (var extra in GetAllExtraPlayers())
        {
            if (extra != null)
            {
                CopyLayersRecursively(Core.Logic.Penitent.transform, extra.transform);
            }
        }
    }

    // Fired by the game every time P1 spawns/respawns: level load, teleport, death respawn, etc.
    private void OnPlayerSpawn(Penitent p1)
    {
        Penitent_OnUpdate_P2Death_Patch.IsAllDeadEventLaunched = false;

        CleanupAllExtraPlayers();

        if (Core.GameModeManager != null && Core.GameModeManager.IsCurrentMode(GameModeManager.GAME_MODES.MENU))
        {
            return;
        }

        if (!CoopGameConfig.IsCoopEnabled || CoopGameConfig.PlayerCount < 2)
        {
            return;
        }

        if (Player2DeathState.IsPendingRevive())
        {
            return;
        }

        SpawnAllGuestPlayers(p1);
    }

    private static void CleanupAllExtraPlayers()
    {
        CleanupSinglePlayer(Player2, 1);
        Player2 = null;
        CleanupSinglePlayer(Player3, 2);
        Player3 = null;
        CleanupSinglePlayer(Player4, 3);
        Player4 = null;
    }

    private static void CleanupSinglePlayer(Penitent p, int slot)
    {
        if (p == null) return;
        if (CameraManager.Instance != null && CameraManager.Instance.ProCamera2D != null)
        {
            CameraManager.Instance.ProCamera2D.RemoveCameraTarget(p.transform);
        }
        if (slot >= 1)
        {
            Player2SkillManager.Persist();
            Player2InventoryManager.Persist();
            Player2StatsSync.SaveCurrentVitals(p, slot);
        }
        Object.Destroy(p.gameObject);
    }

    internal static Penitent SpawnPlayer2(Penitent p1, Vector3 spawnPosition)
    {
        SpawnAllGuestPlayers(p1);
        return Player2;
    }

    internal static void SpawnAllGuestPlayers(Penitent p1)
    {
        if (!CoopGameConfig.IsCoopEnabled || CoopGameConfig.PlayerCount < 2)
        {
            return;
        }

        int activeCount = Mathf.Clamp(CoopGameConfig.PlayerCount, 2, 4);

        // Spawn P2
        Vector3 p2Pos = p1.transform.position + new Vector3(-0.6f, 0f, 0f);
        Player2 = SpawnSingleGuestPlayer(p1, 1, p2Pos);

        // Spawn P3
        if (activeCount >= 3)
        {
            Vector3 p3Pos = p1.transform.position + new Vector3(0.6f, 0f, 0f);
            Player3 = SpawnSingleGuestPlayer(p1, 2, p3Pos);
        }

        // Spawn P4
        if (activeCount >= 4)
        {
            Vector3 p4Pos = p1.transform.position + new Vector3(1.2f, 0f, 0f);
            Player4 = SpawnSingleGuestPlayer(p1, 3, p4Pos);
        }

        // Ignorar colisiones entre todos los jugadores
        List<Penitent> all = new List<Penitent> { p1 };
        foreach (var extra in GetAllExtraPlayers())
        {
            if (extra != null) all.Add(extra);
        }
        for (int i = 0; i < all.Count; i++)
        {
            Collider2D[] colsA = all[i].GetComponentsInChildren<Collider2D>(includeInactive: true);
            for (int j = i + 1; j < all.Count; j++)
            {
                Collider2D[] colsB = all[j].GetComponentsInChildren<Collider2D>(includeInactive: true);
                foreach (var ca in colsA)
                {
                    foreach (var cb in colsB)
                    {
                        Physics2D.IgnoreCollision(ca, cb, true);
                    }
                }
            }
        }

        // Registrar P1 en CoopPlayerRegistry y asignar etiqueta
        CoopPlayerRegistry.RegisterInstance(0, p1);
        AddNameLabel(p1, CoopPlayerRegistry.GetDisplayName(0), CoopPlayerRegistry.GetLabelColor(0), outlineColor: CoopPlayerRegistry.GetOutlineColor(0));

        // Registrar targets en la cámara
        if (CameraManager.Instance != null && CameraManager.Instance.ProCamera2D != null)
        {
            CameraManager_UpdateNewCameraParams_Patch.AddPlayer2AsCameraTarget(CameraManager.Instance.ProCamera2D);
        }

        // HUD para P2
        if (Player2 != null)
        {
            Player2HealthBar.EnsureCreated(Player2);
            Player2FervourBar.EnsureCreated(Player2);
            Player2PurgePoints.EnsureCreated(Player2);
            Player2HealthBar.BringToFront();
            Player2HudFadeSync.ApplyCurrentFadeState();
        }
    }

    private static Penitent SpawnSingleGuestPlayer(Penitent p1, int slotIndex, Vector3 spawnPosition)
    {
        Penitent pPrefab = Resources.Load<Penitent>("Core/Penitent");
        Penitent guest = Object.Instantiate(pPrefab, spawnPosition, Quaternion.identity);
        Object.DontDestroyOnLoad(guest.gameObject);
        CopyLayersRecursively(p1.transform, guest.transform);
        guest.PlatformCharacterController.PlatformCharacterPhysics.GravityScale = 3f;

        CoopSaveManager.EnsureLoadedForCurrentSlot();
        CoopSaveManager.ApplyConfigToGame(CoopSaveManager.CurrentSave);
        Player2StatsSync.EnsureSynced(p1, guest, slotIndex);
        Player2SkillManager.EnsureLoadedForCurrentSlot();
        Player2InventoryManager.EnsureLoadedForCurrentSlot();

        CoopPlayerRegistry.RegisterInstance(slotIndex, guest);
        AddNameLabel(guest, CoopPlayerRegistry.GetDisplayName(slotIndex), CoopPlayerRegistry.GetLabelColor(slotIndex), outlineColor: CoopPlayerRegistry.GetOutlineColor(slotIndex));

        // Aplicar aspecto / skin configurado
        string skinId = CoopGameConfig.Slots[slotIndex].SkinId;
        if (string.IsNullOrEmpty(skinId)) skinId = CoopPlayerRegistry.GetSlot(slotIndex).SkinPaletteId;
        var swapper = guest.GetComponentInChildren<Gameplay.GameControllers.Effects.Player.Recolor.ColorPaletteSwapper>();
        if (swapper != null && !string.IsNullOrEmpty(skinId))
        {
            ColorPaletteSwapper_ForcePlayer2TrueApostasy_Patch.ApplySkinToSwapper(swapper, skinId);
        }

        MudAreaEffect_OnExitAreaEffect_Patch.RememberBaseline(guest);

        if (Main.CoopLocal != null)
        {
            Blasphemous.ModdingAPI.ModLog.Info(
                $"Guest P{slotIndex + 1} spawned at {spawnPosition} (p1 at {p1.transform.position})",
                Main.CoopLocal);
        }

        return guest;
    }

    // Round 54: replaces the old SetLayerRecursively(Transform, int) - a single layer value
    // stamped onto every node - with a per-node copy from P1's own live hierarchy onto P2's,
    // matched by child index (both are instances of the same "Core/Penitent" prefab structure, so
    // child order is stable between them). This is what actually makes P2's per-child layers
    // (Attack Area, #Abilities, etc.) match P1's real ones instead of collapsing them all onto
    // whatever the root happens to be - see the comments on OnLevelLoaded/OnPlayerSpawn above for
    // the concrete bug this fixes. Defensively caps at the shorter of the two child counts in case
    // the two hierarchies ever briefly diverge (shouldn't happen for two instances of the same
    // prefab, but costs nothing to guard against).
    private static void CopyLayersRecursively(Transform source, Transform target)
    {
        target.gameObject.layer = source.gameObject.layer;
        int childCount = Mathf.Min(source.childCount, target.childCount);
        for (int i = 0; i < childCount; i++)
        {
            CopyLayersRecursively(source.GetChild(i), target.GetChild(i));
        }
    }

    private const string NameLabelChildName = "CoopLocalNameLabel";

    private static void AddNameLabel(Penitent penitent, string label, Color32 color, Color? outlineColor = null)
    {
        if (penitent.transform.Find(NameLabelChildName) != null)
        {
            return;
        }

        GameObject labelObject = new GameObject(NameLabelChildName);
        labelObject.transform.SetParent(penitent.transform, worldPositionStays: false);
        labelObject.transform.localPosition = new Vector3(0f, 2.3f, 0f);
        // Keep the label upright even if the parent gets horizontally flipped for orientation.
        labelObject.AddComponent<KeepLocalTransform>();

        if (outlineColor.HasValue)
        {
            // No outline: faked with 8 duplicate copies of the same text, offset by a tiny
            // amount in a ring around the real text and drawn one sorting order behind it.
            // Classic "poor man's outline" trick - kept as-is so the new TMP labels look
            // identical to the old legacy ones, just with the game's actual font.
            const float offset = 0.045f;
            Vector3[] outlineOffsets =
            {
                new Vector3(-offset, 0f, 0f), new Vector3(offset, 0f, 0f),
                new Vector3(0f, -offset, 0f), new Vector3(0f, offset, 0f),
                //new Vector3(-offset, -offset, 0f), new Vector3(-offset, offset, 0f),
                //new Vector3(offset, -offset, 0f), new Vector3(offset, offset, 0f),
            };
            foreach (Vector3 delta in outlineOffsets)
            {
                GameObject outlineObject = new GameObject(NameLabelChildName + "_Outline");
                outlineObject.transform.SetParent(labelObject.transform, worldPositionStays: false);
                outlineObject.transform.localPosition = delta;
                CreateLabelTextMesh(outlineObject, label, outlineColor.Value, sortingOrder: 99);
            }
        }

        CreateLabelTextMesh(labelObject, label, color, sortingOrder: 100);
    }

    internal static void UpdateNameLabel(Penitent penitent, string label, Color32 color, Color? outlineColor = null)
    {
        if (penitent == null) return;
        Transform labelTr = penitent.transform.Find(NameLabelChildName);
        if (labelTr == null)
        {
            AddNameLabel(penitent, label, color, outlineColor);
            return;
        }

        var texts = labelTr.GetComponentsInChildren<TMPro.TextMeshPro>(includeInactive: true);
        foreach (var tm in texts)
        {
            tm.text = label;
            if (tm.gameObject.name.Contains("Outline"))
            {
                if (outlineColor.HasValue) tm.color = outlineColor.Value;
            }
            else
            {
                tm.color = color;
            }
        }
    }

    // The game's real Latin TMP font (the same one the in-game UI text uses; asset name captured
    // in the Round-50 journal note). It is a serialized runtime asset - not loadable by path - so
    // it can only be found by scanning the loaded resources once and reusing it for all labels.
    private const string GameFontName = "MajesticExtended_FullLatin";
    private static TMPro.TMP_FontAsset gameFont;

    // Target world-space height of the label text. The legacy TextMesh label it replaces rendered
    // about this tall. TMP fontSize is in font units, not world units - so we build a mesh at a
    // base size, measure its real world height, then rescale fontSize so the text lands at
    // LabelTargetHeight. The rect stays at localScale (1,1,1), so KeepLocalTransform and the
    // outline offset ring stay exactly as before.
    private const float LabelTargetHeight = 0.4f;
    private const float LabelBaseFontSize = 5f;

    private static void CreateLabelTextMesh(GameObject target, string label, Color32 color, int sortingOrder)
    {
        // Adding a RectTransform upgrades the object's existing Transform in place, which can
        // reset its localPosition - re-apply it afterwards so the label stays above the penitent.
        Vector3 localPosition = target.transform.localPosition;

        // TMPro.TextMeshPro extends TMP_Text: TMP_Text lazily grabs a RectTransform and
        // LoadDefaultSettings writes m_rectTransform.sizeDelta, so a RectTransform must exist on
        // the object before the component is added (same pattern as Player2ModeIndicator).
        if (target.GetComponent<RectTransform>() == null)
        {
            target.AddComponent<RectTransform>();
        }
        target.transform.localPosition = localPosition;

        TMPro.TextMeshPro textMesh = target.AddComponent<TMPro.TextMeshPro>();
        textMesh.text = label;
        textMesh.color = color;
        textMesh.alignment = TMPro.TextAlignmentOptions.Center;
        textMesh.enableAutoSizing = false;
        textMesh.fontSize = LabelBaseFontSize;

        if (gameFont == null)
        {
            gameFont = System.Array.Find(Resources.FindObjectsOfTypeAll<TMPro.TMP_FontAsset>(),
                f => f != null && f.name == GameFontName);
        }

        if (gameFont != null)
        {
            textMesh.font = gameFont;
        }

        // Build at the base fontSize, measure the rendered mesh's real height, then rescale the
        // fontSize so the label lands at the same world height the old legacy label had.
        textMesh.ForceMeshUpdate();
        MeshFilter meshFilter = target.GetComponent<MeshFilter>();
        if (meshFilter != null)
        {
            float measuredHeight = meshFilter.mesh.bounds.size.y;
            if (measuredHeight > 0.001f)
            {
                textMesh.fontSize = LabelBaseFontSize * (LabelTargetHeight / measuredHeight);
                textMesh.ForceMeshUpdate();
            }
        }

        MeshRenderer meshRenderer = target.GetComponent<MeshRenderer>();
        if (meshRenderer != null)
        {
            meshRenderer.sortingLayerName = "Player";
            meshRenderer.sortingOrder = sortingOrder;
        }
    }
}

// Penitent's sprite flip is done via SpriteRenderer.flipX (not a transform scale flip), so a
// plain child object wouldn't normally need this - but Penitent.SetOrientation can also flip
// the renderer via a scale change in some code paths, so this keeps the label's own local
// scale pinned to (1,1,1) every frame regardless of what the parent does.
internal class KeepLocalTransform : MonoBehaviour
{
    private void LateUpdate()
    {
        transform.localScale = Vector3.one;
    }
}


// Debug-only: a translucent red rectangle that tracks P2's real damage-taking collider
// (PenitentDamageArea's own BoxCollider2D) every frame, so a hit landing on P2 can be visually
// confirmed against the collider itself rather than the sprite (which is usually bigger than the
// actual hitbox). Parented directly to the DamageArea's own transform - not P2's root - and driven
// from that same collider's offset/size every frame, so it automatically follows
// PenitentDamageArea.ResizeDamageArea()'s own crouch/dash/jump-forward-triggered resizes without
// needing to know about any of those cases itself.
internal class Player2HitboxVisualizer : MonoBehaviour
{
    private const string ChildName = "CoopLocalHitboxVisualizer";

    private static Sprite overlaySprite;

    private BoxCollider2D targetCollider;

    internal static void EnsureCreated(Penitent p2)
    {
        if (p2.DamageArea == null)
        {
            return;
        }
        Transform damageAreaTransform = p2.DamageArea.transform;
        if (damageAreaTransform.Find(ChildName) != null)
        {
            return;
        }

        BoxCollider2D collider = p2.DamageArea.GetComponent<BoxCollider2D>();
        if (collider == null)
        {
            return;
        }

        GameObject overlayObject = new GameObject(ChildName);
        overlayObject.transform.SetParent(damageAreaTransform, worldPositionStays: false);

        SpriteRenderer renderer = overlayObject.AddComponent<SpriteRenderer>();
        renderer.sprite = GetOverlaySprite();
        renderer.color = new Color(1f, 0f, 0f, 0.35f);
        renderer.sortingLayerName = "Player";
        renderer.sortingOrder = 1000;

        Player2HitboxVisualizer visualizer = overlayObject.AddComponent<Player2HitboxVisualizer>();
        visualizer.targetCollider = collider;
    }

    // Center pivot (0.5, 0.5) here, unlike Player2StatusBars' left-pivot sprite - a BoxCollider2D's
    // own offset is already its center, so a center-pivoted sprite can copy that offset directly
    // as localPosition without any extra math.
    private static Sprite GetOverlaySprite()
    {
        if (overlaySprite == null)
        {
            Texture2D texture = new Texture2D(1, 1);
            texture.SetPixel(0, 0, Color.white);
            texture.Apply();
            overlaySprite = Sprite.Create(texture, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 1f);
        }
        return overlaySprite;
    }

    private void LateUpdate()
    {
        if (targetCollider == null)
        {
            return;
        }
        transform.localPosition = targetCollider.offset;
        transform.localScale = new Vector3(targetCollider.size.x, targetCollider.size.y, 1f);
    }
}
