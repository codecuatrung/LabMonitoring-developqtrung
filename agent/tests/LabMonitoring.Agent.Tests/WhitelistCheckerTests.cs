using FluentAssertions;
using LabMonitoring.Agent.Commands;
using LabMonitoring.Agent.Protocol;

namespace LabMonitoring.Agent.Tests;

public sealed class WhitelistCheckerTests
{
    [Fact]
    public void App_is_rejected_when_no_config_has_synced_yet()
    {
        WhitelistChecker.IsAppAllowed(@"C:\Windows\notepad.exe", null).Should().BeFalse();
    }

    [Fact]
    public void App_is_allowed_when_present_in_the_allow_list()
    {
        var config = new AgentConfigResponse { AllowedApplications = [@"C:\Windows\notepad.exe"] };
        WhitelistChecker.IsAppAllowed(@"C:\Windows\notepad.exe", config).Should().BeTrue();
    }

    [Fact]
    public void App_matching_is_case_insensitive()
    {
        var config = new AgentConfigResponse { AllowedApplications = [@"C:\Windows\notepad.exe"] };
        WhitelistChecker.IsAppAllowed(@"C:\WINDOWS\NOTEPAD.EXE", config).Should().BeTrue();
    }

    [Fact]
    public void App_not_in_either_list_is_rejected()
    {
        var config = new AgentConfigResponse { AllowedApplications = [@"C:\Windows\notepad.exe"] };
        WhitelistChecker.IsAppAllowed(@"C:\Windows\System32\cmd.exe", config).Should().BeFalse();
    }

    [Fact]
    public void Blocked_app_wins_even_if_also_present_in_the_allow_list()
    {
        var config = new AgentConfigResponse
        {
            AllowedApplications = [@"C:\Windows\notepad.exe"],
            BlockedApplications = [@"C:\Windows\notepad.exe"],
        };
        WhitelistChecker.IsAppAllowed(@"C:\Windows\notepad.exe", config).Should().BeFalse();
    }

    [Fact]
    public void Url_is_rejected_when_no_config_has_synced_yet()
    {
        WhitelistChecker.IsUrlAllowed("https://example.com", null).Should().BeFalse();
    }

    [Fact]
    public void Url_is_rejected_when_not_a_valid_absolute_uri()
    {
        var config = new AgentConfigResponse { AllowedUrls = ["example.com"] };
        WhitelistChecker.IsUrlAllowed("not a url", config).Should().BeFalse();
    }

    [Fact]
    public void Url_matching_exact_allowed_domain_succeeds()
    {
        var config = new AgentConfigResponse { AllowedUrls = ["example.com"] };
        WhitelistChecker.IsUrlAllowed("https://example.com/path", config).Should().BeTrue();
    }

    [Fact]
    public void Url_matching_a_subdomain_of_an_allowed_domain_succeeds()
    {
        var config = new AgentConfigResponse { AllowedUrls = ["example.com"] };
        WhitelistChecker.IsUrlAllowed("https://docs.example.com", config).Should().BeTrue();
    }

    [Fact]
    public void Url_matching_is_case_insensitive_on_the_host()
    {
        var config = new AgentConfigResponse { AllowedUrls = ["example.com"] };
        WhitelistChecker.IsUrlAllowed("https://EXAMPLE.com", config).Should().BeTrue();
    }

    [Fact]
    public void Url_for_an_unrelated_domain_is_rejected()
    {
        var config = new AgentConfigResponse { AllowedUrls = ["example.com"] };
        WhitelistChecker.IsUrlAllowed("https://notexample.com", config).Should().BeFalse();
    }

    [Fact]
    public void Blocked_url_wins_even_if_also_present_in_the_allow_list()
    {
        var config = new AgentConfigResponse
        {
            AllowedUrls = ["example.com"],
            BlockedUrls = ["example.com"],
        };
        WhitelistChecker.IsUrlAllowed("https://example.com", config).Should().BeFalse();
    }

    [Fact]
    public void Blocked_parent_domain_also_blocks_its_subdomains()
    {
        var config = new AgentConfigResponse
        {
            AllowedUrls = ["example.com"],
            BlockedUrls = ["ads.example.com"],
        };
        WhitelistChecker.IsUrlAllowed("https://ads.example.com", config).Should().BeFalse();
        WhitelistChecker.IsUrlAllowed("https://example.com", config).Should().BeTrue();
    }
}
