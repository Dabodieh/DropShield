<?php

declare(strict_types=1);

namespace DropShield\Connector\Api\Data;

/**
 * The DropShield protection manifest: the active protected drop (if any) and its assigned
 * products. No catalogue, price, stock, customer, or secret data is ever included.
 */
interface ManifestInterface
{
    /**
     * @return int
     */
    public function getVersion(): int;

    /**
     * @return string
     */
    public function getGeneratedAt(): string;

    /**
     * @return \DropShield\Connector\Api\Data\ActiveDropInterface|null
     */
    public function getActiveDrop(): ?ActiveDropInterface;
}
