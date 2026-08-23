using CreativeSpore.SmartColliders;
using Framework.Managers;
using Gameplay.GameControllers.Camera;
using Gameplay.GameControllers.Penitent;
using ModdingAPI;
using UnityEngine;

namespace Blasphemous.CoopLocal;

public class CoopLocal : Mod
{
    // Offset from P1 where P2 spawns/respawns, in world units.
    private static readonly Vector3 P2SpawnOffset = new Vector3(1.5f, 0f, 0f);

    // "Rojo vino tinto" (wine/burgundy red) for P1's name label - #722F37.
    private static readonly Color WineRed = new Color(0.447f, 0.184f, 0.216f);

    // Exposed so GamePatches can tell P2's PlatformCharacterInput apart from P1's.
    internal static Penitent Player2 { get; private set; }

    internal CoopLocal() : base(ModInfo.MOD_ID, ModInfo.MOD_NAME, ModInfo.MOD_VERSION) { }

    protected override void Initialize()
    {
        SpawnManager.OnPlayerSpawn += OnPlayerSpawn;
    }

    protected override void Dispose()
    {
        SpawnManager.OnPlayerSpawn -= OnPlayerSpawn;
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
            Object.Destroy(Player2.gameObject);
            Player2 = null;
        }

        Penitent p2Prefab = Resources.Load<Penitent>("Core/Penitent");
        Vector3 spawnPosition = p1.transform.position + P2SpawnOffset;
        Player2 = Object.Instantiate(p2Prefab, spawnPosition, Quaternion.identity);

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

        // P1 and P2 are both full Penitent clones on the same physics layer, each with a real
        // Rigidbody2D (Penitent.RigidBody) - so by default they're solid to each other under
        // Unity's own automatic collision resolution, same as any two colliders normally would
        // be. Disabling collision between every Collider2D pair on the two characters removes
        // that specific interaction (harmless to keep - trigger-based colliders like damage
        // areas/cliff-lede grabs never physically resolve in the first place either way).
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

        AddNameLabel(p1, "Pan-chan", WineRed, outlineColor: Color.white);
        AddNameLabel(Player2, "Baby", new Color(0.35f, 0.85f, 0.35f));

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

        // Stopgap while the cloned HUD bar (above) is still visually broken (see Modding/NOTES.md,
        // ronda 30) - simple in-world life/Fervour bars floating over P2's own head, built from a
        // single runtime-generated white pixel sprite (no game assets needed) instead of depending
        // on anything from PlayerHealth/PlayerFervour's UI hierarchy.
        Player2StatusBars.EnsureCreated(Player2);

        Log($"P2 spawned at {spawnPosition}");
    }

    private const string NameLabelChildName = "CoopLocalNameLabel";

    private static void AddNameLabel(Penitent penitent, string label, Color color, Color? outlineColor = null)
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
            // Legacy TextMesh has no built-in outline/glow support (that needs a custom
            // shader/material) - faked with 8 duplicate copies of the same text, offset by a tiny
            // amount in a ring around the real text and drawn one sorting order behind it. Classic
            // "poor man's outline" trick, no new assets required.
            const float offset = 0.045f;
            Vector3[] outlineOffsets =
            {
                new Vector3(-offset, 0f, 0f), new Vector3(offset, 0f, 0f),
                new Vector3(0f, -offset, 0f), new Vector3(0f, offset, 0f),
                new Vector3(-offset, -offset, 0f), new Vector3(-offset, offset, 0f),
                new Vector3(offset, -offset, 0f), new Vector3(offset, offset, 0f),
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

    private static void CreateLabelTextMesh(GameObject target, string label, Color color, int sortingOrder)
    {
        TextMesh textMesh = target.AddComponent<TextMesh>();
        textMesh.text = label;
        textMesh.characterSize = 0.08f;
        textMesh.fontSize = 48;
        textMesh.anchor = TextAnchor.LowerCenter;
        textMesh.alignment = TextAlignment.Center;
        textMesh.color = color;

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

// Simple floating life/Fervour bars above P2's head - a stopgap for as long as the cloned HUD bar
// (Player2HealthBar, GamePatches.cs) stays visually broken. Deliberately doesn't reuse any of
// PlayerHealth/PlayerFervour's UI Image/RectTransform machinery (that's exactly what's been buggy)
// - instead builds two bars from a single runtime-generated 1x1 white pixel Sprite, with its pivot
// pinned to the left edge so scaling a bar's own transform.localScale.x grows/shrinks it from a
// fixed left anchor, the same trick a screen-space fill bar gets from Image.fillMethod, without
// needing Canvas/UI at all.
internal class Player2StatusBars : MonoBehaviour
{
    private const string ChildName = "CoopLocalStatusBars";
    private const float BarWidth = 1.3f;
    private const float BarHeight = 0.12f;
    private static readonly Vector3 LifeBarLocalPosition = new Vector3(0f, 2.05f, 0f);
    private static readonly Vector3 FervourBarLocalPosition = new Vector3(0f, 1.88f, 0f);

    private static Sprite barSprite;

    private SpriteRenderer lifeFill;
    private SpriteRenderer fervourFill;

    internal static void EnsureCreated(Penitent p2)
    {
        if (p2.transform.Find(ChildName) != null)
        {
            return;
        }

        GameObject root = new GameObject(ChildName);
        root.transform.SetParent(p2.transform, worldPositionStays: false);
        root.AddComponent<KeepLocalTransform>();

        Player2StatusBars bars = root.AddComponent<Player2StatusBars>();
        // Wine-red life bar, gold Fervour bar - matches the game's own Fervour color convention,
        // and pairs with P1's own wine-red name label without the two being literally the same tag.
        bars.lifeFill = CreateBar(root.transform, LifeBarLocalPosition, new Color(0.75f, 0.12f, 0.12f));
        bars.fervourFill = CreateBar(root.transform, FervourBarLocalPosition, new Color(0.95f, 0.78f, 0.2f));
    }

    private static SpriteRenderer CreateBar(Transform parent, Vector3 localPosition, Color fillColor)
    {
        GameObject background = new GameObject("Background");
        background.transform.SetParent(parent, worldPositionStays: false);
        background.transform.localPosition = localPosition;
        background.transform.localScale = new Vector3(BarWidth, BarHeight, 1f);
        SpriteRenderer backgroundRenderer = background.AddComponent<SpriteRenderer>();
        backgroundRenderer.sprite = GetBarSprite();
        backgroundRenderer.color = new Color(0f, 0f, 0f, 0.6f);
        backgroundRenderer.sortingLayerName = "Player";
        backgroundRenderer.sortingOrder = 99;

        GameObject fill = new GameObject("Fill");
        fill.transform.SetParent(parent, worldPositionStays: false);
        fill.transform.localPosition = localPosition;
        fill.transform.localScale = new Vector3(BarWidth, BarHeight, 1f);
        SpriteRenderer fillRenderer = fill.AddComponent<SpriteRenderer>();
        fillRenderer.sprite = GetBarSprite();
        fillRenderer.color = fillColor;
        fillRenderer.sortingLayerName = "Player";
        fillRenderer.sortingOrder = 100;

        return fillRenderer;
    }

    // Pivot at (0, 0.5) - left-center - means the GameObject's own position is already the bar's
    // left edge, so scaling transform.localScale.x shrinks/grows the sprite towards/away from that
    // fixed left anchor instead of from the center, without needing any position offset math.
    private static Sprite GetBarSprite()
    {
        if (barSprite == null)
        {
            Texture2D texture = new Texture2D(1, 1);
            texture.SetPixel(0, 0, Color.white);
            texture.Apply();
            barSprite = Sprite.Create(texture, new Rect(0f, 0f, 1f, 1f), new Vector2(0f, 0.5f), 1f);
        }
        return barSprite;
    }

    private void Update()
    {
        Penitent p2 = CoopLocal.Player2;
        if (p2 == null)
        {
            return;
        }
        SetFillRatio(lifeFill, p2.Stats.Life.Final > 0f ? p2.Stats.Life.Current / p2.Stats.Life.Final : 0f);
        SetFillRatio(fervourFill, p2.Stats.Fervour.CurrentMaxWithoutFactor > 0f
            ? p2.Stats.Fervour.Current / p2.Stats.Fervour.CurrentMaxWithoutFactor
            : 0f);
    }

    private static void SetFillRatio(SpriteRenderer fill, float ratio)
    {
        if (fill == null)
        {
            return;
        }
        fill.transform.localScale = new Vector3(BarWidth * Mathf.Clamp01(ratio), BarHeight, 1f);
    }
}
