<?php

declare(strict_types=1);

namespace DropShield\Connector\Plugin;

use DropShield\Connector\Model\OriginAssertionGuard;
use DropShield\Connector\Model\ProtectedDropResolver;
use Magento\Framework\App\Request\Http as HttpRequest;
use Magento\Quote\Api\CartManagementInterface;
use Magento\Quote\Api\CartRepositoryInterface;

/**
 * Intercepts CartManagementInterface::placeOrder, the service contract
 * every order-placement flow (REST, GraphQL placeOrder, and storefront
 * one-page checkout via PaymentInformationManagementInterface) ultimately
 * calls to convert a quote into an order.
 *
 * Confirmed by runtime testing against Mage-OS 3.0.0 (see
 * docs/adobe-commerce.md): a protected checkout is rejected without a valid
 * assertion and succeeds with one, over both REST guest checkout and the
 * GraphQL placeOrder mutation.
 *
 * Only quotes containing a protected drop require a valid origin
 * assertion; ordinary checkouts are unaffected.
 */
class CartManagementPlugin
{
    private const ACTION = 'checkout';
    private const ROUTE = 'POST /api/checkout';

    public function __construct(
        private readonly CartRepositoryInterface $cartRepository,
        private readonly ProtectedDropResolver $dropResolver,
        private readonly OriginAssertionGuard $guard,
        private readonly HttpRequest $request
    ) {
    }

    public function beforePlaceOrder(CartManagementInterface $subject, $cartId, $paymentMethod = null): void
    {
        $quote = $this->cartRepository->getActive($cartId);
        $skus = [];
        foreach ($quote->getAllVisibleItems() as $item) {
            $skus[] = (string) $item->getSku();
        }

        $hasProtectedSku = false;
        foreach ($skus as $sku) {
            if ($this->dropResolver->isProtected($sku)) {
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
            self::ROUTE
        );
    }
}
