using System;
using System.Reflection;
using Framework.Managers;
using Gameplay.UI;
using Gameplay.UI.Others.MenuLogic;
using UnityEngine;

namespace Blasphemous.CoopLocal.MultiPlayer;

/// <summary>
/// Un cliente online entra en la partida del anfitrión con ChangeLevel directo, sin pasar por el flujo vanilla de
/// "elegir ranura -> Jugar" (NewMainMenu.InternalPlay). Si estaba en el menú principal, ese menú (y su pantalla de ranuras)
/// quedaba visible por debajo de la partida y se podía incluso iniciar otra. Esto lo cierra igual que lo haría InternalPlay.
/// </summary>
internal static class CoopMainMenuCloser
{
    internal static void CloseIfOpen()
    {
        try
        {
            NewMainMenu menu = UnityEngine.Object.FindObjectOfType<NewMainMenu>();
            if (menu == null || !menu.currentlyActive) return;

            // La corrutina ShowSlotSaveWidget, al terminar, arrancaría OTRA partida con la ranura marcada: se detiene.
            menu.StopAllCoroutines();

            BindingFlags any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            try
            {
                FieldInfo widgetField = typeof(NewMainMenu).GetField("selectSaveSlotsWidget", any);
                SelectSaveSlots widget = widgetField != null ? widgetField.GetValue(menu) as SelectSaveSlots : null;
                if (widget != null)
                {
                    // IsShowing tiene el setter privado: por reflexión (el acceso directo falla al compilar el método con JIT).
                    PropertyInfo showing = typeof(SelectSaveSlots).GetProperty("IsShowing", any);
                    if (showing != null) showing.SetValue(widget, false, null);
                }
            }
            catch (Exception e) { SwallowedExceptions.Report(e, "CoopMainMenuCloser:widget"); }

            try
            {
                MethodInfo setState = typeof(NewMainMenu).GetMethod("SetState", any);
                if (setState != null) setState.Invoke(menu, new object[] { Enum.ToObject(setState.GetParameters()[0].ParameterType, 0) }); // MenuState.OFF
            }
            catch (Exception e) { SwallowedExceptions.Report(e, "CoopMainMenuCloser:state"); }

            if (Core.LevelManager != null) Core.LevelManager.ActivatePrecachedScene();
            if (UIController.instance != null) UIController.instance.HideMainMenu();
            if (UnityEngine.EventSystems.EventSystem.current != null) UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(null);
            if (Core.Audio != null && Core.Audio.Ambient != null) Core.Audio.Ambient.StopCurrent();

            if (Main.CoopLocal != null) Blasphemous.ModdingAPI.ModLog.Info("[CoopNetwork] Menú principal cerrado para entrar a la partida del anfitrión.", Main.CoopLocal);
        }
        catch (Exception e) { SwallowedExceptions.Report(e, "CoopMainMenuCloser"); }
    }
}
