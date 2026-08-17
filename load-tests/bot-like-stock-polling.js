import { sleep } from 'k6';
import {
    PROTECTED_MODE,
    SUMMARY_TREND_STATS,
    durationEnvironmentVariable,
    getProfile,
    integerEnvironmentVariable,
    numberEnvironmentVariable,
} from './config.js';
import { getStock } from './lib/requests.js';

const profile = getProfile('polling');
const virtualUsers = integerEnvironmentVariable('VIRTUAL_USERS', profile.virtualUsers);
const duration = durationEnvironmentVariable('DURATION', profile.duration);
const pollIntervalSeconds = numberEnvironmentVariable('POLL_INTERVAL_SECONDS', 0, 0, 60);

export const options = {
    discardResponseBodies: true,
    summaryTrendStats: SUMMARY_TREND_STATS,
    tags: {
        test_id: PROTECTED_MODE
            ? 'BOT_LIKE_STOCK_POLLING_PROTECTED'
            : 'BOT_LIKE_STOCK_POLLING',
    },
    scenarios: {
        aggressive_stock_pollers: {
            executor: 'constant-vus',
            vus: virtualUsers,
            duration,
            gracefulStop: '5s',
            tags: { traffic_type: 'aggressive_stock_poller' },
        },
    },
    thresholds: {
        request_error_rate: ['rate==0'],
    },
};

export default function () {
    getStock({ traffic_type: 'aggressive_stock_poller' });

    if (pollIntervalSeconds > 0) {
        sleep(pollIntervalSeconds);
    }
}
