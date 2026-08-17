<?php

declare(strict_types=1);

namespace DropShield\Connector\Plugin;

use DropShield\Connector\Model\OriginAssertionGuard;
use DropShield\Connector\Model\ProtectedDropResolver;
use Magento\Framework\App\Request\Http as HttpRequest;
use Magento\Quote\Api\CartItemRepositoryInterface;
use Magento\Quote\Api\Data\CartItemInterface;

/**
 * Intercepts CartItemRepositoryInterface::save, the public service contract
 * behind the REST cart-items endpoint, so a protected SKU requires a valid
 * origin assertion when a mutation reaches this specific service contract.
 *
 * Confirmed by runtime testing against Mage-OS 3.0.0 (see
 * docs/adobe-commerce.md, "Verified against a live Magento instance"):
 * GraphQL's addSimpleProductsToCart and the storefront cart-add controller
 * both call Quote::addProduct() directly and never reach
 * CartItemRepositoryInterface::save, so neither surface is covered by this
 * plugin. Treat GraphQL and storefront cart-add as unprotected until a
 * separate extension point is added for them.
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
        private readonly HttpRequest $request
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
