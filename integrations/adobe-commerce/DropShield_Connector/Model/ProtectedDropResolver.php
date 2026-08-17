<?php

declare(strict_types=1);

namespace DropShield\Connector\Model;

/**
 * Determines whether a SKU is a DropShield-protected drop.
 *
 * Unprotected SKUs must behave exactly as normal Commerce catalogue items;
 * this is the single place that decision is made so plugins never
 * hard-code product identifiers.
 */
class ProtectedDropResolver
{
    public function __construct(private readonly Config $config)
    {
    }

    public function isProtected(string $sku, ?int $storeId = null): bool
    {
        if ($sku === '') {
            return false;
        }

        foreach ($this->config->getProtectedSkus($storeId) as $protectedSku) {
            if (strcasecmp($protectedSku, $sku) === 0) {
                return true;
            }
        }

        return false;
    }

    /**
     * @param string[] $skus
     */
    public function containsProtected(array $skus, ?int $storeId = null): bool
    {
        foreach ($skus as $sku) {
            if ($this->isProtected($sku, $storeId)) {
                return true;
            }
        }

        return false;
    }
}
