using Burndown.Common;
using Burndown.Firefly;
using Shouldly;

namespace Burndown.UnitTests.Firefly;

public class FireflyErrorCodesTests
{
    [Theory]
    [InlineData(FireflyErrorCodes.Unauthorized, 502)]
    [InlineData(FireflyErrorCodes.Forbidden, 502)]
    [InlineData(FireflyErrorCodes.Unexpected, 502)]
    [InlineData(FireflyErrorCodes.InvalidResponse, 502)]
    [InlineData(FireflyErrorCodes.Unavailable, 503)]
    [InlineData(FireflyErrorCodes.Timeout, 504)]
    [InlineData(FireflyErrorCodes.Validation, 422)]
    public void Maps_a_Firefly_code_to_an_HTTP_status(string code, int expected)
    {
        FireflyErrorCodes.ToHttpStatusCode(code).ShouldBe(expected);
    }

    [Theory]
    [InlineData(ErrorCodes.Validation)]
    [InlineData("something.else")]
    [InlineData("")]
    public void Leaves_other_codes_unmapped(string code)
    {
        FireflyErrorCodes.ToHttpStatusCode(code).ShouldBeNull();
    }
}
