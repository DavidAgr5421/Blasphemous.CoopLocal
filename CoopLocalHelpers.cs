using System.Collections.Generic;

using CreativeSpore.SmartColliders;

using Framework.FrameworkCore;

using Framework.Managers;

using Gameplay.GameControllers.Camera;

using Gameplay.GameControllers.Penitent;

using Blasphemous.ModdingAPI;

using UnityEngine;



namespace Blasphemous.CoopLocal;

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
