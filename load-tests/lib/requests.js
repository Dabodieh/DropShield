import http from 'k6/http';
import { check } from 'k6';
import { PROTECTED_MODE, TARGET_BASE_URL } from '../config.js';
import { recordResponse, responseIsExpected } from './metrics.js';

export function getHealth(tags = {}) {
    return sendGet('/health', 'health', 200, tags);
}

export function getProducts(tags = {}) {
    return sendGet('/api/products', 'products', 200, tags);
}

export function getProduct(tags = {}) {
    return sendGet('/api/products/pokemon-etb', 'product', 200, tags);
}

export function getStock(tags = {}) {
    return sendGet('/api/products/pokemon-etb/stock', 'stock', 200, tags);
}

export function postCart(tags = {}) {
    const endpointTags = { ...tags, endpoint: 'cart', name: 'POST /api/cart' };
    const response = http.post(
        `${TARGET_BASE_URL}/api/cart`,
        null,
        requestParameters(endpointTags));

    check(response, {
        'POST /api/cart returns 202 or protected 429':
            (result) => responseIsExpected(result, 202),
    }, endpointTags);

    return recordResponse(response, 'cart', 202, tags);
}

function sendGet(path, endpoint, expectedStatus, tags) {
    const endpointTags = { ...tags, endpoint, name: `GET ${path}` };
    const response = http.get(`${TARGET_BASE_URL}${path}`, requestParameters(endpointTags));

    check(response, {
        [`GET ${path} returns ${expectedStatus} or protected 429`]:
            (result) => responseIsExpected(result, expectedStatus),
    }, endpointTags);

    return recordResponse(response, endpoint, expectedStatus, tags);
}

function requestParameters(tags) {
    const parameters = { tags };
    if (PROTECTED_MODE) {
        parameters.headers = {
            'X-DropShield-Test-Client': `k6-vu-${__VU}`,
        };
    }

    return parameters;
}
