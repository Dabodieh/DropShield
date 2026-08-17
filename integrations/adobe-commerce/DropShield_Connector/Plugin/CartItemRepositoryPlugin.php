<?php

declare(strict_types=1);

namespace DropShield\Connector\Plugin;

use DropShield\Connector\Model\OriginAssertionGuard;
use DropShield\Connector\Model\ProtectedDropResolver;
use Magento\Framework\App\RequestInterface;
use Magento\Quote\Api\CartItemRepositoryInterface;
use Magento\Quote\Api\Data\CartItemInterface;

/**
 * Intercepts CartItemRepositoryInterface::save, the public service contract
 * behind both the REST cart-items endpoint and the GraphQL add-to-cart
 * resolvers, so a protected SKU requires a valid origin assertion regardless
 * of which storefront surface issued the mutation.
 *
 * This connector supports exactly one active protected drop (see
 * ProtectedDropResolver::getDropId()); every protected SKU maps to it.
 * Ordinary (unprotected) SKUs pass through untouched.
 */
class CartItemRepositoryPlugin
{
    private const ACTION = 'cart';
    private const ROUTE = 'POST /api/cart';

    public function __construct(
        private readonly ProtectedDropResolver $dropResolver,
        private readonly OriginAssertionGuard $guard,
        private readonly RequestInterface $request
    ) {
    }

    public function beforeSave(CartItemRepositoryInterface $subject, CartItemInterface $cartItem): void
    {
        $sku = (string) $cartItem->getSku();
        if (!$this->dropResolver->isProtected($sku)) {
            return;
        }

        $this->guard->requireValidAssertion(
            $this->request,
            $this->dropResolver->getDropId(),
            self::ACTION,
            self::ROUTE
        );
    }
}
