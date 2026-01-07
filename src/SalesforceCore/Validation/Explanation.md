# Validation Directory Explanation

## 1. Overview
The `src/SalesforceCore/Validation` directory contains a robust, rule-based validation engine. While Salesforce has its own server-side validation rules, this module allows the client application to perform **client-side (pre-flight) validation** to provide faster feedback and save API calls.

## 2. Key Components

### `ValidationRuleEngine`
**Purpose**: The central orchestrator.
**Features**:
- **Recursion Detection**: Prevents infinite loops if rules trigger other validations.
- **Circular Dependency Check**: Ensures rules don't depend on each other in a cycle.
- **Priority Execution**: Runs rules in a defined order.

### `ValidationRuleBuilder`
**Purpose**: A fluent API for defining rules in code.
**Example**:
```csharp
ValidationRuleBuilder.ForObject("Account")
    .RequireWhen("Phone", "Type", "Customer")
    .EmailDomainMustBe("Email", "company.com")
    .Build();
```

### `IValidationRule`
**Purpose**: Interface for defining a single rule.
**Implementations**:
- **`LambdaValidationRule`**: Allows defining ad-hoc rules using C# lambdas (`Func<Context, Result>`).
- **`CommonValidationRules`**: A factory for standard rules like `Required`, `Pattern`, `Range`, `FutureDate`.

### `ValidationContext`
**Purpose**: Provides context to a rule, including the Record being validated, the Original Record (for updates), and the Schema Metadata (Field Map).

## 3. Design Decisions
- **Pre-Flight Check**: This engine is designed to run *before* `DataService` sends data to Salesforce.
- **Fluent Interface**: The Builder pattern makes it easy for developers to define complex validation logic in a readable way during application startup.
