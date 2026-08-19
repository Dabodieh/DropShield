<?php

declare(strict_types=1);

namespace DropShield\Connector\Test\Unit\Model;

use DropShield\Connector\Model\OriginAssertionRequestRoute;
use Magento\Framework\App\Request\Http as HttpRequest;
use PHPUnit\Framework\TestCase;

/**
 * Commerce REST route claims are bound to the exact incoming path. This protects the opaque
 * cart identifier rather than validating against a fabricated static endpoint.
 */
final class OriginAssertionRouteContractTest extends TestCase
{
    public function testCommerceRouteTemplatesDocumentTheConcreteClaimShape(): void
    {
        $routes = $this->loadContractRoutes();

        self::assertSame('POST /rest[/default]/V1/guest-carts/{cartId}/items', $routes['commerceRestCartTemplate']);
        self::assertSame(
            'POST /rest[/default]/V1/guest-carts/{cartId}/payment-information',
            $routes['commerceRestCheckoutTemplate']
        );
    }

    public function testRequestRouteUsesTheActualMethodAndPath(): void
    {
        $request = $this->createMock(HttpRequest::class);
        $request->method('getMethod')->willReturn('post');
        $request->method('getPathInfo')->willReturn('/rest/default/V1/guest-carts/masked-cart/items');

        self::assertSame(
            'POST /rest/default/V1/guest-carts/masked-cart/items',
            OriginAssertionRequestRoute::fromRequest($request)
        );
    }

    /**
     * @return array{commerceRestCartTemplate: string, commerceRestCheckoutTemplate: string}
     */
    private function loadContractRoutes(): array
    {
        $path = $this->findContractPath();
        $contract = json_decode((string) file_get_contents($path), true, flags: JSON_THROW_ON_ERROR);

        return [
            'commerceRestCartTemplate' => (string) $contract['routes']['commerceRestCartTemplate'],
            'commerceRestCheckoutTemplate' => (string) $contract['routes']['commerceRestCheckoutTemplate'],
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

}
