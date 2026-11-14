using System;
using Framework.FrameworkCore;
using Gameplay.GameControllers.Entities;
using Tools.Level.Layout;
using UnityEngine;

namespace Blasphemous.CoopLocal.MultiPlayer;

/// <summary>
/// Attached on the online client to every locally spawned (and neutralized) enemy.
/// Driven by host snapshots through EnemySyncClient (step B).
/// </summary>
public class CoopEnemyPuppet : MonoBehaviour
{
    private const float LerpSpeed = 20f;
    private const float SnapDistance = 5f;

    public uint NetId;
    public Vector2 TargetPos;
    public Enemy Enemy;
    public EnemySpawnPoint SpawnPoint;
    public bool IsBoss;
    public bool Alive = true;
    public int TargetAnimHash;
    public float TargetAnimNormTime;
    public bool TargetFacingLeft;
    public ushort Hp;

    private bool _hasTarget;
    private bool _hasOrientation;
    private bool _hasVisible;
    private bool _lastVisible;

    private void Update()
    {
        if (!_hasTarget || !MultiPlayer.CoopNetworkManager.IsOnlineClient) return;
        Vector3 cur = transform.position;
        Vector2 cur2 = new Vector2(cur.x, cur.y);
        Vector2 diff = TargetPos - cur2;
        if (diff.sqrMagnitude < 0.00001f) return;

        if (diff.magnitude > SnapDistance)
        {
            transform.position = new Vector3(TargetPos.x, TargetPos.y, cur.z);
            return;
        }
        Vector2 next = Vector2.Lerp(cur2, TargetPos, Mathf.Clamp01(LerpSpeed * Time.deltaTime));
        transform.position = new Vector3(next.x, next.y, cur.z);
    }

    internal void ApplyState(EnemyStateEntry e)
    {
        if (!Alive) return;
        if ((e.Flags & EnemyStateFlags.Alive) == 0)
        {
            Deactivate();
            return;
        }

        TargetPos = new Vector2(e.X, e.Y);
        _hasTarget = true;
        Hp = e.Hp;
        TargetFacingLeft = (e.Flags & EnemyStateFlags.FacingLeft) != 0;
        TargetAnimNormTime = e.AnimNormTime / 255f;

        if (Enemy == null) return;

        // HP: only the stat (health bars); never Damage/Kill.
        try { if (e.Hp > 0) Enemy.Stats.Life.Current = e.Hp; } catch (System.Exception __swallowed) { SwallowedExceptions.Report(__swallowed, "CoopEnemyPuppet.cs:69"); }

        // Orientation
        if (!_hasOrientation || TargetFacingLeft != ((Enemy.Status.Orientation) == EntityOrientation.Left))
        {
            _hasOrientation = true;
            try
            {
                Enemy.SetOrientation(TargetFacingLeft ? EntityOrientation.Left : EntityOrientation.Right);
            }
            catch (System.Exception __swallowed) { SwallowedExceptions.Report(__swallowed, "CoopEnemyPuppet.cs:79"); }
        }

        // Animation
        if (e.AnimShortHash != 0 && e.AnimShortHash != TargetAnimHash)
        {
            TargetAnimHash = e.AnimShortHash;
            try
            {
                Animator anim = Enemy.Animator;
                if (anim != null && anim.isActiveAndEnabled && anim.HasState(0, e.AnimShortHash))
                    anim.Play(e.AnimShortHash, 0, TargetAnimNormTime);
            }
            catch (System.Exception __swallowed) { SwallowedExceptions.Report(__swallowed, "CoopEnemyPuppet.cs:92"); }
        }

        // Visibility
        bool visible = (e.Flags & EnemyStateFlags.Visible) != 0;
        if (!_hasVisible || visible != _lastVisible)
        {
            _hasVisible = true;
            _lastVisible = visible;
            try { if (Enemy.SpriteRenderer != null) Enemy.SpriteRenderer.enabled = visible; } catch (System.Exception __swallowed) { SwallowedExceptions.Report(__swallowed, "CoopEnemyPuppet.cs:101"); }
        }
    }

    internal void ApplyFull(EnemyFullSyncEntry f)
    {
        if (!Alive) return;
        TargetPos = new Vector2(f.X, f.Y);
        _hasTarget = true;
        Hp = f.Hp;
        try { if (Enemy != null && f.Hp > 0) Enemy.Stats.Life.Current = f.Hp; } catch (System.Exception __swallowed) { SwallowedExceptions.Report(__swallowed, "CoopEnemyPuppet.cs:111"); }
    }

    internal void Deactivate()
    {
        Alive = false;
        try { gameObject.SetActive(false); } catch (System.Exception __swallowed) { SwallowedExceptions.Report(__swallowed, "CoopEnemyPuppet.cs:117"); }
    }
}
