using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Blasphemous.ModdingAPI;
using HarmonyLib;

namespace Blasphemous.CoopLocal;

/// <summary>
/// Autotest de arranque que verifica que todos los [HarmonyPatch] del ensamblado
/// resuelven su método objetivo, están registrados en Harmony y sus parámetros de
/// Prefix/Postfix/Finalizer existen en el método vanilla.
/// </summary>
internal static class PatchSelfTest
{
    private static readonly HashSet<string> HarmonySpecialParams = new HashSet<string>(StringComparer.Ordinal)
    {
        "__instance",
        "__result",
        "__state",
        "__args",
        "__originalMethod",
        "__runOriginal"
    };

    internal static void RunAll()
    {
        int okCount = 0;
        int failCount = 0;

        try
        {
            Type[] allTypes = typeof(PatchSelfTest).Assembly.GetTypes();
            foreach (Type type in allTypes)
            {
                if (type == null) continue;
                var patchAttrs = type.GetCustomAttributes(typeof(HarmonyPatch), inherit: false);
                if (patchAttrs == null || patchAttrs.Length == 0) continue;

                string targetLabel = "?";
                try
                {
                    if (ValidatePatchType(type, out targetLabel, out string reason))
                    {
                        okCount++;
                        LogInfo($"[PatchCheck] OK {type.Name} -> {targetLabel}: {reason}");
                    }
                    else
                    {
                        failCount++;
                        LogError($"[PatchCheck] FAIL {type.Name} -> {targetLabel}: {reason}");
                    }
                }
                catch (Exception ex)
                {
                    failCount++;
                    LogError($"[PatchCheck] FAIL {type.Name} -> {targetLabel}: excepción al validar ({ex.Message})");
                }
            }

            LogInfo($"[PatchCheck] {okCount} ok, {failCount} fallidos");
        }
        catch (Exception ex)
        {
            LogError($"[PatchCheck] Error general en PatchSelfTest.RunAll: {ex}");
        }
    }

    private static bool ValidatePatchType(Type patchType, out string targetLabel, out string reason)
    {
        targetLabel = "?";
        List<MethodBase> targets = ResolveTargetMethods(patchType, out string resolveError);
        if (targets == null || targets.Count == 0)
        {
            reason = resolveError ?? "no se pudo resolver método objetivo";
            return false;
        }

        var labels = new List<string>();
        for (int t = 0; t < targets.Count; t++)
        {
            MethodBase target = targets[t];
            if (target == null)
            {
                targetLabel = string.Join(", ", labels.ToArray());
                reason = $"TargetMethod(s) devolvió un método null (índice {t})";
                return false;
            }
            string declName = target.DeclaringType != null ? target.DeclaringType.FullName : "?";
            labels.Add($"{declName}.{target.Name}");
        }
        targetLabel = labels.Count == 1 ? labels[0] : $"{labels.Count} métodos ({labels[0]}, ...)";

        MethodInfo[] patchMethods = GetPatchHookMethods(patchType);
        for (int t = 0; t < targets.Count; t++)
        {
            MethodBase target = targets[t];
            Patches patchInfo = Harmony.GetPatchInfo(target);
            if (patchInfo == null ||
                ((patchInfo.Prefixes == null || patchInfo.Prefixes.Count == 0) &&
                 (patchInfo.Postfixes == null || patchInfo.Postfixes.Count == 0) &&
                 (patchInfo.Transpilers == null || patchInfo.Transpilers.Count == 0) &&
                 (patchInfo.Finalizers == null || patchInfo.Finalizers.Count == 0)))
            {
                reason = $"Harmony.GetPatchInfo no reporta parches activos en {labels[t]}";
                return false;
            }

            // Verificar nombres de parámetros en Prefix / Postfix / Finalizer contra vanilla (por cada método objetivo)
            ParameterInfo[] targetParams = target.GetParameters();
            var validParamNames = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < targetParams.Length; i++)
            {
                if (!string.IsNullOrEmpty(targetParams[i].Name))
                {
                    validParamNames.Add(targetParams[i].Name);
                }
            }

            for (int i = 0; i < patchMethods.Length; i++)
            {
                MethodInfo pm = patchMethods[i];
                ParameterInfo[] pmParams = pm.GetParameters();
                for (int j = 0; j < pmParams.Length; j++)
                {
                    string pName = pmParams[j].Name;
                    if (string.IsNullOrEmpty(pName)) continue;
                    if (HarmonySpecialParams.Contains(pName)) continue;
                    if (pName.StartsWith("__", StringComparison.Ordinal)) continue;

                    if (!validParamNames.Contains(pName))
                    {
                        reason = $"parámetro '{pName}' en {pm.Name} no existe en {labels[t]} ({string.Join(", ", validParamNames.ToArray())})";
                        return false;
                    }
                }
            }
        }

        reason = targets.Count == 1 ? "parcheado" : $"parcheado ({targets.Count} métodos)";
        return true;
    }

    private static List<MethodBase> ResolveTargetMethods(Type patchType, out string error)
    {
        error = null;
        const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        // 1. TargetMethods() plural (IEnumerable<MethodBase>)
        MethodInfo pluralFactory = patchType.GetMethod("TargetMethods", flags);
        if (pluralFactory != null && pluralFactory.GetParameters().Length == 0)
        {
            var list = new List<MethodBase>();
            if (pluralFactory.Invoke(null, null) is System.Collections.IEnumerable en)
            {
                foreach (object o in en) list.Add(o as MethodBase);
            }
            if (list.Count == 0) error = "TargetMethods() no devolvió métodos";
            return list;
        }

        // 2. TargetMethod() singular
        MethodBase single = ResolveTargetMethod(patchType, out error);
        if (single == null) return null;
        return new List<MethodBase> { single };
    }

    private static MethodBase ResolveTargetMethod(Type patchType, out string error)
    {
        error = null;

        MethodInfo targetMethodFactory = patchType.GetMethod("TargetMethod",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        if (targetMethodFactory != null && targetMethodFactory.GetParameters().Length == 0)
        {
            MethodBase r = targetMethodFactory.Invoke(null, null) as MethodBase;
            if (r == null) error = "TargetMethod() devolvió null";
            return r;
        }

        // Combinar metadatos de [HarmonyPatch]
        List<HarmonyMethod> infos = HarmonyMethodExtensions.GetFromType(patchType);
        if (infos == null || infos.Count == 0)
        {
            error = "sin metadatos HarmonyMethod";
            return null;
        }

        HarmonyMethod merged = HarmonyMethod.Merge(infos);
        if (merged.declaringType == null)
        {
            error = "declaringType es null";
            return null;
        }

        // Harmony real usa Declared* (sin heredados); fallback a la variante con herencia sólo si no existe declarado.
        MethodType mType = merged.methodType ?? MethodType.Normal;
        switch (mType)
        {
            case MethodType.Getter:
                if (string.IsNullOrEmpty(merged.methodName))
                {
                    error = "methodName vacío para Getter";
                    return null;
                }
                return AccessTools.DeclaredPropertyGetter(merged.declaringType, merged.methodName)
                    ?? AccessTools.PropertyGetter(merged.declaringType, merged.methodName);

            case MethodType.Setter:
                if (string.IsNullOrEmpty(merged.methodName))
                {
                    error = "methodName vacío para Setter";
                    return null;
                }
                return AccessTools.DeclaredPropertySetter(merged.declaringType, merged.methodName)
                    ?? AccessTools.PropertySetter(merged.declaringType, merged.methodName);

            case MethodType.Constructor:
                return AccessTools.Constructor(merged.declaringType, merged.argumentTypes);

            case MethodType.StaticConstructor:
                return merged.declaringType.TypeInitializer;

            default:
                if (string.IsNullOrEmpty(merged.methodName))
                {
                    error = "methodName vacío";
                    return null;
                }
                return AccessTools.DeclaredMethod(merged.declaringType, merged.methodName, merged.argumentTypes)
                    ?? AccessTools.Method(merged.declaringType, merged.methodName, merged.argumentTypes);
        }
    }

    private static MethodInfo[] GetPatchHookMethods(Type patchType)
    {
        var result = new List<MethodInfo>();
        MethodInfo[] methods = patchType.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
        for (int i = 0; i < methods.Length; i++)
        {
            MethodInfo m = methods[i];
            if (m.Name == "Prefix" || m.Name == "Postfix" || m.Name == "Finalizer" ||
                m.GetCustomAttributes(typeof(HarmonyPrefix), false).Length > 0 ||
                m.GetCustomAttributes(typeof(HarmonyPostfix), false).Length > 0 ||
                m.GetCustomAttributes(typeof(HarmonyFinalizer), false).Length > 0)
            {
                result.Add(m);
            }
        }
        return result.ToArray();
    }

    private static void LogInfo(string msg)
    {
        if (Main.CoopLocal != null)
        {
            ModLog.Info(msg, Main.CoopLocal);
        }
    }

    private static void LogError(string msg)
    {
        if (Main.CoopLocal != null)
        {
            ModLog.Error(msg, Main.CoopLocal);
        }
    }
}
