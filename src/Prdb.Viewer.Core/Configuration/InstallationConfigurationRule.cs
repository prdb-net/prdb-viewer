namespace Prdb.Viewer.Core.Configuration;

public static class InstallationConfigurationRule
{
    public static InstallationConfigurationStatus Determine(
        bool claimed,
        PrdbConnectionStatus connection,
        bool hasActiveLibraryDirectory,
        bool hasPendingLibraryDirectory,
        bool initialProcessingStarted,
        bool attentionRequired = false)
    {
        if (!claimed)
        {
            return InstallationConfigurationStatus.Unclaimed;
        }

        if (connection is PrdbConnectionStatus.Missing or PrdbConnectionStatus.Rejected ||
            !hasActiveLibraryDirectory && !hasPendingLibraryDirectory)
        {
            return InstallationConfigurationStatus.ConfigurationRequired;
        }

        if (connection == PrdbConnectionStatus.VerificationPending ||
            hasPendingLibraryDirectory ||
            !hasActiveLibraryDirectory ||
            !initialProcessingStarted)
        {
            return InstallationConfigurationStatus.ConfigurationPending;
        }

        return attentionRequired
            ? InstallationConfigurationStatus.AttentionRequired
            : InstallationConfigurationStatus.Configured;
    }
}
