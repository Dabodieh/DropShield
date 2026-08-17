import { sleep } from 'k6';
import {
    PROTECTED_MODE,
    SUMMARY_TREND_STATS,
    durationEnvironmentVariable,
    getProfile,
    integerEnvironmentVariable,
    numberEnvironmentVariable,
    randomBetween,
} from './config.js';
import { getProduct, getStock, postCart } from './lib/requests.js';

const profile = getProfile('flash');
const baselineUsers = integerEnvironmentVariable('BASELINE_VUS', profile.baselineUsers);
const peakUsers = integerEnvironmentVariable('VIRTUAL_USERS', profile.peakUsers);
const baselineDuration = durationEnvironmentVariable('BASELINE_DURATION', profile.baselineDuration);
const rampDuration = durationEnvironmentVariable('RAMP_DURATION', profile.rampDuration);
const peakDuration = durationEnvironmentVariable('DURATION', profile.peakDuration);
const cooldownDuration = durationEnvironmentVariable('COOLDOWN_DURATION', profile.cooldownDuration);
const cartProbability = numberEnvironmentVariable('CART_PROBABILITY', 0.15);

if (peakUsers < baselineUsers) {
    throw new Error('VIRTUAL_USERS must be greater than or equal to BASELINE_VUS.');
}

export const options = {
    discardResponseBodies: true,
    summaryTrendStats: SUMMARY_TREND_STATS,
    tags: { test_id: PROTECTED_MODE ? 'FLASH_CROWD_PROTECTED' : 'FLASH_CROWD' },
    scenarios: {
        launch_crowd: {
            executor: 'ramping-vus',
            startVUs: baselineUsers,
            stages: [
                { duration: baselineDuration, target: baselineUsers },
                { duration: rampDuration, target: peakUsers },
                { duration: peakDuration, target: peakUsers },
                { duration: cooldownDuration, target: 0 },
            ],
            gracefulRampDown: '5s',
            tags: { traffic_type: 'flash_customer' },
        },
    },
    thresholds: {
        request_error_rate: ['rate==0'],
    },
};

export default function () {
    const tags = { traffic_type: 'flash_customer' };

    getProduct(tags);
    sleep(randomBetween(0.10, 0.50));
    getStock(tags);

    if (Math.random() < cartProbability) {
        sleep(randomBetween(0.10, 0.40));
        postCart(tags);
    }

    sleep(randomBetween(0.15, 0.75));
}
