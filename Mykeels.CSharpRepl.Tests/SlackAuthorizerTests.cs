namespace Mykeels.CSharpRepl.Tests;

public class SlackAuthorizerTests
{
    [Test]
    public void Constructor_WithNoAllowlistConfigured_ThrowsToFailClosed()
    {
        var options = new SlackReplOptions { BotToken = "xoxb", AppToken = "xapp" };

        Assert.Throws<InvalidOperationException>(() => new SlackAuthorizer(options));
    }

    [Test]
    public void Constructor_WithEmptyAllowlists_ThrowsToFailClosed()
    {
        var options = new SlackReplOptions
        {
            BotToken = "xoxb",
            AppToken = "xapp",
            AllowedUserIds = [],
            AllowedChannelIds = [],
        };

        Assert.Throws<InvalidOperationException>(() => new SlackAuthorizer(options));
    }

    [Test]
    public void CanStartSession_WithUserOnAllowlist_ReturnsTrue()
    {
        var authorizer = new SlackAuthorizer(
            new SlackReplOptions
            {
                BotToken = "xoxb",
                AppToken = "xapp",
                AllowedUserIds = ["U1"],
            }
        );

        Assert.That(authorizer.CanStartSession("U1", "C1"), Is.True);
    }

    [Test]
    public void CanStartSession_WithUserNotOnAllowlist_ReturnsFalse()
    {
        var authorizer = new SlackAuthorizer(
            new SlackReplOptions
            {
                BotToken = "xoxb",
                AppToken = "xapp",
                AllowedUserIds = ["U1"],
            }
        );

        Assert.That(authorizer.CanStartSession("U2", "C1"), Is.False);
    }

    [Test]
    public void CanStartSession_WithOnlyChannelAllowlistConfigured_AllowsAnyUserInThatChannel()
    {
        var authorizer = new SlackAuthorizer(
            new SlackReplOptions
            {
                BotToken = "xoxb",
                AppToken = "xapp",
                AllowedChannelIds = ["C1"],
            }
        );

        Assert.That(authorizer.CanStartSession("U-anyone", "C1"), Is.True);
        Assert.That(authorizer.CanStartSession("U-anyone", "C2"), Is.False);
    }

    [Test]
    public void CanStartSession_WithFailingIsAuthorizedCallback_ReturnsFalseEvenIfOnAllowlists()
    {
        var authorizer = new SlackAuthorizer(
            new SlackReplOptions
            {
                BotToken = "xoxb",
                AppToken = "xapp",
                AllowedUserIds = ["U1"],
                IsAuthorized = _ => false,
            }
        );

        Assert.That(authorizer.CanStartSession("U1", "C1"), Is.False);
    }

    [Test]
    public void CanReply_FromSessionOwner_ReturnsTrue()
    {
        var authorizer = new SlackAuthorizer(
            new SlackReplOptions
            {
                BotToken = "xoxb",
                AppToken = "xapp",
                AllowedUserIds = ["U1"],
            }
        );

        Assert.That(authorizer.CanReply("U1", "C1", sessionOwnerUserId: "U1"), Is.True);
    }

    [Test]
    public void CanReply_FromNonOwner_WithDefaultRestriction_ReturnsFalse()
    {
        var authorizer = new SlackAuthorizer(
            new SlackReplOptions
            {
                BotToken = "xoxb",
                AppToken = "xapp",
                AllowedUserIds = ["U1", "U2"],
            }
        );

        Assert.That(authorizer.CanReply("U2", "C1", sessionOwnerUserId: "U1"), Is.False);
    }

    [Test]
    public void CanReply_FromNonOwner_WithRestrictionDisabled_ReturnsTrueIfAllowlisted()
    {
        var authorizer = new SlackAuthorizer(
            new SlackReplOptions
            {
                BotToken = "xoxb",
                AppToken = "xapp",
                AllowedUserIds = ["U1", "U2"],
                RestrictRepliesToSessionOwner = false,
            }
        );

        Assert.That(authorizer.CanReply("U2", "C1", sessionOwnerUserId: "U1"), Is.True);
    }
}
