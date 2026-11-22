using ClaimsModule.Application.Reserves;
using ClaimsModule.Domain.Exceptions;
using ClaimsModule.Domain.Reserves;
using Xunit;

namespace ClaimsModule.IntegrationTests.Claims;

public sealed class ReservePostingDomainTests
{
    [Fact]
    public void ApprovalIsRequiredAndPostingFinishesOnceForTheAssignedJobAndKey()
    {
        var pending = Submit(15_000m);
        Assert.False(pending.IsPendingPosting);
        Assert.Throws<InvalidOperationException>(() => pending.AssignPostingJob("1", Guid.NewGuid()));
        Assert.Throws<ClaimRuleViolation>(() => pending.RetryPosting(Guid.NewGuid()));

        var approved = Submit(5_000m);
        var correlation = Guid.NewGuid();
        approved.CapturePostingCorrelation(correlation);
        Assert.False(approved.TryFinishPosting("1", approved.PostingKey, succeeded: true));
        approved.AssignPostingJob("1", Guid.NewGuid());
        Assert.Equal(correlation, approved.PostingCorrelationId);
        Assert.False(approved.TryFinishPosting("2", approved.PostingKey, succeeded: true));
        Assert.False(approved.TryFinishPosting("1", "other-key", succeeded: true));
        Assert.True(approved.TryFinishPosting("1", approved.PostingKey, succeeded: true));
        Assert.False(approved.TryFinishPosting("1", approved.PostingKey, succeeded: true));
        Assert.False(approved.TryFinishPosting("1", approved.PostingKey, succeeded: false));
        Assert.Equal(ReservePostingStatus.Posted, approved.PostingStatus);
        Assert.Equal(ReserveApprovalStatus.AutoApproved, approved.ApprovalStatus);
        Assert.Throws<ClaimRuleViolation>(() => approved.RetryPosting(Guid.NewGuid()));
    }

    [Fact]
    public void RetryKeepsPostingKeyAndRejectsAnObsoleteJob()
    {
        var approved = Submit(5_000m);
        var key = approved.PostingKey;
        var fallback = Guid.NewGuid();
        approved.AssignPostingJob("first", fallback);
        Assert.Equal(fallback, approved.PostingCorrelationId);

        Assert.True(approved.TryFinishPosting("first", key, succeeded: false));
        Assert.Equal(ReservePostingStatus.Failed, approved.PostingStatus);

        var retryCorrelation = Guid.NewGuid();
        approved.RetryPosting(retryCorrelation);
        Assert.Equal(ReservePostingStatus.Pending, approved.PostingStatus);
        Assert.Null(approved.PostingJobId);
        Assert.Equal(key, approved.PostingKey);
        Assert.Equal(retryCorrelation, approved.PostingCorrelationId);
        approved.AssignPostingJob("next", Guid.NewGuid());
        Assert.False(approved.TryFinishPosting("first", key, succeeded: true));
        Assert.False(approved.TryFinishPosting("first", key, succeeded: false));
        Assert.True(approved.TryFinishPosting("next", key, succeeded: true));
    }

    [Theory]
    [InlineData(100, "Change in Outstanding Reserves", "Outstanding Loss Reserves")]
    [InlineData(-100, "Outstanding Loss Reserves", "Change in Outstanding Reserves")]
    public void JournalUsesTheSignedDirectionAndAbsoluteAmount(int amount, string debit, string credit)
    {
        var journal = SimulatedGlJournal.For(amount, "posting-key", "job-id");
        Assert.Equal(debit, journal.DebitAccount);
        Assert.Equal(credit, journal.CreditAccount);
        Assert.Equal(100m, journal.Amount);
        Assert.Equal("posting-key", journal.PostingKey);
        Assert.Equal("job-id", journal.PostingJobId);
    }

    private static ReserveHistory Submit(decimal value)
    {
        var actor = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var component = new ClaimReserveComponent(Guid.NewGuid(), Guid.NewGuid(),
            ReserveComponentType.Indemnity, actor, now);
        return component.Submit(new(ReserveTransactionType.Add, value, "Estimate"), 0m, false, actor, now);
    }
}
