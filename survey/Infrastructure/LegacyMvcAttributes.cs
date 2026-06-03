namespace Microsoft.AspNetCore.Mvc;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class ValidateInputAttribute : Attribute
{
    public ValidateInputAttribute(bool enableValidation)
    {
        EnableValidation = enableValidation;
    }

    public bool EnableValidation { get; }
}