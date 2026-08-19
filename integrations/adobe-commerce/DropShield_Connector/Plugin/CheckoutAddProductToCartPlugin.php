<?php

declare(strict_types=1);

namespace DropShield\Connector\Plugin;

use DropShield\Connector\Model\OriginAssertionGuard;
use DropShield\Connector\Model\OriginAssertionRequestRoute;
use DropShield\Connector\Model\ProtectedDropResolver;
use Magento\Catalog\Model\Product;
use Magento\Checkout\Model\AddProductToCart;
use Magento\Checkout\Model\Cart;
use Magento\Framework\App\Request\Http as HttpRequest;

/**
 * Intercepts Magento\Checkout\Model\AddProductToCart::execute, the class the storefront
 * checkout/cart/add controller uses. Like GraphQL (see
 * QuoteGraphQlAddProductsToCartPlugin), the storefront controller never calls
 * CartItemRepositoryInterface::save — it calls this class directly, which mutates the quote
 * through Cart::addProduct() and saves it. A separate, distinct class from the GraphQL path,
 * so it needs its own extension point.
 *
 * The assertion's route claim is bound to the real storefront add-to-cart endpoint; its body
 * claim is bound to the real raw request body (the storefront form POST), not a synthesized
 * cart-item payload.
 */
class CheckoutAddProductToCartPlugin
{
    private const ACTION = 'cart';

    public function __construct(
        private readonly ProtectedDropResolver $dropResolver,
        private readonly OriginAssertionGuard $guard,
        private readonly HttpRequest $request
    ) {
    }

    /**
     * @param array<string, mixed> $buyRequest
     * @param int[] $related
     */
    public function beforeExecute(
        AddProductToCart $subject,
        Cart $cart,
        Product $product,
        array $buyRequest = [],
        array $related = []
    ): void {
        $sku = (string) $product->getSku();
        if ($sku === '' || !$this->dropResolver->isProtected($sku)) {
            return;
        }

        $this->guard->requireValidAssertion(
            $this->request,
            $this->dropResolver->getDropId(),
            self::ACTION,
            OriginAssertionRequestRoute::fromRequest($this->request)
        );
    }
}
