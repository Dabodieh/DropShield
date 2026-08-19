<?php

declare(strict_types=1);

namespace DropShield\Connector\Test\Unit\Plugin;

use DropShield\Connector\Model\OriginAssertionGuard;
use DropShield\Connector\Model\ProtectedDropResolver;
use DropShield\Connector\Plugin\ModernGraphQlAddProductsToCartPlugin;
use Magento\Framework\App\Request\Http as HttpRequest;
use Magento\QuoteGraphQl\Model\AddProductsToCart;
use PHPUnit\Framework\TestCase;

final class ModernGraphQlAddProductsToCartPluginTest extends TestCase
{
    public function testProtectedSkuInvokesGuardForTheActualGraphQlRoute(): void
    {
        $dropResolver = $this->createMock(ProtectedDropResolver::class);
        $dropResolver->method('isProtected')->willReturnMap([['pokemon-etb', null, true]]);
        $dropResolver->method('getDropId')->willReturn('configured-drop-id');
        $request = $this->createMock(HttpRequest::class);
        $request->method('getPathInfo')->willReturn('/graphql');
        $request->method('getMethod')->willReturn('POST');
        $guard = $this->createMock(OriginAssertionGuard::class);
        $guard->expects(self::once())
            ->method('requireValidAssertion')
            ->with($request, 'configured-drop-id', 'cart', 'POST /graphql');

        $plugin = new ModernGraphQlAddProductsToCartPlugin($dropResolver, $guard, $request);
        $plugin->beforeExecute($this->createMock(AddProductsToCart::class), null, [
            'cartItems' => [['sku' => 'pokemon-etb', 'quantity' => 1]],
        ]);
    }

    public function testOrdinarySkuDoesNotInvokeGuard(): void
    {
        $dropResolver = $this->createMock(ProtectedDropResolver::class);
        $dropResolver->method('isProtected')->willReturn(false);
        $request = $this->createMock(HttpRequest::class);
        $guard = $this->createMock(OriginAssertionGuard::class);
        $guard->expects(self::never())->method('requireValidAssertion');

        $plugin = new ModernGraphQlAddProductsToCartPlugin($dropResolver, $guard, $request);
        $plugin->beforeExecute($this->createMock(AddProductsToCart::class), null, [
            'cartItems' => [['sku' => 'regular-mug', 'quantity' => 1]],
        ]);
    }
}
