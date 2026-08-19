<?php

declare(strict_types=1);

namespace DropShield\Connector\Model\Data;

use DropShield\Connector\Api\Data\ActiveDropInterface;
use DropShield\Connector\Api\Data\ManifestInterface;
use Magento\Framework\Api\AbstractSimpleObject;

class Manifest extends AbstractSimpleObject implements ManifestInterface
{
    public function getVersion(): int
    {
        return (int) $this->_get('version');
    }

    public function setVersion(int $version): self
    {
        return $this->setData('version', $version);
    }

    public function getGeneratedAt(): string
    {
        return (string) $this->_get('generated_at');
    }

    public function setGeneratedAt(string $generatedAt): self
    {
        return $this->setData('generated_at', $generatedAt);
    }

    public function getActiveDrop(): ?ActiveDropInterface
    {
        return $this->_get('active_drop');
    }

    public function setActiveDrop(?ActiveDropInterface $activeDrop): self
    {
        return $this->setData('active_drop', $activeDrop);
    }
}
