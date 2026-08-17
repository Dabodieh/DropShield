<?php

declare(strict_types=1);

namespace DropShield\Connector\Test\Unit\Model;

use DropShield\Connector\Plugin\CartItemRepositoryPlugin;
use DropShield\Connector\Plugin\CartManagementPlugin;
use DropShield\Connector\Plugin\CheckoutAddProductToCartPlugin;
use DropShield\Connector\Plugin\QuoteGraphQlAddProductsToCartPlugin;
use PHPUnit\Framework\TestCase;

/**
 * Guards against H3-style drift: the ROUTE constants each plugin validates against must stay
 * byte-identical to the "route" claim DropShield.Api signs
 * (src/DropShield.Api/Traffic/TrafficRouteClassifier.cs, GetRouteTemplate) for the REST routes,
 * and to the documented literal for the GraphQL/storefront routes DropShield.Api does not yet
 * issue assertions for. Nothing else enforces this across languages, so all four are checked
 * against the single source of truth in contracts/origin-assertion-v1.json.
 */
final class OriginAssertionRouteContractTest extends TestCase
{
    public function testCartAndCheckoutRouteConstantsMatchTheSharedContract(): void
    {
        $routes = $this->loadContractRoutes();

        self::assertSame($routes['cart'], $this->readPrivateConstant(CartItemRepositoryPlugin::class, 'ROUTE'));
        self::assertSame($routes['checkout'], $this->readPrivateConstant(CartManagementPlugin::class, 'ROUTE'));
    }

    public function testGraphQlAndStorefrontRouteConstantsMatchTheSharedContract(): void
    {
        $routes = $this->loadContractRoutes();

        self::assertSame(
            $routes['graphqlCartAdd'],
            $this->readPrivateConstant(QuoteGraphQlAddProductsToCartPlugin::class, 'ROUTE')
        );
        self::assertSame(
            $routes['storefrontCartAdd'],
            $this->readPrivateConstant(CheckoutAddProductToCartPlugin::class, 'ROUTE')
        );
    }

    /**
     * @return array{cart: string, checkout: string, graphqlCartAdd: string, storefrontCartAdd: string}
     */
    private function loadContractRoutes(): array
    {
        $path = $this->findContractPath();
        $contract = json_decode((string) file_get_contents($path), true, flags: JSON_THROW_ON_ERROR);

        return [
            'cart' => (string) $contract['routes']['cart'],
            'checkout' => (string) $contract['routes']['checkout'],
            'graphqlCartAdd' => (string) $contract['routes']['graphqlCartAdd'],
            'storefrontCartAdd' => (string) $contract['routes']['storefrontCartAdd'],
        ];
    }

    private function findContractPath(): string
    {
        $directory = __DIR__;
        while ($directory !== dirname($directory)) {
            $candidate = $directory . '/contracts/origin-assertion-v1.json';
            if (is_file($candidate)) {
                return $candidate;
            }

            $directory = dirname($directory);
        }

        self::fail('Could not locate contracts/origin-assertion-v1.json from the test directory.');
    }

    private function readPrivateConstant(string $class, string $name): string
    {
        $reflection = new \ReflectionClass($class);

        return (string) $reflection->getConstant($name);
    }
}
