<?php

declare(strict_types=1);

namespace DropShield\Connector\Plugin;

use DropShield\Connector\Model\OriginAssertionGuard;
use DropShield\Connector\Model\OriginAssertionRequestRoute;
use DropShield\Connector\Model\ProtectedDropResolver;
use Magento\Framework\App\Request\Http as HttpRequest;
use Magento\QuoteGraphQl\Model\AddProductsToCart;

/**
 * Guards Mage-OS' current addProductsToCart service. This is intentionally separate from
 * QuoteGraphQlAddProductsToCartPlugin: Mage-OS 3.0.0 routes addSimpleProductsToCart through
 * Magento\QuoteGraphQl\Model\Cart\AddProductsToCart, while addProductsToCart uses this class.
 */
class ModernGraphQlAddProductsToCartPlugin
{
    private const ACTION = 'cart';

    public function __construct(
        private readonly ProtectedDropResolver $dropResolver,
        private readonly OriginAssertionGuard $guard,
        private readonly HttpRequest $request
    ) {
    }

    /**
     * @param array<string, mixed>|null $args
     */
    public function beforeExecute(AddProductsToCart $subject, $context, ?array $args): void
    {
        foreach (($args['cartItems'] ?? []) as $cartItem) {
            if (!is_array($cartItem)) {
                continue;
            }

            $sku = (string) ($cartItem['parent_sku'] ?? $cartItem['sku'] ?? '');
            if ($sku === '' || !$this->dropResolver->isProtected($sku)) {
                continue;
            }

            $this->guard->requireValidAssertion(
                $this->request,
                $this->dropResolver->getDropId(),
                self::ACTION,
                OriginAssertionRequestRoute::fromRequest($this->request)
            );

            return;
        }
    }
}
