using System;
using Xunit;

namespace Blasphemous.CoopLocal.Tests;

public class SwallowedExceptionsTests
{
    [Theory]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(9, false)]
    [InlineData(10, true)]
    [InlineData(11, false)]
    [InlineData(100, true)]
    [InlineData(1000, true)]
    [InlineData(1001, false)]
    [InlineData(20, false)]
    [InlineData(0, false)]
    [InlineData(-5, false)]
    public void ShouldLog_OnlyOnPowersOfTen(int n, bool expected)
    {
        Assert.Equal(expected, SwallowedExceptions.ShouldLog(n));
    }

    [Fact]
    public void Report_IsRateLimited_PerLocation()
    {
        DashParryDebugLog.Lines.Clear();
        for (int i = 0; i < 25; i++) SwallowedExceptions.Report(new InvalidOperationException("x"), "TestFile.cs:1");
        SwallowedExceptions.Report(new InvalidOperationException("y"), "Other.cs:2");

        int forFirst = 0, forOther = 0;
        foreach (string l in DashParryDebugLog.Lines)
        {
            if (l.Contains("TestFile.cs:1")) forFirst++;
            if (l.Contains("Other.cs:2")) forOther++;
        }
        Assert.Equal(2, forFirst); // 1ª y 10ª de 25
        Assert.Equal(1, forOther);
    }

    [Fact]
    public void Report_NeverThrows_EvenWithNullException()
    {
        SwallowedExceptions.Report(null, "Null.cs:1");
    }
}
