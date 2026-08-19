<?php

declare(strict_types=1);

namespace DropShield\Connector\Api\Data;

/**
 * A single protected-drop product entry in the protection manifest. Deliberately carries only
 * the Magento product entity ID and SKU — no catalogue, price, stock, or PII data.
 */
interface ProtectedProductInterface
{
    /**
     * @return int
     */
    public function getProductId(): int;

    /**
     * @return string
     */
    public function getSku(): string;
}
