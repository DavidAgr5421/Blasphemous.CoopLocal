using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;
using Version = System.Version;

namespace Blasphemous.CoopLocal;

/// <summary>
/// Comprobación de actualizaciones al abrir el juego. Consulta la última Release de GitHub; si hay una versión mayor
/// que la instalada, muestra un aviso en el menú (Y = actualizar, N = más tarde, S = no avisar de esta versión).
/// Al aceptar descarga el paquete y deja un script que lo instala cuando el juego se cierre (no se puede sustituir el
/// DLL mientras está en uso). Se desactiva con <c>AutoUpdate = false</c> en CoopLocal.cfg. Sin red: no hace nada.
/// </summary>
internal sealed class CoopUpdater : MonoBehaviour
{
    private const string LatestUrl = "https://codeberg.org/api/v1/repos/David542agr/BlasphemousCoopMod/releases/latest";
    private const float StartDelay = 4f;

    private enum State { Idle, Checking, Available, Downloading, Ready, Failed }

    private static CoopUpdater _instance;
    private State state = State.Idle;
    private string newVersion = "";
    private string assetUrl = "";
    private string message = "";
    private float messageUntil;
    private GUIStyle boxStyle, textStyle;

    internal static void EnsureCreated()
    {
        if (_instance != null) return;
        var go = new GameObject("CoopUpdater");
        DontDestroyOnLoad(go);
        _instance = go.AddComponent<CoopUpdater>();
    }

    private void Start()
    {
        if (CoopSettings.AutoUpdate) StartCoroutine(CheckRoutine());
    }

    // ------------------------------------------------------------------ comprobación
    private IEnumerator CheckRoutine()
    {
        yield return new WaitForSeconds(StartDelay);
        state = State.Checking;
        UnityWebRequest req = UnityWebRequest.Get(LatestUrl);
        req.SetRequestHeader("Accept", "application/vnd.github+json");
        req.SetRequestHeader("User-Agent", "CoopLocal-Updater");
        req.timeout = 15;
        yield return req.SendWebRequest();

        if (req.isNetworkError || req.isHttpError)
        {
            Log("Sin comprobar actualización: " + req.error);
            state = State.Idle;
            req.Dispose();
            yield break;
        }

        try
        {
            JObject rel = JObject.Parse(req.downloadHandler.text);
            string tag = (string)rel["tag_name"] ?? "";
            Version remote, local;
            if (!TryParseVersion(tag, out remote) || !TryParseVersion(ModInfo.MOD_VERSION, out local) || remote <= local)
            {
                state = State.Idle;
                req.Dispose();
                yield break;
            }

            foreach (JToken a in (JArray)rel["assets"])
            {
                string name = (string)a["name"] ?? "";
                if (name.StartsWith("BlasphemousCoopLocal-", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    assetUrl = (string)a["browser_download_url"] ?? "";
                    break;
                }
            }
            newVersion = remote.ToString();
            state = (assetUrl.Length > 0 && ReadSkipped() != newVersion) ? State.Available : State.Idle;
            Log("Versión instalada " + ModInfo.MOD_VERSION + ", última " + newVersion + " -> " + state);
        }
        catch (Exception e)
        {
            Log("Respuesta de actualización no válida: " + e.Message);
            state = State.Idle;
        }
        req.Dispose();
    }

    private static bool TryParseVersion(string text, out Version v)
    {
        v = null;
        if (string.IsNullOrEmpty(text)) return false;
        text = text.Trim();
        if (text.StartsWith("v") || text.StartsWith("V")) text = text.Substring(1);
        int dash = text.IndexOfAny(new[] { '-', '+' });
        if (dash >= 0) text = text.Substring(0, dash);
        try { v = new Version(text); return true; }
        catch (Exception) { return false; }
    }

    // ------------------------------------------------------------------ descarga e instalación diferida
    private IEnumerator DownloadRoutine()
    {
        state = State.Downloading;
        string dir = Path.Combine(GameDir(), Path.Combine("Modding", "CoopLocal_update"));
        string zip = Path.Combine(dir, "update.zip");
        UnityWebRequest req = UnityWebRequest.Get(assetUrl);
        req.SetRequestHeader("User-Agent", "CoopLocal-Updater");
        req.timeout = 120;
        yield return req.SendWebRequest();

        if (req.isNetworkError || req.isHttpError || req.downloadHandler.data == null || req.downloadHandler.data.Length < 1024)
        {
            Fail("No se pudo descargar la actualización: " + req.error);
            req.Dispose();
            yield break;
        }

        try
        {
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(zip, req.downloadHandler.data);
            LaunchApplyScript(dir, zip);
            state = State.Ready;
            Say("Actualización " + newVersion + " descargada. Se instalará al cerrar el juego.", 10f);
        }
        catch (Exception e)
        {
            Fail("No se pudo preparar la actualización: " + e.Message);
        }
        req.Dispose();
    }

    private static string GameDir()
    {
        string b = AppDomain.CurrentDomain.BaseDirectory;
        return string.IsNullOrEmpty(b) ? Directory.GetCurrentDirectory() : b;
    }

    /// <summary>Script que espera a que el juego termine y entonces copia el contenido de "payload" sobre Modding/.</summary>
    private static void LaunchApplyScript(string dir, string zip)
    {
        int pid = Process.GetCurrentProcess().Id;
        string modding = Path.Combine(GameDir(), "Modding");
        bool windows = Application.platform == RuntimePlatform.WindowsPlayer || Application.platform == RuntimePlatform.WindowsEditor;
        ProcessStartInfo psi;
        if (windows)
        {
            string ps1 = Path.Combine(dir, "apply.ps1");
            var sb = new StringBuilder();
            sb.AppendLine("$ErrorActionPreference = 'Stop'");
            sb.AppendLine("try { Wait-Process -Id " + pid + " -ErrorAction SilentlyContinue } catch {}");
            sb.AppendLine("Start-Sleep -Seconds 2");
            sb.AppendLine("$tmp = Join-Path '" + Esc(dir) + "' 'extract'");
            sb.AppendLine("if (Test-Path $tmp) { Remove-Item $tmp -Recurse -Force }");
            sb.AppendLine("Expand-Archive -LiteralPath '" + Esc(zip) + "' -DestinationPath $tmp -Force");
            sb.AppendLine("Copy-Item (Join-Path $tmp 'payload\\*') '" + Esc(modding) + "' -Recurse -Force");
            sb.AppendLine("Remove-Item $tmp -Recurse -Force; Remove-Item '" + Esc(zip) + "' -Force");
            File.WriteAllText(ps1, sb.ToString(), new UTF8Encoding(true));
            psi = new ProcessStartInfo("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"" + ps1 + "\"");
        }
        else
        {
            string sh = Path.Combine(dir, "apply.sh");
            var sb = new StringBuilder();
            sb.Append("#!/bin/sh\n");
            sb.Append("while kill -0 " + pid + " 2>/dev/null; do sleep 1; done\nsleep 2\n");
            sb.Append("rm -rf '" + dir + "/extract' && mkdir -p '" + dir + "/extract' && unzip -oq '" + zip + "' -d '" + dir + "/extract' && ");
            sb.Append("cp -rf '" + dir + "/extract/payload/.' '" + modding + "/' && rm -rf '" + dir + "/extract' '" + zip + "'\n");
            File.WriteAllText(sh, sb.ToString().Replace("\r\n", "\n"), new UTF8Encoding(false));
            psi = new ProcessStartInfo("/bin/sh", "\"" + sh + "\"");
        }
        psi.UseShellExecute = false;
        psi.CreateNoWindow = true;
        Process.Start(psi);
    }

    private static string Esc(string s) { return s.Replace("'", "''"); }

    // ------------------------------------------------------------------ interfaz
    private bool InGameplay()
    {
        try { return Framework.Managers.Core.Logic != null && Framework.Managers.Core.Logic.Penitent != null; }
        catch (Exception) { return false; }
    }

    private void Update()
    {
        if (state != State.Available || InGameplay()) return;
        if (Input.GetKeyDown(KeyCode.Y)) StartCoroutine(DownloadRoutine());
        else if (Input.GetKeyDown(KeyCode.N)) state = State.Idle;
        else if (Input.GetKeyDown(KeyCode.S)) { WriteSkipped(newVersion); state = State.Idle; }
    }

    private void OnGUI()
    {
        string text = null;
        if (state == State.Available && !InGameplay())
            text = "CoopLocal: hay una versión nueva (" + newVersion + ", tienes la " + ModInfo.MOD_VERSION + ").\n[Y] Actualizar     [N] Más tarde     [S] No avisar de esta versión";
        else if (state == State.Downloading)
            text = "CoopLocal: descargando la versión " + newVersion + "...";
        else if (message.Length > 0 && Time.unscaledTime < messageUntil)
            text = message;
        if (text == null) return;

        if (boxStyle == null)
        {
            boxStyle = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.MiddleCenter, fontSize = 18, wordWrap = true };
            textStyle = boxStyle;
        }
        float w = Mathf.Min(760f, Screen.width - 40f);
        GUI.Box(new Rect((Screen.width - w) / 2f, 24f, w, 76f), text, textStyle);
    }

    private void Say(string msg, float seconds) { message = msg; messageUntil = Time.unscaledTime + seconds; }

    private void Fail(string msg)
    {
        Log(msg);
        state = State.Failed;
        Say(msg, 8f);
    }

    // ------------------------------------------------------------------ versión omitida
    private static string SkipFile()
    {
        return Path.Combine(Path.Combine(Application.persistentDataPath, "CoopLocalMod"), "update_skip.txt");
    }

    private static string ReadSkipped()
    {
        try { string f = SkipFile(); return File.Exists(f) ? File.ReadAllText(f).Trim() : ""; }
        catch (Exception) { return ""; }
    }

    private static void WriteSkipped(string version)
    {
        try
        {
            string f = SkipFile();
            Directory.CreateDirectory(Path.GetDirectoryName(f));
            File.WriteAllText(f, version);
        }
        catch (Exception __swallowed) { SwallowedExceptions.Report(__swallowed, "CoopUpdater.cs:WriteSkipped"); }
    }

    private static void Log(string msg)
    {
        try { if (Main.CoopLocal != null) Blasphemous.ModdingAPI.ModLog.Info("[CoopUpdater] " + msg, Main.CoopLocal); }
        catch (Exception __swallowed) { SwallowedExceptions.Report(__swallowed, "CoopUpdater.cs:Log"); }
    }
}
