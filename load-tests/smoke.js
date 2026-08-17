import { check, group } from 'k6';
import { PROTECTED_MODE, SUMMARY_TREND_STATS } from './config.js';
import { getHealth, getProduct, getProducts, getStock } from './lib/requests.js';

export const options = {
    discardResponseBodies: false,
    iterations: 1,
    vus: 1,
    summaryTrendStats: SUMMARY_TREND_STATS,
    tags: { test_id: 'SMOKE' },
    thresholds: {
        checks: ['rate==1'],
        request_error_rate: ['rate==0'],
        http_req_duration: ['p(95)<1000'],
    },
};

export default function () {
    group('load-test harness smoke check', () => {
        const health = getHealth({ traffic_type: 'smoke' });
        check(health, {
            'health payload identifies target service': (response) =>
                response.json('service') ===
                (PROTECTED_MODE ? 'DropShield.Api' : 'DropShield.DemoStore'),
        });

        const products = getProducts({ traffic_type: 'smoke' });
        check(products, {
            'product list contains Pokémon ETB': (response) => response.json('0.id') === 'pokemon-etb',
        });

        const product = getProduct({ traffic_type: 'smoke' });
        check(product, {
            'product detail contains expected id': (response) => response.json('id') === 'pokemon-etb',
        });

        const stock = getStock({ traffic_type: 'smoke' });
        check(stock, {
            'stock payload reports 500 available': (response) => response.json('available') === 500,
        });
    });
}
