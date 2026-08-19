<?php

declare(strict_types=1);

namespace DropShield\Connector\Test\Unit\Plugin;

use DropShield\Connector\Model\OriginAssertionGuard;
use DropShield\Connector\Model\ProtectedDropResolver;
use DropShield\Connector\Plugin\CheckoutAddProductToCartPlugin;
use Magento\Catalog\Model\Product;
use Magento\Checkout\Model\AddProductToCart;
use Magento\Checkout\Model\Cart;
use Magento\Framework\App\Request\Http as HttpRequest;
use PHPUnit\Framework\TestCase;

/**
 * The storefront add-to-cart controller never reaches CartItemRepositoryInterface::save (see
 * CartItemRepositoryPlugin's docblock) or the GraphQL path — it calls
 * Magento\Checkout\Model\AddProductToCart directly, so it needs its own plugin.
 */
final class CheckoutAddProductToCartPluginTest extends TestCase
{
    public function testProtectedSkuInvokesGuardWithConfiguredDropId(): void
    {
        $dropResolver = $this->createMock(ProtectedDropResolver::class);
        $dropResolver->method('isProtected')->with('pokemon-etb')->willReturn(true);
        $dropResolver->method('getDropId')->willReturn('configured-drop-id');

        $request = $this->createMock(HttpRequest::class);
        $request->method('getPathInfo')->willReturn('/checkout/cart/add');
        $request->method('getMethod')->willReturn('POST');
        $guard = $this->createMock(OriginAssertionGuard::class);
        $guard->expects(self::once())
            ->method('requireValidAssertion')
            ->with($request, 'configured-drop-id', 'cart', 'POST /checkout/cart/add');

        $plugin = new CheckoutAddProductToCartPlugin($dropResolver, $guard, $request);
        $subject = $this->createMock(AddProductToCart::class);
        $cart = $this->createMock(Cart::class);
        $product = $this->createMock(Product::class);
        $product->method('getSku')->willReturn('pokemon-etb');

        $plugin->beforeExecute($subject, $cart, $product, ['qty' => 1], []);
    }

    public function testOrdinarySkuNeverInvokesGuard(): void
    {
        $dropResolver = $this->createMock(ProtectedDropResolver::class);
        $dropResolver->method('isProtected')->with('regular-mug')->willReturn(false);

        $request = $this->createMock(HttpRequest::class);
        $guard = $this->createMock(OriginAssertionGuard::class);
        $guard->expects(self::never())->method('requireValidAssertion');

        $plugin = new CheckoutAddProductToCartPlugin($dropResolver, $guard, $request);
        $subject = $this->createMock(AddProductToCart::class);
        $cart = $this->createMock(Cart::class);
        $product = $this->createMock(Product::class);
        $product->method('getSku')->willReturn('regular-mug');

        $plugin->beforeExecute($subject, $cart, $product, ['qty' => 1], []);
        $this->addToAssertionCount(1);
    }
}
