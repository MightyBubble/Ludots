namespace Ludots.Core.Gameplay.Providers
{
    public static class ProviderFailureCodes
    {
        public const string UnknownProviderKey = "unknown_provider_key";
        public const string NeedsProviderRegistration = "needs_provider_registration";
        public const string DuplicateProviderKey = "duplicate_provider_key";
        public const string InvalidProviderKeyForm = "invalid_provider_key_form";
        public const string DomainNotAllowed = "provider_domain_not_allowed";
        public const string ParameterSchemaMismatch = "provider_parameter_schema_mismatch";
        public const string ConditionWriteDetected = "condition_write_detected";
        public const string GapEntryNotResolvable = "gap_entry_not_resolvable";
    }
}
