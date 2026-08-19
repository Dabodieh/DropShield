<?php

declare(strict_types=1);

namespace DropShield\Connector\Plugin;

use DropShield\Connector\Model\OriginAssertionGuard;
use DropShield\Connector\Model\OriginAssertionRequestRoute;
use DropShield\Connector\Model\ProtectedDropResolver;
use Magento\Framework\App\Request\Http as HttpRequest;
use Magento\Quote\Model\Quote;
use Magento\QuoteGraphQl\Model\Cart\AddProductsToCart;

/**
 * Intercepts Magento\QuoteGraphQl\Model\Cart\AddProductsToCart::execute, the class the
 * addSimpleProductsToCart/addVirtualProductsToCart GraphQL resolvers use to add items to
 * a quote. GraphQL never calls CartItemRepositoryInterface::save (see
 * CartItemRepositoryPlugin) — it mutates the Quote object directly and saves it through
 * CartRepositoryInterface, so it needs its own extension point rather than reusing the REST
 * plugin's.
 *
 * The assertion's route claim is bound to the real GraphQL endpoint ("POST /graphql"), not a
 * fabricated REST-shaped route, and its body claim is bound to the real raw GraphQL request
 * body — the query/variables JSON, not a synthesized cart-item payload. This is a genuinely
 * different HTTP request shape than the REST path; see docs/adobe-commerce.md for how
 * DropShield.Api issues an assertion shaped this way (TrafficRoute.GraphQlCartAdd).
 */
class QuoteGraphQlAddProductsToCartPlugin
{
    private const ACTION = 'cart';

    public function __construct(
        private readonly ProtectedDropResolver $dropResolver,
        private readonly OriginAssertionGuard $guard,
        private readonly HttpRequest $request
    ) {
    }

    /**
     * @param array<int, array<string, mixed>> $cartItems
     */
    public function beforeExecute(AddProductsToCart $subject, Quote $cart, array $cartItems): void
    {
        $hasProtectedSku = false;
        foreach ($cartItems as $cartItemData) {
            $sku = (string) ($cartItemData['parent_sku'] ?? $cartItemData['data']['sku'] ?? '');
            if ($sku !== '' && $this->dropResolver->isProtected($sku)) {
                $hasProtectedSku = true;
                break;
            }
        }

        if (!$hasProtectedSku) {
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
