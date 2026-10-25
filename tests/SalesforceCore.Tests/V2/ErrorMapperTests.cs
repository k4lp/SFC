using FluentAssertions;
using SalesforceCore.Models.Data;
using SalesforceCore.Models.Errors;
using SalesforceCore.Tests.TestKit;
using Xunit;

namespace SalesforceCore.Tests.V2;

public class ErrorMapperTests
{
    private static string Body(string code, string message = "m", params string[] fields)
        => SalesforceJson.Errors((code, message, fields)).ToJsonString();

    [Theory]
    [InlineData(404, "NOT_FOUND", typeof(SalesforceNotFoundException))]
    [InlineData(404, "ENTITY_IS_DELETED", typeof(SalesforceNotFoundException))]
    [InlineData(403, "INSUFFICIENT_ACCESS", typeof(SalesforcePermissionException))]
    [InlineData(400, "INSUFFICIENT_ACCESS_OR_READONLY", typeof(SalesforcePermissionException))]
    [InlineData(403, "API_DISABLED_FOR_ORG", typeof(SalesforcePermissionException))]
    [InlineData(400, "INVALID_FIELD_FOR_INSERT_UPDATE", typeof(SalesforceFieldSecurityException))]
    [InlineData(400, "MALFORMED_QUERY", typeof(SalesforceQueryException))]
    [InlineData(400, "INVALID_FIELD", typeof(SalesforceQueryException))]
    [InlineData(400, "INVALID_TYPE", typeof(SalesforceQueryException))]
    [InlineData(400, "REQUIRED_FIELD_MISSING", typeof(SalesforceValidationException))]
    [InlineData(400, "FIELD_CUSTOM_VALIDATION_EXCEPTION", typeof(SalesforceValidationException))]
    [InlineData(400, "STRING_TOO_LONG", typeof(SalesforceValidationException))]
    [InlineData(400, "DUPLICATES_DETECTED", typeof(SalesforceDuplicateRuleException))]
    [InlineData(400, "UNABLE_TO_LOCK_ROW", typeof(SalesforceRecordLockedException))]
    [InlineData(403, "REQUEST_LIMIT_EXCEEDED", typeof(SalesforceRateLimitException))]
    [InlineData(429, "SOMETHING", typeof(SalesforceRateLimitException))]
    [InlineData(401, "INVALID_SESSION_ID", typeof(SalesforceAuthException))]
    [InlineData(500, "UNKNOWN_EXCEPTION", typeof(SalesforceServerException))]
    [InlineData(503, "SERVER_UNAVAILABLE", typeof(SalesforceServerException))]
    [InlineData(400, "SOME_NEW_CODE", typeof(SalesforceException))]
    public void FromResponse_MapsCodeAndStatus_ToTypedException(int status, string code, Type expected)
    {
        var ex = SalesforceErrorMapper.FromResponse(status, Body(code, "boom"), requestId: "req-1");

        ex.Should().BeOfType(expected);
        ex.ErrorCode.Should().Be(code, "the real Salesforce code always wins over a subtype default");
        ex.HttpStatusCode.Should().Be(status);
        ex.RequestId.Should().Be("req-1");
        ex.Errors.Should().ContainSingle().Which.ErrorCode.Should().Be(code);
        ex.Message.Should().Be("boom");
    }

    [Theory]
    [InlineData(403, typeof(SalesforcePermissionException), "Access denied")]
    [InlineData(404, typeof(SalesforceNotFoundException), "Resource not found")]
    [InlineData(401, typeof(SalesforceAuthException), "Session has expired or is invalid")]
    [InlineData(502, typeof(SalesforceServerException), "Salesforce server error")]
    [InlineData(400, typeof(SalesforceException), "Invalid request")]
    public void FromResponse_EmptyBody_UsesStatusDefaults(int status, Type expected, string message)
    {
        var ex = SalesforceErrorMapper.FromResponse(status, "");
        ex.Should().BeOfType(expected);
        ex.Message.Should().Be(message);
        ex.Errors.Should().BeEmpty();
    }

    [Fact]
    public void FromResponse_NonJsonBody_IsTruncatedAndKeptRaw()
    {
        var html = "<html>" + new string('x', 5000) + "</html>";
        var ex = SalesforceErrorMapper.FromResponse(502, html, maxBodyLength: 100);
        ex.Should().BeOfType<SalesforceServerException>();
        ex.RawResponse!.Length.Should().Be(101);
        ex.Message.Length.Should().Be(101);
    }

    [Fact]
    public void FromResponse_OAuthError_UsesDescriptionAndCode()
    {
        var ex = SalesforceErrorMapper.FromResponse(400, """{"error":"invalid_grant","error_description":"authentication failure"}""");
        ex.Should().BeOfType<SalesforceAuthException>();
        ex.Message.Should().Be("authentication failure");
        ex.ErrorCode.Should().Be("invalid_grant");
    }

    [Fact]
    public void FromResponse_MultipleErrors_JoinsMessages_AndCollectsFields()
    {
        var body = SalesforceJson.Errors(("REQUIRED_FIELD_MISSING", "Name missing", new[] { "Name" }), ("STRING_TOO_LONG", "Too long", new[] { "Phone" })).ToJsonString();
        var ex = SalesforceErrorMapper.FromResponse(400, body);
        var v = ex.Should().BeOfType<SalesforceValidationException>().Subject;
        v.Message.Should().Contain("[REQUIRED_FIELD_MISSING] Name missing").And.Contain("[STRING_TOO_LONG] Too long");
        v.Fields.Should().BeEquivalentTo("Name", "Phone");
        v.ValidationErrors.Keys.Should().BeEquivalentTo("Name", "Phone");
        v.Errors.Should().HaveCount(2);
    }

    [Fact]
    public void FromResponse_RateLimit_CarriesRetryAfter_AndIsTransient()
    {
        var ex = SalesforceErrorMapper.FromResponse(429, Body("REQUEST_LIMIT_EXCEEDED"), retryAfter: TimeSpan.FromSeconds(7));
        var rl = ex.Should().BeOfType<SalesforceRateLimitException>().Subject;
        rl.RetryAfter.Should().Be(TimeSpan.FromSeconds(7));
        rl.IsTransient.Should().BeTrue();
    }

    [Theory]
    [InlineData(400, "MALFORMED_QUERY", false)]
    [InlineData(404, "NOT_FOUND", false)]
    [InlineData(503, "SERVER_UNAVAILABLE", true)]
    [InlineData(400, "UNABLE_TO_LOCK_ROW", true)]
    public void IsTransient_ReflectsRetryability(int status, string code, bool transient)
        => SalesforceErrorMapper.FromResponse(status, Body(code)).IsTransient.Should().Be(transient);

    [Fact]
    public void FromErrors_ForCollectionResults_ReturnsTypedException()
    {
        var ex = SalesforceException.FromErrors(new[] { new SalesforceError { ErrorCode = "DUPLICATES_DETECTED", Message = "dup" } });
        ex.Should().BeOfType<SalesforceDuplicateRuleException>();
        ex.ErrorCode.Should().Be("DUPLICATES_DETECTED");
        SalesforceException.FromErrors(Array.Empty<SalesforceError>()).Message.Should().Be("Unknown Salesforce error");
    }

    [Fact]
    public void ParseErrors_HandlesNestedErrorsObject()
    {
        var errors = SalesforceErrorMapper.ParseErrors("""{"errors":[{"statusCode":"INVALID_FIELD","message":"bad"}]}""");
        errors.Should().ContainSingle().Which.ErrorCode.Should().Be("INVALID_FIELD");
        SalesforceErrorMapper.ParseErrors("not json").Should().BeEmpty();
        SalesforceErrorMapper.ParseErrors(null).Should().BeEmpty();
    }

    [Fact]
    public void ConnectionException_IsTransient_AndKeepsInner()
    {
        var inner = new HttpRequestException("reset");
        var ex = new SalesforceConnectionException("failed", inner);
        ex.IsTransient.Should().BeTrue();
        ex.InnerException.Should().BeSameAs(inner);
        ex.ErrorCode.Should().Be("CONNECTION_FAILED");
    }
}
