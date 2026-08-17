<?php

declare(strict_types=1);

namespace DropShield\Connector\Test\Unit\Plugin;

use DropShield\Connector\Model\OriginAssertionGuard;
use DropShield\Connector\Model\ProtectedDropResolver;
use DropShield\Connector\Plugin\QuoteGraphQlAddProductsToCartPlugin;
use Magento\Framework\App\Request\Http as HttpRequest;
use Magento\Quote\Model\Quote;
use Magento\QuoteGraphQl\Model\Cart\AddProductsToCart;
use PHPUnit\Framework\TestCase;

/**
 * GraphQL cart-add never reaches CartItemRepositoryInterface::save (see
 * CartItemRepositoryPlugin's docblock), so it needs its own plugin. These tests prove that
 * plugin routes protected drops through the shared OriginAssertionGuard using the configured
 * drop ID (not the raw SKU), and leaves ordinary SKUs untouched.
 */
final class QuoteGraphQlAddProductsToCartPluginTest extends TestCase
{
    public function testProtectedSkuInCartItemsInvokesGuardWithConfiguredDropId(): void
    {
        $dropResolver = $this->createMock(ProtectedDropResolver::class);
        $dropResolver->method('isProtected')->willReturnMap([
            ['pokemon-etb', null, true],
            ['regular-mug', null, false],
        ]);
        $dropResolver->method('getDropId')->willReturn('configured-drop-id');

        $request = $this->createMock(HttpRequest::class);
        $guard = $this->createMock(OriginAssertionGuard::class);
        $guard->expects(self::once())
            ->method('requireValidAssertion')
            ->with($request, 'configured-drop-id', 'cart', 'POST /graphql');

        $plugin = new QuoteGraphQlAddProductsToCartPlugin($dropResolver, $guard, $request);
        $subject = $this->createMock(AddProductsToCart::class);
        $cart = $this->createMock(Quote::class);

        $plugin->beforeExecute($subject, $cart, [
            ['data' => ['sku' => 'regular-mug', 'quantity' => 1]],
            ['data' => ['sku' => 'pokemon-etb', 'quantity' => 1]],
        ]);
    }

    public function testOnlyOrdinarySkusNeverInvokesGuard(): void
    {
        $dropResolver = $this->createMock(ProtectedDropResolver::class);
        $dropResolver->method('isProtected')->willReturn(false);

        $request = $this->createMock(HttpRequest::class);
        $guard = $this->createMock(OriginAssertionGuard::class);
        $guard->expects(self::never())->method('requireValidAssertion');

        $plugin = new QuoteGraphQlAddProductsToCartPlugin($dropResolver, $guard, $request);
        $subject = $this->createMock(AddProductsToCart::class);
        $cart = $this->createMock(Quote::class);

        $plugin->beforeExecute($subject, $cart, [
            ['data' => ['sku' => 'regular-mug', 'quantity' => 1]],
        ]);
        $this->addToAssertionCount(1);
    }

    public function testConfigurableProductUsesParentSku(): void
    {
        $dropResolver = $this->createMock(ProtectedDropResolver::class);
        $dropResolver->method('isProtected')->willReturnMap([
            ['pokemon-etb', null, true],
        ]);
        $dropResolver->method('getDropId')->willReturn('configured-drop-id');

        $request = $this->createMock(HttpRequest::class);
        $guard = $this->createMock(OriginAssertionGuard::class);
        $guard->expects(self::once())->method('requireValidAssertion');

        $plugin = new QuoteGraphQlAddProductsToCartPlugin($dropResolver, $guard, $request);
        $subject = $this->createMock(AddProductsToCart::class);
        $cart = $this->createMock(Quote::class);

        $plugin->beforeExecute($subject, $cart, [
            ['parent_sku' => 'pokemon-etb', 'data' => ['sku' => 'pokemon-etb-variant-1']],
        ]);
    }
}
