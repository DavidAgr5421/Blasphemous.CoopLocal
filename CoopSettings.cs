using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace Blasphemous.CoopLocal;

/// <summary>
/// Configuración global del mod, en un único archivo de texto fácil de editar: <c>Modding/config/CoopLocal.cfg</c>
/// (junto al juego; si esa carpeta no es escribible, cae a persistentDataPath/CoopLocalMod). Se crea sola con los
/// valores por defecto la primera vez. Formato: <c>Clave = valor</c>, líneas con # son comentarios.
/// En la versión de usuario final TODAS las herramientas de depuración van desactivadas: ni funcionan sus teclas
/// ni se muestran sus textos en pantalla.
/// </summary>
internal static class CoopSettings
{
    private const string FileName = "CoopLocal.cfg";

    // Herramientas de depuración (todas OFF por defecto = versión final).
    // DebugMode es el interruptor maestro: con false, ignora el resto.
    internal static bool DebugMode { get; private set; }
    /// <summary>Editores de interfaz: F4 (tamaño de texto), F7 (HUD), F11 (posición/escala de menús).</summary>
    private static bool UiEditors => DebugMode && uiEditors;
    /// <summary>F10: cambiar el objetivo de la cámara (y su texto en pantalla).</summary>
    private static bool CameraChanger => DebugMode && cameraChanger;
    /// <summary>Herramientas de administración: F2 selector de jefes, F5 dummy, F6 fervor, F8 menú debug, F10+2/3/4 conectar/desconectar jugadores.</summary>
    private static bool AdminTools => DebugMode && adminTools;

    private static bool uiEditors, cameraChanger, adminTools;
    private static bool loaded;

    internal static void EnsureLoaded()
    {
        if (loaded) return;
        loaded = true;
        try
        {
            string path = FindExistingPath();
            if (path == null)
            {
                path = WriteDefaults();
            }
            if (path != null) Parse(File.ReadAllLines(path));
        }
        catch (Exception e) { Log("Error leyendo " + FileName + ": " + e.Message); }
        Log("Debug=" + DebugMode + " UiEditors=" + UiEditors
            + " CameraChanger=" + CameraChanger + " AdminTools=" + AdminTools);
    }

    // Los getters pueden usarse antes de Awake: aseguran la carga.
    internal static bool Ui { get { EnsureLoaded(); return UiEditors; } }
    internal static bool Camera { get { EnsureLoaded(); return CameraChanger; } }
    internal static bool Admin { get { EnsureLoaded(); return AdminTools; } }

    // ------------------------------------------------------------------
    private static IEnumerable<string> CandidateDirs()
    {
        var list = new List<string>();
        try { string b = AppDomain.CurrentDomain.BaseDirectory; if (!string.IsNullOrEmpty(b)) list.Add(Path.Combine(Path.Combine(b, "Modding"), "config")); } catch (Exception __swallowed) { SwallowedExceptions.Report(__swallowed, "CoopSettings.cs:dir1"); }
        try { string c = Directory.GetCurrentDirectory(); if (!string.IsNullOrEmpty(c)) list.Add(Path.Combine(Path.Combine(c, "Modding"), "config")); } catch (Exception __swallowed) { SwallowedExceptions.Report(__swallowed, "CoopSettings.cs:dir2"); }
        try { string p = Application.persistentDataPath; if (!string.IsNullOrEmpty(p)) list.Add(Path.Combine(p, "CoopLocalMod")); } catch (Exception __swallowed) { SwallowedExceptions.Report(__swallowed, "CoopSettings.cs:dir3"); }
        return list;
    }

    private static string FindExistingPath()
    {
        foreach (string dir in CandidateDirs())
        {
            string f = Path.Combine(dir, FileName);
            if (File.Exists(f)) return f;
        }
        return null;
    }

    private static string WriteDefaults()
    {
        foreach (string dir in CandidateDirs())
        {
            try
            {
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                string f = Path.Combine(dir, FileName);
                File.WriteAllText(f, DefaultText(), new UTF8Encoding(false));
                return f;
            }
            catch (Exception __swallowed) { SwallowedExceptions.Report(__swallowed, "CoopSettings.cs:WriteDefaults"); }
        }
        return null;
    }

    private static string DefaultText()
    {
        return string.Join("\r\n", new[]
        {
            "# ============================================================",
            "#  Blasphemous CoopLocal - configuracion global",
            "#  Edita los valores (true / false) y reinicia el juego.",
            "#  Las lineas que empiezan con # son comentarios.",
            "# ============================================================",
            "",
            "# --- HERRAMIENTAS DE DEPURACION ---------------------------------",
            "# Para jugar normalmente deja DebugMode = false: ninguna de las",
            "# herramientas de abajo funciona ni muestra textos en pantalla.",
            "# Interruptor maestro (si es false se ignora todo lo demas):",
            "DebugMode = false",
            "",
            "# Editores de interfaz: F4 tamano de texto, F7 HUD, F11 posicion/escala de menus",
            "UiEditors = true",
            "",
            "# F10: cambiar el objetivo de la camara (muestra un texto en pantalla)",
            "CameraChanger = true",
            "",
            "# Herramientas de administracion: F2 selector de jefes, F5 dummy de pruebas,",
            "# F6 fervor al maximo, F8 menu debug (habilidades/estadisticas),",
            "# F10+2/3/4 conectar o desconectar jugadores en caliente",
            "AdminTools = true",
            ""
        });
    }

    private static void Parse(string[] lines)
    {
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";")) continue;
            int eq = line.IndexOf('=');
            if (eq <= 0) continue;
            string key = line.Substring(0, eq).Trim();
            string val = line.Substring(eq + 1).Trim();
            int hash = val.IndexOf('#');
            if (hash >= 0) val = val.Substring(0, hash).Trim();
            bool b;
            if (!TryBool(val, out b)) continue;
            if (key.Equals("DebugMode", StringComparison.OrdinalIgnoreCase)) DebugMode = b;
            else if (key.Equals("UiEditors", StringComparison.OrdinalIgnoreCase)) uiEditors = b;
            else if (key.Equals("CameraChanger", StringComparison.OrdinalIgnoreCase)) cameraChanger = b;
            else if (key.Equals("AdminTools", StringComparison.OrdinalIgnoreCase)) adminTools = b;
        }
    }

    private static bool TryBool(string v, out bool result)
    {
        string s = v.ToLower(CultureInfo.InvariantCulture);
        if (s == "true" || s == "1" || s == "on" || s == "yes" || s == "si") { result = true; return true; }
        if (s == "false" || s == "0" || s == "off" || s == "no") { result = false; return true; }
        result = false;
        return false;
    }

    private static void Log(string msg)
    {
        try { if (Main.CoopLocal != null) Blasphemous.ModdingAPI.ModLog.Info("[CoopSettings] " + msg, Main.CoopLocal); }
        catch (Exception __swallowed) { SwallowedExceptions.Report(__swallowed, "CoopSettings.cs:Log"); }
    }
}
