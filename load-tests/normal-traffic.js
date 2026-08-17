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
import { getProduct, getProducts, getStock, postCart } from './lib/requests.js';

const profile = getProfile('normal');
const virtualUsers = integerEnvironmentVariable('VIRTUAL_USERS', profile.virtualUsers);
const duration = durationEnvironmentVariable('DURATION', profile.duration);
const cartProbability = numberEnvironmentVariable('CART_PROBABILITY', 0.30);
const minimumPause = numberEnvironmentVariable('MIN_PAUSE_SECONDS', 1, 0, 60);
const maximumPause = numberEnvironmentVariable('MAX_PAUSE_SECONDS', 3, 0, 60);

if (maximumPause < minimumPause) {
    throw new Error('MAX_PAUSE_SECONDS must be greater than or equal to MIN_PAUSE_SECONDS.');
}

export const options = {
    discardResponseBodies: true,
    summaryTrendStats: SUMMARY_TREND_STATS,
    tags: { test_id: PROTECTED_MODE ? 'NORMAL_TRAFFIC_PROTECTED' : 'NORMAL_TRAFFIC' },
    scenarios: {
        normal_customers: {
            executor: 'constant-vus',
            vus: virtualUsers,
            duration,
            gracefulStop: '5s',
            tags: { traffic_type: 'normal_customer' },
        },
    },
    thresholds: {
        request_error_rate: ['rate==0'],
    },
};

export default function () {
    const tags = { traffic_type: 'normal_customer' };

    getProducts(tags);
    humanPause();
    getProduct(tags);
    humanPause();
    getStock(tags);
    humanPause();

    if (Math.random() < cartProbability) {
        postCart(tags);
        humanPause();
    }
}

function humanPause() {
    sleep(randomBetween(minimumPause, maximumPause));
}
