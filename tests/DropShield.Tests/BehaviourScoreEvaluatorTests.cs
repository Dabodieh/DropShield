using DropShield.Api.Behaviour;
using DropShield.Api.Options;
using Microsoft.Extensions.Options;

namespace DropShield.Tests;

public sealed class BehaviourScoreEvaluatorTests
{
    [Fact]
    public void FixedEvidenceProducesExplainableBoundedScore()
    {
        var score = BehaviourScoreEvaluator.Evaluate(new BehaviourEvidence(
            Requests: 10,
            StockRequests: 8,
            RateLimited: 3,
            ReplayRejected: 2,
            InvalidProof: 3,
            Transactions: 4));

        Assert.Equal(100, score.Value);
        Assert.Equal(BehaviourRiskLevel.High, score.Level);
        Assert.Equal(
            [
                BehaviourReasonCode.StockPolling,
                BehaviourReasonCode.StockRequestRatio,
                BehaviourReasonCode.RateLimitHistory,
                BehaviourReasonCode.ReplayActivity,
                BehaviourReasonCode.InvalidProofActivity,
                BehaviourReasonCode.RapidTransactionPattern,
            ],
            score.Reasons);
    }

    [Fact]
    public async Task InMemoryEvidenceExpiresInsteadOfAccumulatingIndefinitely()
    {
        var time = new Support.TestTimeProvider(DateTimeOffset.Parse("2026-08-17T12:00:00Z"));
        var options = Options.Create(new DropShieldOptions
        {
            BehaviourScoring = new BehaviourScoringOptions
            {
                Enabled = true,
                ObservationWindowSeconds = 30,
                StateTtlSeconds = 60,
                MaximumInMemoryActors = 10,
                MaximumEventsPerActor = 16,
            },
        });
        var state = new InMemoryBehaviourState(time, options);

        for (var index = 0; index < 8; index++)
        {
            await state.RecordAsync("derived-actor", BehaviourEventType.StockRequest, CancellationToken.None);
        }

        Assert.Equal(8, (await state.GetAsync("derived-actor", CancellationToken.None)).StockRequests);

        time.Advance(TimeSpan.FromSeconds(30));

        Assert.Equal(BehaviourEvidence.Empty, await state.GetAsync("derived-actor", CancellationToken.None));
    }
}
