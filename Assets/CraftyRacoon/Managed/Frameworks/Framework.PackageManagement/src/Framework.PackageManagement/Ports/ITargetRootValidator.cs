namespace CraftyRacoon.PackageManagement.Framework.Ports
{
    public interface ITargetRootValidator
    {
        TargetRootValidation Validate(string targetRoot);
    }

    public sealed class TargetRootValidation
    {
        public TargetRootValidation(bool isValid, string detail)
        {
            IsValid = isValid;
            Detail = detail ?? string.Empty;
        }

        public bool IsValid { get; }
        public string Detail { get; }

        public static TargetRootValidation Valid()
        {
            return new TargetRootValidation(true, string.Empty);
        }
    }
}
