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

    // Exposed so GamePatches can tell P2's PlatformCharacterInput apart from P1's.
    internal static Penitent Player2 { get; private set; }

    internal CoopLocal() : base(ModInfo.MOD_ID, ModInfo.MOD_NAME, ModInfo.MOD_AUTHOR, ModInfo.MOD_VERSION) { }

    protected override void OnInitialize()
    {
        SpawnManager.OnPlayerSpawn += OnPlayerSpawn;
        LevelManager.OnLevelLoaded += OnLevelLoaded;
        Player2HudFadeSync.Initialize();

        // Round 52 - debug tool: F10 camera-target cycler (Coop/P1 only/P2 only). Its own
        // driver MonoBehaviour just needs to exist once, independent of P1/P2's own lifecycle -
        // see Camera/Camera.cs for the actual toggle logic.
        CameraTargetDebugToggle.EnsureCreated();
        CameraTargetModeIndicator.Show(CameraTargetDebugToggle.Mode);
    }

    protected override void OnDispose()
    {
        SpawnManager.OnPlayerSpawn -= OnPlayerSpawn;
        LevelManager.OnLevelLoaded -= OnLevelLoaded;
        Player2HudFadeSync.Dispose();
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
        if (Player2 == null || Core.Logic == null || Core.Logic.Penitent == null)
        {
            return;
        }
        CopyLayersRecursively(Core.Logic.Penitent.transform, Player2.transform);
    }

    // Fired by the game every time P1 spawns/respawns: level load, teleport, death respawn, etc.
    // P2 piggybacks on this instead of hooking every spawn path individually.
    private void OnPlayerSpawn(Penitent p1)
    {
        if (Player2 != null)
        {
            // Drop the outgoing P2's camera target before destroying it - otherwise ProCamera2D
            // keeps a dangling reference to a destroyed Transform until the next full camera
            // reset (see GamePatches' CameraManager_UpdateNewCameraParams_Patch).
            if (CameraManager.Instance != null && CameraManager.Instance.ProCamera2D != null)
            {
                CameraManager.Instance.ProCamera2D.RemoveCameraTarget(Player2.transform);
            }
            // Save P2's current life/fervour/flasks/currency before it's discarded - P2's whole
            // EntityStats gets rebuilt from scratch on every respawn, so without this all four
            // would silently reset every time (see Player2StatsSync.SaveCurrentVitals's own
            // comment in GamePatches.cs).
            Player2StatsSync.SaveCurrentVitals(Player2);
            Object.Destroy(Player2.gameObject);
            Player2 = null;
        }

        // Round 44: SpawnManager.OnPlayerSpawn also fires for the real Penitent instance that
        // stands in the main menu's background scene (a real level, not a literal menu overlay) -
        // user reported P2 visibly spawning and falling into the void there, HUD included, before
        // any save was even chosen. GameModeManager.GAME_MODES.MENU is the same flag the game's
        // own NewMainMenu.ShowMenu() sets, so it's the authoritative "are we actually in
        // gameplay" signal - skip creating P2 (and by extension its HUD, which only gets created
        // alongside it below) entirely while in the menu.
        if (Core.GameModeManager != null && Core.GameModeManager.IsCurrentMode(GameModeManager.GAME_MODES.MENU))
        {
            return;
        }

        Penitent p2Prefab = Resources.Load<Penitent>("Core/Penitent");
        Vector3 spawnPosition = p1.transform.position + P2SpawnOffset;
        Player2 = Object.Instantiate(p2Prefab, spawnPosition, Quaternion.identity);

        // Round 55 (Player2StatsSync's own vitals-persistence bug): P2 was instantiated as a plain
        // root object with no scene assignment, which Unity places into whatever scene is currently
        // *active* - LevelManager.cs sets that to the current room's own scene right before firing
        // this spawn (SceneManager.SetActiveScene(currentLevel.GetLogicScene().Scene)). Blasphemous
        // gives (at least some) individual rooms their own scene, unloaded via SceneManager as part
        // of a real room-to-room LevelManager.ChangeLevel transition (as opposed to the lighter
        // same-scene reposition path some doors use) - and Unity destroys every non-DontDestroyOnLoad
        // object living in a scene the instant that scene unloads, with no code of ours involved.
        // That unload happens *before* the new room's own OnPlayerSpawn fires (old scene must be
        // gone before the new one loads), so by the time this method's own
        // "if (Player2 != null) { SaveCurrentVitals(...); Object.Destroy(...); }" block runs for
        // that transition, Player2 already got silently destroyed by Unity itself - and Unity's
        // overloaded `== null` on a destroyed UnityEngine.Object returns true, so the whole
        // save-current-vitals-before-replacing-P2 block was skipped entirely for that transition.
        // Net effect: Player2StatsSync's on-disk snapshot only ever got updated by transitions that
        // happened to *not* cross a scene boundary - for any playthrough where most/every room
        // transition does cross one, the snapshot never advances past whatever
        // Player2StatsSync.PerformFirstSync wrote once at the very start of the run, which is
        // exactly "P2's stats reset to the one-time initial copy" as reported. Marking P2
        // DontDestroyOnLoad (the same pattern Camera/Camera.cs and Player2Input.cs already use for
        // their own cross-scene singletons) removes the race entirely: P2 now survives every scene
        // unload on its own, so this method's own explicit save-then-destroy logic is always the one
        // that runs, deterministically, every single time - not a race against Unity's own teardown.
        // Safe with respect to physics: Blasphemous loads its additive scenes via the simple
        // SceneManager.LoadScene(name, LoadSceneMode.Additive) overload (confirmed in the decompiled
        // LevelManager), never the LoadSceneParameters overload that would opt into a separate
        // per-scene PhysicsScene2D - so there is only one global 2D physics world for the whole game,
        // and moving P2 into the DontDestroyOnLoad scene does not remove it from collision/overlap
        // queries against the current room's geometry.
        Object.DontDestroyOnLoad(Player2.gameObject);

        // Round 46: found via live log data that P2's wall cliff-ledge grab never once triggered
        // across ~4500 airborne frames of real testing - _grabbedCliffLede (set purely by
        // OnTriggerEnter2D's own Unity physics layer filtering, no Penitent-ownership logic
        // involved at all) stayed null the entire session. A raw Resources.Load<Penitent>(...)
        // instantiate carries whatever layer the *prefab asset* has serialized in the editor -
        // P1's real layer, by contrast, is whatever the game's own live spawn/init systems assign
        // it to at runtime, which is not guaranteed to be the same value (an old comment further
        // below in this method assumed they matched - "both full Penitent clones on the same
        // physics layer" - that assumption was never actually verified and this is likely why).
        // Unity layers aren't inherited by children, so this has to walk the whole hierarchy, not
        // just the root - copies P1's real layer (and every child's layer) onto every one of P2's
        // own objects so P2 gets the exact same collision-matrix treatment P1 does for anything
        // layer-filtered (cliff-lede triggers being the concrete case proven broken so far).
        // Round 54: per-node copy now, not a single blanket root value - see CopyLayersRecursively
        // and the comment on OnLevelLoaded above for why (P1's own children aren't all on the same
        // layer as P1's own root, e.g. "Attack Area" is on "Water" - a blanket stamp silently wiped
        // that out for P2 and caused real contact-damage cross-talk).
        CopyLayersRecursively(p1.transform, Player2.transform);

        // P2's input mode (keyboard/gamepad) and the P1/gamepad exclusivity that goes with it
        // are handled every frame by Player2Input.Tick() (see GamePatches.cs, called from
        // PlatformCharacterInput_Update_Patch) - nothing to do here at spawn time.

        // P2 doesn't have a shared health pool with P1 yet, so it's made invulnerable for now
        // instead of wiring up a second death/respawn flow (see Modding/NOTES.md) - but by
        // patching PenitentDamageArea.TakeDamage (see GamePatches) rather than destroying the
        // component outright. ~108 places in the game's own code call methods on
        // Penitent.DamageArea assuming it always exists (Dash's cast start/end among them),
        // and destroying it turned every one of those into a live landmine for P2.

        // The "Core/Penitent" prefab ships with GravityScale at whatever the editor left it
        // at (effectively 0) - LevelManager.LoadLevelRoutine only ever sets it to 3 on
        // Core.Logic.Penitent after the level finishes loading, so a cloned P2 never gets
        // real gravity unless we set it ourselves too.
        Player2.PlatformCharacterController.PlatformCharacterPhysics.GravityScale = 3f;

        // One-time (per save slot) clone of P1's current progression (life level, damage/flask
        // upgrades, etc) onto P2, persisted so it never happens again after the first sync for
        // this save - see Player2StatsSync's own comment in GamePatches.cs for the full reasoning.
        Player2StatsSync.EnsureSynced(p1, Player2);

        // P1 and P2 are now both on the same physics layer (see SetLayerRecursively above), each
        // with a real Rigidbody2D (Penitent.RigidBody) - so by default they're solid to each
        // other under Unity's own automatic collision resolution, same as any two colliders
        // normally would be. Disabling collision between every Collider2D pair on the two
        // characters removes that specific interaction (harmless to keep - trigger-based
        // colliders like damage areas/cliff-lede grabs never physically resolve in the first
        // place either way).
        Collider2D[] p1Colliders = p1.GetComponentsInChildren<Collider2D>(includeInactive: true);
        Collider2D[] p2Colliders = Player2.GetComponentsInChildren<Collider2D>(includeInactive: true);
        foreach (Collider2D p1Collider in p1Colliders)
        {
            foreach (Collider2D p2Collider in p2Colliders)
            {
                Physics2D.IgnoreCollision(p1Collider, p2Collider, true);
            }
        }

        // A per-layer fix (Physics2D.IgnoreLayerCollision(penitentLayer, penitentLayer, true) +
        // forcing CreativeSpore.SmartColliders' cached LayerCollision masks to rebuild - see git
        // history / NOTES.md "Ronda 12" for the full reasoning, and why it looked promising: the
        // game's actual character movement/collision runs through that custom raycast-based
        // asset, not Unity's Rigidbody2D physics, so plain Physics2D.IgnoreCollision above never
        // touches it) was tried here and reverted - confirmed by the user to break collision
        // against level geometry entirely. p1.gameObject.layer is evidently not a Penitent-only
        // layer - something in level geometry shares it - so disabling that layer's self-
        // collision project-wide took the floor/walls out from under both players too. Do not
        // reintroduce this without first confirming (via a diagnostic log of the layer's actual
        // index/name and what else uses it) that the layer is safe to touch, or finding a more
        // surgical way to separate P1 and P2 specifically instead of an entire layer.

        AddNameLabel(p1, "Pan-chan", new Color32(235, 36, 10,255), outlineColor: new Color32(140, 14, 0, 190));
        AddNameLabel(Player2, "Baby", new Color32(55, 247, 70,255), outlineColor: new Color32(39, 94, 73,190));

        // Snapshot both players' clean (non-mud) movement stats now, before either could
        // possibly have touched a MudAreaEffect - see GamePatches for why this is needed.
        MudAreaEffect_OnExitAreaEffect_Patch.RememberBaseline(p1);
        MudAreaEffect_OnExitAreaEffect_Patch.RememberBaseline(Player2);

        // Register P2 as a second camera target immediately - covers respawns that don't also
        // trigger a full CameraManager.UpdateNewCameraParams() reset (see GamePatches).
        if (CameraManager.Instance != null)
        {
            CameraManager_UpdateNewCameraParams_Patch.AddPlayer2AsCameraTarget(CameraManager.Instance.ProCamera2D);
        }

        // P2 now has its own real health pool (see PenitentDamageArea patches in GamePatches) -
        // give it its own HUD health bar too, cloned from P1's.
        Player2HealthBar.EnsureCreated(Player2);

        // Same clone-and-redirect treatment for Fervour, per the user's request.
        Player2FervourBar.EnsureCreated(Player2);

        // Same again for the currency (Purge/Tears) counter.
        Player2PurgePoints.EnsureCreated(Player2);

        // Health must render above Fervour's group (which brings the whole portrait/frame along
        // with it) - see Player2HealthBar.BringToFront()'s own comment for why this has to run
        // after all three exist rather than from inside Health's own EnsureCreated.
        Player2HealthBar.BringToFront();

        // Round 56: all three HUD clones above were just (re)created fresh and default to active -
        // but this whole method can run *during* a room transition's fade to black (OnPlayerSpawn
        // fires mid-load). If a fade is already covering the screen right now, hide the brand new
        // clones immediately instead of letting them flash visible until the matching
        // FadeWidget.OnFadeHidedEnd fires - see Player2HudFadeSync's own class comment.
        Player2HudFadeSync.ApplyCurrentFadeState();

        // Ronda 48 - debug visual: a translucent overlay tracking P2's real damage hitbox
        // (PenitentDamageArea's own BoxCollider2D, resized live by ResizeDamageArea() while
        // crouching/dashing/etc) so it's visible in-game exactly where a hit will actually land,
        // instead of guessing from the sprite.
        // Disabled for now (debug-only feature, not meant to ship visible to players) - the
        // Player2HitboxVisualizer class itself is left untouched so this is a one-line revert.
        // Player2HitboxVisualizer.EnsureCreated(Player2);

        ModLog.Info(
            $"P2 spawned at {spawnPosition} (p1 was at {p1.transform.position}, offset={P2SpawnOffset}, " +
            $"actual P2 pos after spawn={Player2.transform.position})",
            this);
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
