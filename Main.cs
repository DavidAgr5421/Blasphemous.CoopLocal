using BepInEx;

namespace Blasphemous.CoopLocal;

[BepInPlugin(ModInfo.MOD_ID, ModInfo.MOD_NAME, ModInfo.MOD_VERSION)]
[BepInDependency("com.damocles.blasphemous.modding-api", "1.5.0")]
internal class Main : BaseUnityPlugin
{
    public static CoopLocal CoopLocal { get; private set; }

    private void Start()
    {
        CoopLocal = new CoopLocal();
    }
}
