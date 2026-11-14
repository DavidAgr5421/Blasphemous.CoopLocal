using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;

// net35 no trae los atributos de información de llamador; el compilador los reconoce por nombre completo.
namespace System.Runtime.CompilerServices
{
    [AttributeUsage(AttributeTargets.Parameter, Inherited = false)]
    internal sealed class CallerFilePathAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Parameter, Inherited = false)]
    internal sealed class CallerLineNumberAttribute : Attribute { }
}

namespace Blasphemous.CoopLocal
{
    /// <summary>
    /// Punto único de acceso por reflexión a los internos del juego (campos/métodos/propiedades privados).
    /// Mismo comportamiento que AccessTools, pero si un miembro NO existe (el juego se actualizó, o hay un error de nombre)
    /// lo registra inmediatamente y UNA sola vez, con el archivo y la línea de quien lo pidió, en vez de dar null en silencio
    /// y fallar más tarde con un NullReference lejos de la causa. <see cref="LogSummary"/> vuelca el resumen al cargar sala.
    /// </summary>
    internal static class Reflect
    {
        private static readonly List<string> missing = new List<string>();
        private static int lookups;
        private static bool summarized;

        internal static int Lookups => lookups;
        internal static int MissingCount { get { lock (missing) return missing.Count; } }

        internal static FieldInfo Field(Type type, string name, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
        {
            lookups++;
            FieldInfo f = type != null ? AccessTools.Field(type, name) : null;
            if (f == null) Miss("field", type, name, file, line);
            return f;
        }

        internal static MethodInfo Method(Type type, string name, Type[] parameters = null, Type[] generics = null,
            [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
        {
            lookups++;
            MethodInfo m = type != null ? AccessTools.Method(type, name, parameters, generics) : null;
            if (m == null) Miss("method", type, name, file, line);
            return m;
        }

        internal static PropertyInfo Property(Type type, string name, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
        {
            lookups++;
            PropertyInfo p = type != null ? AccessTools.Property(type, name) : null;
            if (p == null) Miss("property", type, name, file, line);
            return p;
        }

        internal static MethodInfo PropertyGetter(Type type, string name, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
        {
            lookups++;
            MethodInfo m = type != null ? AccessTools.PropertyGetter(type, name) : null;
            if (m == null) Miss("property getter", type, name, file, line);
            return m;
        }

        private static void Miss(string kind, Type type, string name, string file, int line)
        {
            string where = $"{System.IO.Path.GetFileName(file)}:{line}";
            string entry = $"{kind} {(type != null ? type.FullName : "<null type>")}.{name} (pedido en {where})";
            lock (missing) missing.Add(entry);
            DashParryDebugLog.LogOnce("reflect:" + entry, "[Reflect] NO ENCONTRADO: " + entry + " - posible cambio de versión del juego.", isWarning: true);
        }

        /// <summary>Escribe una sola vez el resumen de miembros no encontrados (llamar tras cargar la primera sala).</summary>
        internal static void LogSummary()
        {
            if (summarized) return;
            summarized = true;
            string[] items;
            lock (missing) items = missing.ToArray();
            DashParryDebugLog.Log($"[Reflect] {lookups} accesos por reflexión resueltos, {items.Length} sin encontrar.");
            foreach (string s in items) DashParryDebugLog.Log("[Reflect]   - " + s);
        }
    }
}
