<?php

declare(strict_types=1);

namespace DropShield\Connector\Model;

use DropShield\Connector\Api\Data\ManifestInterface;
use DropShield\Connector\Api\ProtectionManifestInterface;
use DropShield\Connector\Model\Data\ActiveDrop;
use DropShield\Connector\Model\Data\Manifest;
use DropShield\Connector\Model\Data\ProtectedProduct;

class ProtectionManifest implements ProtectionManifestInterface
{
    public function __construct(private readonly ProtectedDropRepository $repository)
    {
    }

    public function get(): ManifestInterface
    {
        $drop = $this->repository->getActiveDrop();
        if ($drop === null) {
            return (new Manifest())->setVersion(1)->setGeneratedAt(gmdate('c'))->setActiveDrop(null);
        }

        $products = array_map(static fn (array $product): ProtectedProduct => (new ProtectedProduct())
            ->setProductId((int) $product['product_id'])
            ->setSku((string) $product['sku']), $this->repository->getActiveProducts());

        $activeDrop = (new ActiveDrop())->setId($drop->identifier)->setProducts($products);

        return (new Manifest())->setVersion(1)->setGeneratedAt(gmdate('c'))->setActiveDrop($activeDrop);
    }
}
