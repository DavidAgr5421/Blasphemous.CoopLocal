namespace Blasphemous.CoopLocal;

// Sustituto mínimo del logger del mod (el real depende de BepInEx/Unity) para poder compilar lógica pura en las pruebas.
internal static class DashParryDebugLog
{
    internal static readonly System.Collections.Generic.List<string> Lines = new System.Collections.Generic.List<string>();
    internal static void Log(string message) { lock (Lines) Lines.Add(message); }
}
