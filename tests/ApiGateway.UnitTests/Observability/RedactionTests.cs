using System.Net;
using System.Text;
using ApiGateway.Observability.Redaction;
using Microsoft.AspNetCore.Http;

namespace ApiGateway.UnitTests.Observability;

public sealed class RedactionTests
{
    private readonly SensitiveDataClassifier _classifier = new(new RedactionOptions { AdditionalSensitiveNames = ["x-partner-secret-id"] });

    [Theory]
    [InlineData("Authorization", true)]
    [InlineData("cookie", true)]
    [InlineData("Set-Cookie", true)]
    [InlineData("X-Api-Key", true)]
    [InlineData("x_api_key", true)]
    [InlineData("Idempotency-Key", true)]
    [InlineData("refresh_token", true)]
    [InlineData("X-Partner-Token", true)]
    [InlineData("userPassword", true)]
    [InlineData("Content-Type", false)]
    [InlineData("orderId", false)]
    public void Sensitive_names(string name, bool sensitive) => _classifier.IsSensitiveName(name).ShouldBe(sensitive);

    [Fact]
    public void Scrubs_bearer_tokens_jwts_and_key_value_secrets()
    {
        const string jwt = "eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxMjM0NTY3ODkwIn0.dozjgNryP4J3jVmNHl0w5N_XgL0n3I9PlFUP0THsR8U";
        var text = $"call failed: Authorization=Bearer abcdefghijklmnop; token {jwt}; url ?api_key=s3cr3t&page=2 password: hunter2";

        var scrubbed = _classifier.Scrub(text);

        scrubbed.ShouldNotContain("abcdefghijklmnop");
        scrubbed.ShouldNotContain(jwt);
        scrubbed.ShouldNotContain("s3cr3t");
        scrubbed.ShouldNotContain("hunter2");
        scrubbed.ShouldContain("page=2");
    }

    [Fact]
    public void Plain_text_is_returned_unchanged()
    {
        const string text = "GET /api/orders responded 200";
        _classifier.Scrub(text).ShouldBeSameAs(text);
    }

    [Fact]
    public void Query_string_values_of_sensitive_parameters_are_masked()
    {
        var redactor = new QueryStringRedactor(_classifier);

        var result = redactor.Redact(new QueryString("?page=2&access_token=abc123&q=shoes"));

        result.ShouldBe("?page=2&access_token=***&q=shoes");
    }

    [Fact]
    public void Only_allow_listed_headers_are_selected_and_secrets_masked()
    {
        var redactor = new HeaderRedactor(_classifier);
        var headers = new HeaderDictionary
        {
            ["User-Agent"] = "curl/8",
            ["Authorization"] = "Bearer abcdefghijkl",
            ["X-Internal"] = "x",
        };

        var selected = redactor.Select(headers, ["User-Agent", "Authorization"]);

        selected["User-Agent"].ShouldBe("curl/8");
        selected["Authorization"].ShouldBe("***");
        selected.ContainsKey("X-Internal").ShouldBeFalse();
    }

    [Fact]
    public void Json_bodies_mask_sensitive_properties_including_nested_objects()
    {
        var redactor = new JsonBodyRedactor(_classifier);
        var body = """{"user":"ann","password":"hunter2","credentials":{"pin":"1234"},"items":[{"token":"t"}],"n":5}"""u8;

        var result = redactor.Redact(body, isJson: true, truncated: false);

        result.ShouldBe("""{"user":"ann","password":"***","credentials":"***","items":[{"token":"***"}],"n":5}""");
    }

    [Fact]
    public void Truncated_json_is_redacted_up_to_the_cut()
    {
        var redactor = new JsonBodyRedactor(_classifier);
        var body = Encoding.UTF8.GetBytes("""{"user":"ann","password":"hunter2","rest":"aaaaaaaaaa""");

        var result = redactor.Redact(body, isJson: true, truncated: true);

        result.ShouldNotContain("hunter2");
        result.ShouldEndWith("...(truncated)");
    }

    [Theory]
    [InlineData("192.0.2.17", "192.0.2.0")]
    [InlineData("::ffff:192.0.2.17", "192.0.2.0")]
    [InlineData("2001:db8:1:2::7", "2001:db8:1::")]
    public void Truncation_keeps_the_network_prefix(string address, string expected)
    {
        new TruncatingClientIpAnonymizer(24, 48).Anonymize(IPAddress.Parse(address)).ShouldBe(expected);
    }

    [Fact]
    public void Hashing_is_stable_per_key_and_not_reversible()
    {
        var address = IPAddress.Parse("198.51.100.4");
        var first = new HashingClientIpAnonymizer(new string('k', 32));
        var second = new HashingClientIpAnonymizer(new string('z', 32));

        first.Anonymize(address).ShouldBe(first.Anonymize(address));
        first.Anonymize(address).ShouldNotBe(second.Anonymize(address));
        first.Anonymize(address)!.ShouldNotContain("198.51");
    }

    [Fact]
    public void Erase_logs_nothing()
    {
        ClientIpAnonymizerFactory.Create(new ClientIpOptions { Mode = ClientIpMode.Erase }).Anonymize(IPAddress.Loopback).ShouldBeNull();
    }
}
