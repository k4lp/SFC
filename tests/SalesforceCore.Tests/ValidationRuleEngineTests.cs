using FluentAssertions;
using Moq;
using SalesforceCore.Models.Metadata;
using SalesforceCore.Services.Metadata;
using SalesforceCore.Validation;
using Xunit;

namespace SalesforceCore.Tests;

public class ValidationRuleEngineTests
{
    private readonly Mock<ISchemaService> _mockSchemaService;
    private readonly Mock<IFieldValidator> _mockFieldValidator;
    private readonly ValidationRuleEngine _engine;

    public ValidationRuleEngineTests()
    {
        _mockSchemaService = new Mock<ISchemaService>();
        _mockFieldValidator = new Mock<IFieldValidator>();
        _engine = new ValidationRuleEngine(_mockSchemaService.Object, _mockFieldValidator.Object);
    }

    [Fact]
    public void RegisterRule_AddsRule()
    {
        var rule = new Mock<IValidationRule>();
        rule.Setup(r => r.RuleId).Returns("TestRule");

        _engine.RegisterRule(rule.Object);

        var rules = _engine.GetRules();
        rules.Should().Contain(rule.Object);
    }

    [Fact]
    public async Task ValidateAsync_ExecutesRules()
    {
        var objectName = "Account";
        var record = new Dictionary<string, object?> { { "Name", "Test" } };

        _mockFieldValidator.Setup(v => v.ValidateRecordAsync(It.IsAny<string>(), It.IsAny<IDictionary<string, object?>>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ValidationResult.Success());

        _mockSchemaService.Setup(s => s.GetFieldMapAsync(objectName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, SObjectField>());

        var rule = new Mock<IValidationRule>();
        rule.Setup(r => r.RuleId).Returns("Rule1");
        rule.Setup(r => r.ValidateAsync(It.IsAny<ValidationContext>()))
            .ReturnsAsync(ValidationResult.Success());

        _engine.RegisterRule(rule.Object);

        var result = await _engine.ValidateAsync(objectName, record);

        result.IsValid.Should().BeTrue();
        rule.Verify(r => r.ValidateAsync(It.IsAny<ValidationContext>()), Times.Once);
    }

    [Fact]
    public async Task ValidateAsync_StopsOnFailure_IfConfigured()
    {
        var objectName = "Account";
        var record = new Dictionary<string, object?> { { "Name", "Test" } };

        _mockFieldValidator.Setup(v => v.ValidateRecordAsync(It.IsAny<string>(), It.IsAny<IDictionary<string, object?>>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ValidationResult.Success());

        _mockSchemaService.Setup(s => s.GetFieldMapAsync(objectName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, SObjectField>());

        var rule1 = new Mock<IValidationRule>();
        rule1.Setup(r => r.RuleId).Returns("Rule1");
        rule1.Setup(r => r.Priority).Returns(1);
        rule1.Setup(r => r.StopOnFailure).Returns(true);
        rule1.Setup(r => r.ValidateAsync(It.IsAny<ValidationContext>()))
            .ReturnsAsync(ValidationResult.Failure("Field", "Error", "Msg"));

        var rule2 = new Mock<IValidationRule>();
        rule2.Setup(r => r.RuleId).Returns("Rule2");
        rule2.Setup(r => r.Priority).Returns(2);

        _engine.RegisterRule(rule1.Object);
        _engine.RegisterRule(rule2.Object);

        var result = await _engine.ValidateAsync(objectName, record);

        result.IsValid.Should().BeFalse();
        rule1.Verify(r => r.ValidateAsync(It.IsAny<ValidationContext>()), Times.Once);
        rule2.Verify(r => r.ValidateAsync(It.IsAny<ValidationContext>()), Times.Never);
    }
}
