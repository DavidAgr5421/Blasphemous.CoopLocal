using Gameplay.GameControllers.Entities.Animations;
using Gameplay.GameControllers.Penitent;
using Gameplay.GameControllers.Penitent.Abilities;
using Gameplay.GameControllers.Penitent.Animator;
using HarmonyLib;

namespace Blasphemous.CoopLocal;

// Ronda 80: PenitentAttackAnimations.CanLungeAttack gatea LungeAttack.CanHit pero usa
// Core.Logic.Penitent en vez de la instancia correcta. Vanilla:
//   public void CanLungeAttack(Activation activation) {
//     LungeAttack l = Core.Logic.Penitent.GetComponentInChildren<LungeAttack>();
//     l.CanHit = activation == Activation.True;
//   }
// Activation es enum anidado en AttackAnimationsEvents (base de PenitentAttackAnimations),
// no en PenitentAttackAnimations. _penitent ya existe per-instance y se resuelve en
// Awake() via GetComponentInParent<Penitent>() correctamente — todos los demas metodos
// de esta misma clase lo usan. El evento de animacion se invoca por SendMessage sobre
// el Animator de P2, la instancia es la de P2, pero el cuerpo hardcodea P1.
// LungeAttack.AttackAreaOnStay solo aplica daño si CanHit && Casting && _newEnemyHit,
// y OnCastEnd resetea CanHit=false, asi que P2 nunca llega a CanHit=true y nunca hace daño.
//
// Este Prefix reemplaza el metodo ENTERO (return false) para P1 y P2 por igual — es seguro
// porque para P1, ____penitent (de esta misma instancia, GetComponentInParent en Awake())
// es exactamente el mismo objeto que Core.Logic.Penitent (P1 es el unico global), mismo
// resultado para P1 y fix correcto para P2, sin branch condicional.
[HarmonyPatch(typeof(PenitentAttackAnimations), nameof(PenitentAttackAnimations.CanLungeAttack))]
internal static class PenitentAttackAnimations_CanLungeAttack_Patch
{
    private static bool Prefix(AttackAnimationsEvents.Activation activation, Penitent ____penitent)
    {
        if (____penitent == null)
        {
            return true;
        }
        LungeAttack lungeAttack = ____penitent.GetComponentInChildren<LungeAttack>();
        if (lungeAttack != null)
        {
            lungeAttack.CanHit = activation == AttackAnimationsEvents.Activation.True;
        }
        return false;
    }
}
