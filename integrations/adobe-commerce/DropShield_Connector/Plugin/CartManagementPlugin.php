<?php

declare(strict_types=1);

namespace DropShield\Connector\Plugin;

use DropShield\Connector\Model\OriginAssertionGuard;
use DropShield\Connector\Model\ProtectedDropResolver;
use Magento\Framework\App\RequestInterface;
use Magento\Quote\Api\CartManagementInterface;
use Magento\Quote\Api\CartRepositoryInterface;

/**
 * Intercepts CartManagementInterface::placeOrder, the service contract
 * every order-placement flow (REST, GraphQL placeOrder, and storefront
 * one-page checkout via PaymentInformationManagementInterface) ultimately
 * calls to convert a quote into an order.
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
        private readonly RequestInterface $request
    ) {
    }

    public function beforePlaceOrder(CartManagementInterface $subject, $cartId, $paymentMethod = null): void
    {
        $quote = $this->cartRepository->getActive($cartId);
        $skus = [];
        foreach ($quote->getAllVisibleItems() as $item) {
            $skus[] = (string) $item->getSku();
        }

        $protectedSku = null;
        foreach ($skus as $sku) {
            if ($this->dropResolver->isProtected($sku)) {
                $protectedSku = $sku;
                break;
            }
        }

        if ($protectedSku === null) {
            return;
        }

        $this->guard->requireValidAssertion($this->request, $protectedSku, self::ACTION, self::ROUTE);
    }
}
