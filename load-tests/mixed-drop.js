import { sleep } from 'k6';
import {
    PROTECTED_MODE,
    PROFILE_NAME,
    SUMMARY_TREND_STATS,
    getProfile,
    integerEnvironmentVariable,
    numberEnvironmentVariable,
    randomBetween,
} from './config.js';
import { getProduct, getProducts, getStock, postCart } from './lib/requests.js';

const profile = getProfile('mixed');
const stageNames = ['A', 'B', 'C', 'D'];
const maximumStage = (__ENV.MAX_STAGE || 'D').trim().toUpperCase();
const maximumStageIndex = stageNames.indexOf(maximumStage);

if (maximumStageIndex < 0) {
    throw new Error('MAX_STAGE must be A, B, C, or D.');
}

const customerPercent = integerEnvironmentVariable('CUSTOMER_PERCENT', 70, 0, 100);
const pollerPercent = integerEnvironmentVariable('POLLER_PERCENT', 20, 0, 100);
const cartPercent = integerEnvironmentVariable('CART_PERCENT', 10, 0, 100);
const normalCustomerShare = numberEnvironmentVariable('NORMAL_CUSTOMER_SHARE', 0.70);
const customerCartProbability = numberEnvironmentVariable('CUSTOMER_CART_PROBABILITY', 0.10);
const pollIntervalSeconds = numberEnvironmentVariable('POLL_INTERVAL_SECONDS', 0, 0, 60);
const stageDurationSeconds = integerEnvironmentVariable(
    'STAGE_DURATION_SECONDS',
    profile.stageDurationSeconds,
    5,
    600);
const stageSlotSeconds = integerEnvironmentVariable(
    'STAGE_SLOT_SECONDS',
    Math.max(profile.stageSlotSeconds, stageDurationSeconds + 4),
    stageDurationSeconds + 1,
    900);

if (customerPercent + pollerPercent + cartPercent !== 100) {
    throw new Error('CUSTOMER_PERCENT, POLLER_PERCENT, and CART_PERCENT must total 100.');
}

const scenarios = {};
const thresholds = {
    request_error_rate: ['rate==0'],
};
const trafficTypes = [
    'normal_customer',
    'flash_customer',
    'aggressive_stock_poller',
    'cart_oriented',
];

for (const trafficType of trafficTypes) {
    thresholds[`incoming_requests{traffic_type:${trafficType}}`] = ['count>=0'];
    thresholds[`allowed_requests{traffic_type:${trafficType}}`] = ['count>=0'];
    thresholds[`rate_limited_requests{traffic_type:${trafficType}}`] = ['count>=0'];
    thresholds[`stock_incoming_requests{traffic_type:${trafficType}}`] = ['count>=0'];
    thresholds[`stock_allowed_requests{traffic_type:${trafficType}}`] = ['count>=0'];
    thresholds[`stock_rate_limited_requests{traffic_type:${trafficType}}`] = ['count>=0'];
}

for (let index = 0; index <= maximumStageIndex; index += 1) {
    const stage = stageNames[index];
    const configuredUsers = profile.stageUsers[index];
    const totalUsers = integerEnvironmentVariable(`STAGE_${stage}_VUS`, configuredUsers);
    const allocation = allocateUsers(totalUsers);
    const startTime = `${index * stageSlotSeconds}s`;

    addStageScenario(stage, 'customers', allocation.customers, startTime, 'mixedCustomerTraffic');
    addStageScenario(stage, 'pollers', allocation.pollers, startTime, 'mixedPollingTraffic');
    addStageScenario(stage, 'cart', allocation.cart, startTime, 'mixedCartTraffic');

    thresholds[`http_reqs{stage:${stage}}`] = ['count>=0'];
    if (!PROTECTED_MODE) {
        thresholds[`http_req_failed{stage:${stage}}`] = ['rate==0'];
    }
    thresholds[`http_req_duration{stage:${stage}}`] = ['p(99)<60000'];
    thresholds[`stock_request_duration{stage:${stage}}`] = ['p(99)<60000'];
    thresholds[`successful_requests{stage:${stage}}`] = ['count>=0'];
    thresholds[`failed_requests{stage:${stage}}`] = ['count>=0'];
}

export const options = {
    discardResponseBodies: true,
    summaryTrendStats: SUMMARY_TREND_STATS,
    tags: {
        test_id: PROTECTED_MODE ? 'POKEMON_DROP_PROTECTED' : 'POKEMON_DROP_BASELINE',
        profile: PROFILE_NAME,
    },
    scenarios,
    thresholds,
};

export function mixedCustomerTraffic() {
    const stage = __ENV.STAGE;
    if (Math.random() < normalCustomerShare) {
        normalCustomerJourney(stage);
    }
    else {
        flashCustomerJourney(stage);
    }
}

export function mixedPollingTraffic() {
    const tags = { stage: __ENV.STAGE, traffic_type: 'aggressive_stock_poller' };
    getStock(tags);

    if (pollIntervalSeconds > 0) {
        sleep(pollIntervalSeconds);
    }
}

export function mixedCartTraffic() {
    const tags = { stage: __ENV.STAGE, traffic_type: 'cart_oriented' };
    getProduct(tags);
    getStock(tags);
    postCart(tags);
    sleep(randomBetween(0.20, 0.60));
}

function normalCustomerJourney(stage) {
    const tags = { stage, traffic_type: 'normal_customer' };
    getProducts(tags);
    sleep(randomBetween(0.75, 1.50));
    getProduct(tags);
    sleep(randomBetween(0.75, 1.50));
    getStock(tags);

    if (Math.random() < customerCartProbability) {
        sleep(randomBetween(0.50, 1.00));
        postCart(tags);
    }

    sleep(randomBetween(0.75, 1.50));
}

function flashCustomerJourney(stage) {
    const tags = { stage, traffic_type: 'flash_customer' };
    getProduct(tags);
    sleep(randomBetween(0.10, 0.35));
    getStock(tags);

    if (Math.random() < customerCartProbability) {
        postCart(tags);
    }

    sleep(randomBetween(0.10, 0.50));
}

function allocateUsers(totalUsers) {
    const customers = Math.floor(totalUsers * customerPercent / 100);
    const pollers = Math.floor(totalUsers * pollerPercent / 100);
    const cart = totalUsers - customers - pollers;

    return { customers, pollers, cart };
}

function addStageScenario(stage, trafficName, virtualUsers, startTime, exec) {
    if (virtualUsers === 0) {
        return;
    }

    scenarios[`stage_${stage.toLowerCase()}_${trafficName}`] = {
        executor: 'constant-vus',
        exec,
        vus: virtualUsers,
        duration: `${stageDurationSeconds}s`,
        startTime,
        gracefulStop: '3s',
        env: { STAGE: stage },
        tags: { stage, traffic_type: trafficName },
    };
}
