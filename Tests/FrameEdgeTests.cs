using Blasphemous.CoopLocal;
using Xunit;

namespace Blasphemous.CoopLocal.Tests;

public class FrameEdgeTests
{
    [Fact]
    public void FirstHeldFrameIsAPress_ThenNot()
    {
        var e = new FrameEdge();
        Assert.False(e.Update(1, false));
        Assert.True(e.Update(2, true));
        Assert.False(e.Update(3, true));
        Assert.False(e.Update(4, true));
    }

    [Fact]
    public void ReleaseThenPressFiresAgain()
    {
        var e = new FrameEdge();
        e.Update(1, true);
        e.Update(2, false);
        Assert.True(e.Update(3, true));
    }

    [Fact]
    public void SameFrameConsumersSeeSameValue_AndReadIsNotReEvaluated()
    {
        var e = new FrameEdge();
        int reads = 0;
        System.Func<bool> read = () => { reads++; return true; };
        Assert.True(e.Evaluate(5, read));
        Assert.True(e.Evaluate(5, read));   // misma respuesta, sin releer
        Assert.Equal(1, reads);
        Assert.False(e.Evaluate(6, read));  // sigue pulsado: ya no es flanco
        Assert.Equal(2, reads);
    }

    [Fact]
    public void PumpedEveryFrame_EnteringRangeWhileHoldingIsNotAPress()
    {
        var e = new FrameEdge();
        e.Update(1, true);   // el bombeo lo evalua cada fotograma aunque nadie pregunte
        e.Update(2, true);
        Assert.False(e.Evaluate(3, () => true));
    }
}
