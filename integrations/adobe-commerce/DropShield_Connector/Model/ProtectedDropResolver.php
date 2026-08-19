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
    public function __construct(private readonly ProtectedDropRepository $repository)
    {
    }

    public function isProtected(string $sku, ?int $storeId = null): bool
    {
        if ($sku === '') {
            return false;
        }

        return $this->resolveForSku($sku, $storeId) !== null;
    }

    public function resolveForSku(string $sku, ?int $storeId = null): ?ProtectedDrop
    {
        if ($sku === '') {
            return null;
        }

        $drop = $this->repository->getActiveDrop();
        if ($drop === null) {
            return null;
        }

        return $this->repository->activeDropContainsSku($drop, $sku) ? $drop : null;
    }

    /**
     * The drop identifier every protected SKU maps to. This connector supports exactly one
     * active protected drop at a time (matching DropShield.Api's single-drop admission
     * model) rather than one drop per SKU.
     */
    public function getActiveDrop(?int $storeId = null): ?ProtectedDrop
    {
        return $this->repository->getActiveDrop();
    }

    public function getDropId(?int $storeId = null): string
    {
        return $this->getActiveDrop($storeId)?->identifier ?? '';
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
