using Domain.SharedKernel;
using FluentAssertions;

namespace DddEntityContracts.Domain.Tests.SharedKernel;

public class ValidationTests
{
    [Fact]
    public void Validation_Success_ToResult_ReturnsSuccess()
    {
        var validation = Validation<int>.Success(42);

        var result = validation.ToResult();

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(42);
    }

    [Fact]
    public void Validation_Failure_ToResult_PreservesAllErrors()
    {
        var errors = new[] { new Error("ERR001", "First"), new Error("ERR002", "Second") };
        var validation = Validation<int>.Failure(errors);

        var result = validation.ToResult();

        result.IsFailure.Should().BeTrue();
        result.Errors.Should().HaveCount(2);
    }

    [Fact]
    public void Validation_Combine_AccumulatesAllErrors()
    {
        var v1 = Validation<string>.Failure(new Error("ERR001", "Error 1"));
        var v2 = Validation<int>.Failure(new Error("ERR002", "Error 2"));

        var combined = Validation<string>.Combine(v1, v2, (s, i) => s + i);

        combined.IsFailure.Should().BeTrue();
        combined.ToResult().Errors.Should().HaveCount(2);
    }

    [Fact]
    public void Validation_Combine_WithAllSuccess_ReturnsProjectedValue()
    {
        var v1 = Validation<int>.Success(10);
        var v2 = Validation<int>.Success(20);

        var combined = Validation<int>.Combine(v1, v2, (a, b) => a + b);

        combined.IsSuccess.Should().BeTrue();
        combined.ToResult().Value.Should().Be(30);
    }

    [Fact]
    public void Validation_Failure_WithEmptyErrors_ThrowsInvalidOperation()
    {
        var act = () => Validation<int>.Failure(Enumerable.Empty<Error>());

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*at least one error*");
    }

    [Fact]
    public void Validation_Combine_WithMixedFailures_DoesNotCallProjector()
    {
        var v1 = Validation<int>.Success(10);
        var v2 = Validation<int>.Failure(new Error("ERR001", "Error"));

        var projectorCalled = false;
        var combined = Validation<int>.Combine(v1, v2, (a, b) =>
        {
            projectorCalled = true;
            return a + b;
        });

        combined.IsFailure.Should().BeTrue();
        projectorCalled.Should().BeFalse();
    }
}
