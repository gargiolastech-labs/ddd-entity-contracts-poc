using Domain.SharedKernel;
using FluentAssertions;

namespace DddEntityContracts.Domain.Tests.SharedKernel;

public class ResultTests
{
    [Fact]
    public void Result_Success_HasNoErrors()
    {
        var result = Result.Success();

        result.IsSuccess.Should().BeTrue();
        result.Errors.Should().BeEmpty();
        result.FirstError.Should().BeNull();
    }

    [Fact]
    public void Result_Failure_HasErrors()
    {
        var error = new Error("ERR001", "Something went wrong");
        var result = Result.Failure(error);

        result.IsFailure.Should().BeTrue();
        result.Errors.Should().ContainSingle().Which.Should().Be(error);
        result.FirstError.Should().Be(error);
    }

    [Fact]
    public void Result_Failure_RequiresAtLeastOneError()
    {
        var act = () => Result.Failure(Enumerable.Empty<Error>());

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Result_Generic_Success_ExposesValue()
    {
        var result = Result<int>.Success(42);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(42);
    }

    [Fact]
    public void Result_Generic_Failure_DoesNotExposeValue()
    {
        var result = Result<int>.Failure(new Error("ERR001", "Error"));

        result.IsFailure.Should().BeTrue();
        var act = () => result.Value;
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Result_Map_TransformsSuccessOnly()
    {
        var success = Result<int>.Success(5);
        var failure = Result<int>.Failure(new Error("ERR001", "Error"));

        var mappedSuccess = success.Map(x => x * 2);
        var mappedFailure = failure.Map(x => x * 2);

        mappedSuccess.IsSuccess.Should().BeTrue();
        mappedSuccess.Value.Should().Be(10);
        mappedFailure.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Result_Bind_ShortCircuitsOnFirstFailure()
    {
        var failure = Result<int>.Failure(new Error("ERR001", "First error"));

        var result = failure.Bind(x => Result<string>.Success(x.ToString()));

        result.IsFailure.Should().BeTrue();
        result.FirstError!.Code.Should().Be("ERR001");
    }
}
