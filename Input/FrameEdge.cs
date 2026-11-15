namespace Blasphemous.CoopLocal;

// Flanco de subida de un boton evaluado como mucho una vez por fotograma. Logica pura (sin Unity) para poder probarla.
// Varios consumidores en el mismo fotograma ven el mismo valor; la primera evaluacion de cada fotograma fija el resultado.
internal sealed class FrameEdge
{
    private int lastFrame = -1;
    private bool previousHeld;
    private bool down;

    internal bool IsStale(int frame) => lastFrame != frame;

    internal bool Update(int frame, bool held)
    {
        lastFrame = frame;
        down = held && !previousHeld;
        previousHeld = held;
        return down;
    }

    internal bool Down => down;

    internal bool Evaluate(int frame, System.Func<bool> readHeld)
    {
        return IsStale(frame) ? Update(frame, readHeld()) : down;
    }
}
