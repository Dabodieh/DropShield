<?php

declare(strict_types=1);

namespace DropShield\Connector\Model\Data;

use DropShield\Connector\Api\Data\ProtectedProductInterface;
use Magento\Framework\Api\AbstractSimpleObject;

class ProtectedProduct extends AbstractSimpleObject implements ProtectedProductInterface
{
    public function getProductId(): int
    {
        return (int) $this->_get('product_id');
    }

    public function setProductId(int $productId): self
    {
        return $this->setData('product_id', $productId);
    }

    public function getSku(): string
    {
        return (string) $this->_get('sku');
    }

    public function setSku(string $sku): self
    {
        return $this->setData('sku', $sku);
    }
}
