namespace DropShield.Api.Behaviour;

public static class BehaviourScoreEvaluator
{
    public static BehaviourScore Evaluate(BehaviourEvidence evidence)
    {
        var score = 0;
        var reasons = new List<BehaviourReasonCode>();

        AddWhen(evidence.StockRequests >= 8, 20, BehaviourReasonCode.StockPolling);
        AddWhen(
            evidence.Requests >= 10 && evidence.StockRequests * 100 >= evidence.Requests * 80,
            10,
            BehaviourReasonCode.StockRequestRatio);
        AddWhen(evidence.RateLimited >= 3, 20, BehaviourReasonCode.RateLimitHistory);
        AddWhen(evidence.ReplayRejected >= 2, 25, BehaviourReasonCode.ReplayActivity);
        AddWhen(evidence.InvalidProof >= 3, 15, BehaviourReasonCode.InvalidProofActivity);
        AddWhen(evidence.Transactions >= 4, 10, BehaviourReasonCode.RapidTransactionPattern);

        var boundedScore = Math.Min(score, 100);
        return new BehaviourScore(
            boundedScore,
            boundedScore switch
            {
                >= 70 => BehaviourRiskLevel.High,
                >= 40 => BehaviourRiskLevel.Suspicious,
                >= 20 => BehaviourRiskLevel.Elevated,
                _ => BehaviourRiskLevel.Normal,
            },
            reasons);

        void AddWhen(bool condition, int contribution, BehaviourReasonCode reason)
        {
            if (!condition)
            {
                return;
            }

            score += contribution;
            reasons.Add(reason);
        }
    }
}

public sealed record BehaviourScore(
    int Value,
    BehaviourRiskLevel Level,
    IReadOnlyList<BehaviourReasonCode> Reasons);

public enum BehaviourRiskLevel
{
    Normal,
    Elevated,
    Suspicious,
    High,
}

public enum BehaviourReasonCode
{
    StockPolling,
    StockRequestRatio,
    RateLimitHistory,
    ReplayActivity,
    InvalidProofActivity,
    RapidTransactionPattern,
}
