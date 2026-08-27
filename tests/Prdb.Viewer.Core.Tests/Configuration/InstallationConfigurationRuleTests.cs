using Prdb.Viewer.Core.Configuration;

using Xunit;

namespace Prdb.Viewer.Core.Tests.Configuration;

public sealed class InstallationConfigurationRuleTests
{
    [Theory]
    [InlineData(false, PrdbConnectionStatus.Missing, false, false, false, InstallationConfigurationStatus.Unclaimed)]
    [InlineData(true, PrdbConnectionStatus.Missing, false, false, false, InstallationConfigurationStatus.ConfigurationRequired)]
    [InlineData(true, PrdbConnectionStatus.Verified, false, true, false, InstallationConfigurationStatus.ConfigurationPending)]
    [InlineData(true, PrdbConnectionStatus.Verified, true, false, false, InstallationConfigurationStatus.ConfigurationPending)]
    [InlineData(true, PrdbConnectionStatus.Verified, true, false, true, InstallationConfigurationStatus.Configured)]
    [InlineData(true, PrdbConnectionStatus.Degraded, true, false, true, InstallationConfigurationStatus.Configured)]
    public void Configuration_status_follows_domain_precedence(
        bool claimed,
        PrdbConnectionStatus connection,
        bool activeDirectory,
        bool pendingDirectory,
        bool processingStarted,
        InstallationConfigurationStatus expected)
    {
        Assert.Equal(
            expected,
            InstallationConfigurationRule.Determine(
                claimed,
                connection,
                activeDirectory,
                pendingDirectory,
                processingStarted));
    }

    [Fact]
    public void Operational_attention_takes_precedence_only_after_configuration_is_complete()
    {
        Assert.Equal(
            InstallationConfigurationStatus.AttentionRequired,
            InstallationConfigurationRule.Determine(
                claimed: true,
                PrdbConnectionStatus.Verified,
                hasActiveLibraryDirectory: true,
                hasPendingLibraryDirectory: false,
                initialProcessingStarted: true,
                attentionRequired: true));
    }
}
