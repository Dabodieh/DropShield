<?php

declare(strict_types=1);

namespace DropShield\Connector\Model\Data;

use DropShield\Connector\Api\Data\ActiveDropInterface;
use Magento\Framework\Api\AbstractSimpleObject;

class ActiveDrop extends AbstractSimpleObject implements ActiveDropInterface
{
    public function getId(): string
    {
        return (string) $this->_get('id');
    }

    public function setId(string $id): self
    {
        return $this->setData('id', $id);
    }

    /**
     * @return \DropShield\Connector\Api\Data\ProtectedProductInterface[]
     */
    public function getProducts(): array
    {
        return $this->_get('products') ?? [];
    }

    /**
     * @param \DropShield\Connector\Api\Data\ProtectedProductInterface[] $products
     */
    public function setProducts(array $products): self
    {
        return $this->setData('products', $products);
    }
}
