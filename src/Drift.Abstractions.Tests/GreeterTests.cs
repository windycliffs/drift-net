namespace WindyCliffs.Drift.Tests;

using Xunit;

public class GreeterTests
{
    [Fact]
    public void GreetReturnsGreeting()
    {
        Assert.Equal("Hello, World!", Greeter.Greet("World"));
    }
}
