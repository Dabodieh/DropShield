using DropShield.Api.Options;
using DropShield.Api.Traffic;
using Microsoft.Extensions.Options;

namespace DropShield.Api.Behaviour;

public sealed class BehaviourActivityRecorder(
    BehaviourIdentityProvider identityProvider,
    IBehaviourState state,
    TrafficMetrics metrics,
    IOptions<DropShieldOptions> options,
    ILogger<BehaviourActivityRecorder> logger)
{
    private readonly BehaviourScoringOptions _options = options.Value.BehaviourScoring;

    public async Task RecordAsync(
        HttpContext context,
        BehaviourEventType eventType,
        CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return;
        }

        try
        {
            await state.RecordAsync(identityProvider.GetActor(context), eventType, cancellationToken);
            metrics.RecordBehaviourObservation(eventType);
        }
        catch (BehaviourStateUnavailableException exception)
        {
            metrics.RecordBehaviourStateFailure();
            logger.LogWarning(exception, "Behaviour state unavailable; recording skipped");
        }
    }
}
