using Burndown.Common;
using Shouldly;

namespace Burndown.UnitTests.Common;

public class ResultTests
{
    private static readonly Error SomeError = new("validation", "Nope.");

    [Fact]
    public void Success_has_no_error()
    {
        var result = Result.Success();

        result.IsSuccess.ShouldBeTrue();
        Should.Throw<InvalidOperationException>(() => result.Error);
    }

    [Fact]
    public void Failure_carries_the_error()
    {
        var result = Result.Failure(SomeError);

        result.IsSuccess.ShouldBeFalse();
        result.Error.ShouldBe(SomeError);
    }

    [Fact]
    public void Typed_success_exposes_the_value()
    {
        var result = Result<int>.Success(42);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(42);
        Should.Throw<InvalidOperationException>(() => result.Error);
    }

    [Fact]
    public void Typed_failure_hides_the_value()
    {
        var result = Result<int>.Failure(SomeError);

        result.IsSuccess.ShouldBeFalse();
        result.Error.ShouldBe(SomeError);
        Should.Throw<InvalidOperationException>(() => result.Value);
    }
}
