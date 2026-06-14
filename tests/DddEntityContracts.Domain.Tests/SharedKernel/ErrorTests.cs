using Domain.SharedKernel;
using FluentAssertions;

namespace DddEntityContracts.Domain.Tests.SharedKernel;

public class ErrorTests
{
    [Fact]
    public void Error_ValueEquality_BySameCodeAndMessage()
    {
        var error1 = new Error("ERR001", "Something went wrong");
        var error2 = new Error("ERR001", "Something went wrong");

        error1.Should().Be(error2);
    }
}
