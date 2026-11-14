using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace Blasphemous.CoopLocal;

/// <summary>
/// Limpieza de los clones de HUD de P2-P4. Las barras de fervor/vida se clonan instanciando el contenedor PADRE del
/// componente vanilla; si ese contenedor agrupa más cosas (p. ej. la barra de vida de P1 junto al fervor), el clon de
/// fervor arrastraba una copia de la barra de vida roja + decoración que se dibujaba ENCIMA de las barras de vida de P2-P4
/// (P1 no lo sufre porque su vida es la original). Aquí se eliminan del clon los sub-árboles ajenos.
/// </summary>
internal static class HudCloneCleaner
{
    private static readonly HashSet<string> dumped = new HashSet<string>();

    /// <summary>
    /// Conserva SOLO los hijos directos del clon cuyo nombre esté en <paramref name="keepNames"/> y destruye el resto.
    /// Whitelist en vez de lista negra: no depende de qué más trajera el contenedor vanilla. Los hijos conservados mantienen
    /// sus referencias serializadas entre sí (PlayerFervour -> PrayerDetail/Spark/PrayerTimer...), que Instantiate remapea al clon.
    /// Devuelve cuántos se destruyeron, o -1 (sin tocar nada) si falta <paramref name="requiredName"/>: así un cambio de
    /// nombres en una versión futura del juego degrada al método antiguo en vez de dejar un clon roto.
    /// </summary>
    internal static int KeepOnlyChildren(GameObject cloneRoot, string[] keepNames, string requiredName)
    {
        if (cloneRoot == null) return -1;
        Transform root = cloneRoot.transform;
        if (root.Find(requiredName) == null) return -1;

        var keep = new HashSet<string>(keepNames);
        var remove = new List<GameObject>();
        for (int i = 0; i < root.childCount; i++)
        {
            Transform c = root.GetChild(i);
            if (!keep.Contains(c.name)) remove.Add(c.gameObject);
        }
        foreach (GameObject go in remove)
        {
            go.SetActive(false);
            UnityEngine.Object.Destroy(go);
        }
        return remove.Count;
    }

    /// <summary>
    /// Elimina del clon los sub-árboles que contienen componentes de tipo <paramref name="foreign"/> y ninguno de <paramref name="keep"/>.
    /// Devuelve cuántos sub-árboles se quitaron.
    /// </summary>
    internal static int StripForeign(GameObject cloneRoot, Type[] foreign, Type[] keep)
    {
        if (cloneRoot == null) return 0;
        int removed = 0;
        var toRemove = new List<GameObject>();

        foreach (Type ft in foreign)
        {
            Component[] found = cloneRoot.GetComponentsInChildren(ft, true);
            foreach (Component c in found)
            {
                if (c == null) continue;
                // Subir hasta el ancestro más alto (por debajo de la raíz del clon) que no contenga nada de lo que hay que conservar.
                Transform top = c.transform;
                if (top == cloneRoot.transform) continue; // el propio clon es de este tipo: no tocar
                while (top.parent != null && top.parent != cloneRoot.transform && !ContainsAny(top.parent, keep))
                {
                    top = top.parent;
                }
                if (ContainsAny(top, keep)) continue; // no se puede aislar sin llevarse lo que sí hay que conservar
                if (!toRemove.Contains(top.gameObject)) toRemove.Add(top.gameObject);
            }
        }

        foreach (GameObject go in toRemove)
        {
            if (go == null) continue;
            go.SetActive(false);
            UnityEngine.Object.Destroy(go);
            removed++;
        }
        return removed;
    }

    private static bool ContainsAny(Transform t, Type[] types)
    {
        foreach (Type k in types)
        {
            if (t.GetComponentInChildren(k, true) != null) return true;
        }
        return false;
    }

    /// <summary>Vuelca (una sola vez por etiqueta y sesión) la jerarquía de un objeto al log para diagnosticar clones.</summary>
    internal static void DumpOnce(string tag, Transform root, int maxDepth = 6)
    {
        if (root == null || !dumped.Add(tag)) return;
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine($"[HudDump] {tag}");
            Walk(root, 0, maxDepth, sb);
            DashParryDebugLog.Log(sb.ToString());
        }
        catch (Exception e) { DashParryDebugLog.Log($"[HudDump] {tag}: {e.Message}"); }
    }

    private static void Walk(Transform t, int depth, int maxDepth, StringBuilder sb)
    {
        var comps = new StringBuilder();
        foreach (Component c in t.GetComponents<Component>())
        {
            if (c == null || c is Transform) continue;
            comps.Append(c.GetType().Name);
            if (c is Image img) comps.Append("(").Append(img.sprite != null ? img.sprite.name : "null").Append(img.type == Image.Type.Filled ? ",fill" : "").Append(")");
            comps.Append(' ');
        }
        sb.Append(' ', depth * 2).Append(t.name).Append(t.gameObject.activeSelf ? "" : " [off]").Append(" : ").AppendLine(comps.ToString());
        if (depth >= maxDepth) return;
        for (int i = 0; i < t.childCount; i++) Walk(t.GetChild(i), depth + 1, maxDepth, sb);
    }
}
