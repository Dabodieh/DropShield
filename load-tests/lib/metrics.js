import { Counter, Rate, Trend } from 'k6/metrics';
import { PROTECTED_MODE } from '../config.js';

export const successfulRequests = new Counter('successful_requests');
export const failedRequests = new Counter('failed_requests');
export const requestErrorRate = new Rate('request_error_rate');
export const incomingRequests = new Counter('incoming_requests');
export const allowedRequests = new Counter('allowed_requests');
export const rateLimitedRequests = new Counter('rate_limited_requests');
export const rateLimitedRate = new Rate('rate_limited_rate');
export const allowedRequestDuration = new Trend('allowed_request_duration', true);
export const rejectedRequestDuration = new Trend('rejected_request_duration', true);
export const stockIncomingRequests = new Counter('stock_incoming_requests');
export const stockAllowedRequests = new Counter('stock_allowed_requests');
export const stockRateLimitedRequests = new Counter('stock_rate_limited_requests');
export const allowedStockRequestDuration = new Trend('allowed_stock_request_duration', true);
export const rejectedStockRequestDuration = new Trend('rejected_stock_request_duration', true);

export const healthRequestDuration = new Trend('health_request_duration', true);
export const productsRequestDuration = new Trend('products_request_duration', true);
export const productRequestDuration = new Trend('product_request_duration', true);
export const stockRequestDuration = new Trend('stock_request_duration', true);
export const cartRequestDuration = new Trend('cart_request_duration', true);

const endpointMetrics = {
    health: healthRequestDuration,
    products: productsRequestDuration,
    product: productRequestDuration,
    stock: stockRequestDuration,
    cart: cartRequestDuration,
};

export function recordResponse(response, endpoint, expectedStatus, metricTags = {}) {
    const allowed = response.status === expectedStatus;
    const rateLimited = PROTECTED_MODE && response.status === 429;
    const unexpected = !allowed && !rateLimited;

    incomingRequests.add(1, metricTags);
    allowedRequests.add(allowed ? 1 : 0, metricTags);
    rateLimitedRequests.add(rateLimited ? 1 : 0, metricTags);
    rateLimitedRate.add(rateLimited, metricTags);
    requestErrorRate.add(unexpected, metricTags);
    successfulRequests.add(allowed ? 1 : 0, metricTags);
    failedRequests.add(unexpected ? 1 : 0, metricTags);

    if (allowed) {
        allowedRequestDuration.add(response.timings.duration, metricTags);
    }

    if (rateLimited) {
        rejectedRequestDuration.add(response.timings.duration, metricTags);
    }

    if (endpoint === 'stock') {
        stockIncomingRequests.add(1, metricTags);
        stockAllowedRequests.add(allowed ? 1 : 0, metricTags);
        stockRateLimitedRequests.add(rateLimited ? 1 : 0, metricTags);

        if (allowed) {
            allowedStockRequestDuration.add(response.timings.duration, metricTags);
        }

        if (rateLimited) {
            rejectedStockRequestDuration.add(response.timings.duration, metricTags);
        }
    }

    const endpointMetric = endpointMetrics[endpoint];
    if (endpointMetric) {
        endpointMetric.add(response.timings.duration, metricTags);
    }

    return response;
}

export function responseIsExpected(response, expectedStatus) {
    return response.status === expectedStatus ||
        (PROTECTED_MODE && response.status === 429);
}
