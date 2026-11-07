using System;
using Framework.FrameworkCore;
using Framework.Managers;
using Gameplay.GameControllers.Enemies.Framework.Attack;
using Gameplay.GameControllers.Enemies.Framework.Damage;
using Gameplay.GameControllers.Enemies.Framework.IA;
using Gameplay.GameControllers.Enemies.Framework.Physics;
using Gameplay.GameControllers.Entities;
using Gameplay.GameControllers.Penitent;
using HarmonyLib;
using UnityEngine;

namespace Blasphemous.CoopLocal.Combat;

/// <summary>
/// Gestor para invocar y reiniciar un enemigo "Dummy" de pruebas con vida exagerada e inmóvil.
/// Permite probar en tiempo real el daño de espada, rezos, barras de veneno/sangrado y números de daño.
/// </summary>
internal static class TestDummyManager
{
    private static TestDummyEnemy activeDummy;
    private static Sprite cachedDummySprite;

    public static void SpawnOrResetDummy()
    {
        if (Core.Logic?.Penitent == null) return;

        // Determinar jugador de referencia (P2 si existe y está en el suelo, sino P1)
        Penitent player = (CoopLocal.Player2 != null && CoopLocal.Player2.Status.IsGrounded)
            ? CoopLocal.Player2
            : Core.Logic.Penitent;

        if (player == null) return;

        float facing = (player.Status.Orientation == EntityOrientation.Left) ? -1f : 1f;
        Vector3 spawnPos = player.transform.position + new Vector3(facing * 2.2f, 0f, 0f);

        // Si ya existe un dummy en la sala actual, reposicionarlo y reiniciar su vida
        if (activeDummy != null && activeDummy.gameObject != null)
        {
            activeDummy.transform.position = spawnPos;
            activeDummy.ResetHealth();
            activeDummy.FlashWhite();
            Core.Audio?.PlayOneShot("event:/SFX/UI/EquipItem");
            if (Main.CoopLocal != null)
            {
                Blasphemous.ModdingAPI.ModLog.Info($"[TestDummy] Muñeco de pruebas reposicionado frente al jugador en {spawnPos}. Vida restaurada.", Main.CoopLocal);
            }
            FloatingDamageNumber.Spawn(spawnPos + new Vector3(0f, 2.5f, 0f), "¡DUMMY LISTO!", Color.yellow, isLarge: true);
            return;
        }

        // Crear nuevo Dummy Enemy
        int enemyLayer = ResolveEnemyLayer();

        GameObject dummyObj = new GameObject("DummyEnemy_Test");
        dummyObj.transform.position = spawnPos;
        dummyObj.layer = enemyLayer;

        // Física cinemática para que no caiga ni sea empujado por colisiones
        Rigidbody2D rb = dummyObj.AddComponent<Rigidbody2D>();
        rb.isKinematic = true;

        // Sprite visual estilizado de muñeco de entrenamiento (poste con diana)
        GameObject spriteObj = new GameObject("Sprite");
        spriteObj.transform.SetParent(dummyObj.transform, false);
        spriteObj.transform.localPosition = Vector3.zero;
        SpriteRenderer sr = spriteObj.AddComponent<SpriteRenderer>();
        sr.sprite = GetDummySprite();
        sr.color = Color.white;
        sr.sortingOrder = 5;

        // Comportamiento inerte requerido por Enemy (no se mueve, no ataca)
        DummyEnemyBehaviour behaviour = dummyObj.AddComponent<DummyEnemyBehaviour>();
        behaviour.EnableBehaviourOnLoad = false;

        // Componente principal de Enemy
        TestDummyEnemy dummy = dummyObj.AddComponent<TestDummyEnemy>();
        dummy.SetSpriteRenderer(sr);

        // Sub-objeto DamageArea con Collider para recibir golpes de espada y ataques
        GameObject dmgAreaObj = new GameObject("DamageArea");
        dmgAreaObj.transform.SetParent(dummyObj.transform, false);
        dmgAreaObj.transform.localPosition = Vector3.zero;
        dmgAreaObj.layer = enemyLayer;

        BoxCollider2D boxCol = dmgAreaObj.AddComponent<BoxCollider2D>();
        boxCol.isTrigger = true;
        boxCol.size = new Vector2(1.2f, 2.4f);
        boxCol.offset = new Vector2(0f, 1.2f);

        EnemyDamageArea dmgArea = dmgAreaObj.AddComponent<EnemyDamageArea>();
        dmgArea.SetOwner(dummy);

        var colliderField = AccessTools.Field(typeof(DamageArea), "damageAreaCollider");
        if (colliderField != null)
        {
            colliderField.SetValue(dmgArea, boxCol);
        }

        AccessTools.Field(typeof(Entity), "entityDamageArea")?.SetValue(dummy, dmgArea);
        dummy.SetDamageArea(dmgArea);

        // Configuración de vida masiva excesiva (10,000,000 HP) para pruebas de escalado elemental
        dummy.Setup(10000000f);
        dummyObj.AddComponent<EnemyElementalStatusController>();
        activeDummy = dummy;

        Core.Audio?.PlayOneShot("event:/SFX/UI/EquipItem");
        FloatingDamageNumber.Spawn(spawnPos + new Vector3(0f, 2.5f, 0f), "¡DUMMY SPAWNED!", Color.yellow, isLarge: true);

        if (Main.CoopLocal != null)
        {
            Blasphemous.ModdingAPI.ModLog.Info($"[TestDummy] Muñeco de pruebas creado en {spawnPos} con 10,000,000 HP (Layer: {enemyLayer}).", Main.CoopLocal);
        }
    }

    private static int ResolveEnemyLayer()
    {
        if (Core.Logic?.Penitent?.AttackArea != null)
        {
            int mask = Core.Logic.Penitent.AttackArea.enemyLayerMask.value;
            for (int i = 0; i < 32; i++)
            {
                if ((mask & (1 << i)) != 0)
                {
                    return i;
                }
            }
        }
        int layer = LayerMask.NameToLayer("Enemy");
        if (layer >= 0) return layer;
        layer = LayerMask.NameToLayer("Damageable");
        if (layer >= 0) return layer;
        return 0;
    }

    private static Sprite GetDummySprite()
    {
        if (cachedDummySprite != null) return cachedDummySprite;

        int w = 32;
        int h = 64;
        Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Point;
        Color transparent = new Color(0f, 0f, 0f, 0f);
        Color woodDark = new Color(0.24f, 0.14f, 0.08f, 1f);
        Color woodMid = new Color(0.38f, 0.23f, 0.13f, 1f);
        Color woodLight = new Color(0.52f, 0.33f, 0.18f, 1f);
        Color sackDark = new Color(0.48f, 0.42f, 0.32f, 1f);
        Color sackMid = new Color(0.68f, 0.60f, 0.46f, 1f);
        Color sackLight = new Color(0.82f, 0.74f, 0.58f, 1f);
        Color rope = new Color(0.28f, 0.20f, 0.10f, 1f);
        Color targetRed = new Color(0.85f, 0.15f, 0.15f, 1f);
        Color targetWhite = new Color(0.92f, 0.90f, 0.85f, 1f);

        Color[] pixels = new Color[w * h];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = transparent;

        // Poste vertical central (x: 13 a 18, y: 0 a 60)
        for (int y = 0; y < 60; y++)
        {
            for (int x = 13; x <= 18; x++)
            {
                Color c = (x == 13 || x == 18) ? woodDark : ((x == 14 || x == 15) ? woodLight : woodMid);
                pixels[y * w + x] = c;
            }
        }

        // Base del poste (pie de madera x: 7 a 24, y: 0 a 4)
        for (int y = 0; y <= 4; y++)
        {
            for (int x = 7; x <= 24; x++)
            {
                pixels[y * w + x] = (y == 0 || x == 7 || x == 24) ? woodDark : woodMid;
            }
        }

        // Travesaño horizontal de los brazos (x: 3 a 28, y: 34 a 39)
        for (int y = 34; y <= 39; y++)
        {
            for (int x = 3; x <= 28; x++)
            {
                Color c = (y == 34 || y == 39 || x == 3 || x == 28) ? woodDark : woodMid;
                pixels[y * w + x] = c;
            }
        }

        // Cuerpo de saco / paja (x: 9 a 22, y: 20 a 48)
        for (int y = 20; y <= 48; y++)
        {
            for (int x = 9; x <= 22; x++)
            {
                bool border = (x == 9 || x == 22 || y == 20 || y == 48);
                Color c = border ? sackDark : ((x < 14) ? sackLight : sackMid);
                pixels[y * w + x] = c;
            }
        }

        // Cuerdas de atadura en el torso (y: 24, 25, 43, 44)
        for (int x = 9; x <= 22; x++)
        {
            pixels[24 * w + x] = rope;
            pixels[25 * w + x] = rope;
            pixels[43 * w + x] = rope;
            pixels[44 * w + x] = rope;
        }

        // Cabeza de saco (x: 11 a 20, y: 49 a 58)
        for (int y = 49; y <= 58; y++)
        {
            for (int x = 11; x <= 20; x++)
            {
                bool border = (x == 11 || x == 20 || y == 49 || y == 58);
                Color c = border ? sackDark : ((x < 15) ? sackLight : sackMid);
                pixels[y * w + x] = c;
            }
        }

        // Cuerda del cuello (y: 48, 49)
        for (int x = 12; x <= 19; x++)
        {
            pixels[48 * w + x] = rope;
        }

        // Diana roja circular en el pecho (centro en x: 15.5, y: 34)
        for (int y = 28; y <= 40; y++)
        {
            for (int x = 10; x <= 21; x++)
            {
                float dx = (x - 15.5f);
                float dy = (y - 34f);
                float dist = Mathf.Sqrt(dx * dx + dy * dy);
                if (dist <= 1.8f)
                {
                    pixels[y * w + x] = targetRed;
                }
                else if (dist <= 3.4f)
                {
                    pixels[y * w + x] = targetWhite;
                }
                else if (dist <= 5.0f)
                {
                    pixels[y * w + x] = targetRed;
                }
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();

        cachedDummySprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.0f), 32f);
        return cachedDummySprite;
    }
}

/// <summary>
/// Comportamiento IA inerte para el Dummy: no se mueve, no ataca, no busca objetivo.
/// </summary>
internal sealed class DummyEnemyBehaviour : EnemyBehaviour
{
    public override void Idle() { }
    public override void Wander() { }
    public override void Chase(Transform targetPosition) { }
    public override void Attack() { }
    public override void Damage() { }
    public override void StopMovement() { }
}

/// <summary>
/// Entidad Enemy de pruebas con vida exagerada y visualizador de HP en tiempo real.
/// </summary>
internal sealed class TestDummyEnemy : Enemy, IDamageable
{
    private SpriteRenderer sr;
    private TextMesh hpTextMesh;
    private GameObject hpLabelObj;
    private EnemyDamageArea damageArea;
    private float maxHp = 10000000f;
    private float lastLife;
    private float flashTimer;

    public override EnemyAttack EnemyAttack() => null;
    public override EnemyBumper EnemyBumper() => null;
    public override EnemyFloorChecker EnemyFloorChecker() => null;
    public Vector3 GetPosition() => transform.position;
    public override void EnablePhysics(bool enable = true) { }

    internal void SetSpriteRenderer(SpriteRenderer renderer)
    {
        sr = renderer;
    }

    internal void SetDamageArea(EnemyDamageArea area)
    {
        damageArea = area;
    }

    internal void Setup(float hp)
    {
        maxHp = hp;
        Id = "DUMMY_TEST";
        EntityName = "Muñeco de Pruebas";
        DamageByContact = false;
        CanGuard = false;
        IsParryable = false;
        IsVulnerable = true;
        UseHealthBar = false;
        bleedOnImpact = true;
        sparksOnImpact = true;

        if (Stats != null && Stats.Life != null)
        {
            Stats.Life.SetPermanentBonus(maxHp);
            Stats.Life.Current = maxHp;
            lastLife = maxHp;
        }

        CreateHpDisplay();
        UpdateHpText();
    }

    internal void ResetHealth()
    {
        if (Stats != null && Stats.Life != null)
        {
            Stats.Life.Current = maxHp;
            lastLife = maxHp;
            Status.Dead = false;
        }
        UpdateHpText();

        var ctrl = GetComponent<EnemyElementalStatusController>();
        if (ctrl != null)
        {
            ctrl.ResetAllStatuses();
        }
    }

    private void CreateHpDisplay()
    {
        if (hpLabelObj != null) return;

        hpLabelObj = new GameObject("DummyHpLabel");
        hpLabelObj.transform.SetParent(transform, false);
        hpLabelObj.transform.localPosition = new Vector3(0f, 2.1f, 0f);

        hpTextMesh = hpLabelObj.AddComponent<TextMesh>();
        hpTextMesh.characterSize = 0.09f;
        hpTextMesh.fontSize = 28;
        hpTextMesh.anchor = TextAnchor.LowerCenter;
        hpTextMesh.alignment = TextAlignment.Center;
        hpTextMesh.color = new Color(1f, 0.90f, 0.40f, 1f);

        MeshRenderer mr = hpLabelObj.GetComponent<MeshRenderer>();
        if (mr != null)
        {
            mr.sortingOrder = 30002;
        }
    }

    private void UpdateHpText()
    {
        if (hpTextMesh == null || Stats?.Life == null) return;
        float current = Mathf.Max(0f, Stats.Life.Current);
        hpTextMesh.text = $"HP: {current:N0} / {maxHp:N0}";
    }

    public void FlashWhite()
    {
        if (sr != null)
        {
            sr.color = new Color(2f, 2f, 2f, 1f);
            flashTimer = 0.12f;
        }
    }

    public void Damage(Hit hit)
    {
        if (Status.Dead) return;

        FlashWhite();
        float dmg = hit.DamageAmount;
        if (dmg > 0f)
        {
            Damage(dmg, hit.HitSoundId);
        }

        if (damageArea != null)
        {
            try { damageArea.TakeDamage(hit, false); } catch { }
        }

        if (EnemyDamageArea.OnDamagedGlobal != null)
        {
            EnemyDamageArea.OnDamagedGlobal(gameObject, hit);
        }

        Penitent attacker = hit.AttackingEntity?.GetComponent<Penitent>()
            ?? hit.AttackingEntity?.GetComponentInParent<Penitent>()
            ?? Core.Logic?.Penitent;
        if (attacker != null)
        {
            SwordElementalCombatBridge.TryApplyElementalStatus(this, attacker);
        }
    }

    public override void OnUpdate()
    {
        base.OnUpdate();
        // Actualizar destello de impacto
        if (flashTimer > 0f)
        {
            flashTimer -= Time.deltaTime;
            if (flashTimer <= 0f && sr != null)
            {
                sr.color = Color.white;
            }
        }

        // Mantener texto derecho si el padre llega a voltearse
        if (hpLabelObj != null)
        {
            float lossyX = transform.lossyScale.x;
            hpLabelObj.transform.localScale = new Vector3(lossyX < 0f ? -1f : 1f, 1f, 1f);
        }

        // Comprobar cambios en la vida
        if (Stats?.Life != null)
        {
            float current = Mathf.Max(0f, Stats.Life.Current);
            if (!Mathf.Approximately(current, lastLife))
            {
                lastLife = current;
                UpdateHpText();
            }

            // Si por alguna razón la vida llega a cero, revivir automáticamente
            if (current <= 0f)
            {
                Stats.Life.Current = maxHp;
                lastLife = maxHp;
                Status.Dead = false;
                UpdateHpText();
                FloatingDamageNumber.Spawn(transform.position + new Vector3(0f, 2.5f, 0f), "¡RESET HP!", Color.yellow, isLarge: true);
            }
        }
    }
}
